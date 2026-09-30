[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dictionaryPath = Join-Path $root 'assets\i18n\en.json'
$avaloniaXamlPath = Join-Path $root 'dotnet\AstroForge.CrossPlatform\MainWindow.axaml'
$avaloniaCodePath = Join-Path $root 'dotnet\AstroForge.CrossPlatform\MainWindow.axaml.cs'
$avaloniaAdapterPath = Join-Path $root 'dotnet\AstroForge.CrossPlatform\AvaloniaLocalizationAdapter.cs'
$installerPath = Join-Path $root 'installer\AstroProjectForge.iss'
$sharedPath = Join-Path $root 'dotnet\AstroForge.CrossPlatform\Shared'
$viewModelPath = Join-Path $sharedPath 'ViewModels\MainViewModel.cs'
$projectStorePath = Join-Path $sharedPath 'Services\ProjectDocumentStore.cs'
$appStatePath = Join-Path $sharedPath 'Services\AppStateStore.cs'
$qualityAnalyzerPath = Join-Path $root 'dotnet\AstroForge.Core\Quality\FitsQualityAnalyzer.cs'

function Read-Raw([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "File mancante: $Path" }
    Get-Content -LiteralPath $Path -Raw -Encoding utf8
}

$dictionaryText = Read-Raw $dictionaryPath
$dictionaryKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($match in [regex]::Matches($dictionaryText, '(?m)^\s*,?\s*"((?:[^"\\]|\\.)+)"\s*:')) {
    [void]$dictionaryKeys.Add([Text.RegularExpressions.Regex]::Unescape($match.Groups[1].Value))
}

$uiAttributes = 'Text|Content|Header|ToolTip|ToolTip\.Tip|Title'
$literalValues = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in @($avaloniaXamlPath)) {
    $xaml = Read-Raw $path
    foreach ($match in [regex]::Matches($xaml, "(?:$uiAttributes)\s*=\s*`"([^`"]*)`"")) {
        $value = [Net.WebUtility]::HtmlDecode($match.Groups[1].Value).Trim()
        if ($value -and -not $value.StartsWith('{') -and ([regex]::Matches($value, '\p{L}').Count -ge 2)) {
            [void]$literalValues.Add($value)
        }
    }
}

$missing = @($literalValues | Where-Object { -not $dictionaryKeys.Contains($_) } | Sort-Object)
if ($missing.Count -gt 0) {
    throw "Traduzioni inglesi mancanti ($($missing.Count)):`n - $($missing -join "`n - ")"
}

$avaloniaXaml = Read-Raw $avaloniaXamlPath
$avaloniaCode = Read-Raw $avaloniaCodePath
$avaloniaAdapter = Read-Raw $avaloniaAdapterPath
$installer = Read-Raw $installerPath
$viewModel = Read-Raw $viewModelPath
$projectStore = Read-Raw $projectStorePath
$qualityAnalyzer = Read-Raw $qualityAnalyzerPath

$contracts = @{
    'Keyboard shortcuts' = $avaloniaCode.Contains('Window_KeyDown') -and $avaloniaCode.Contains('KeyModifiers.Control')
    'Accessible names' = $avaloniaAdapter.Contains('AutomationProperties.SetName')
    'Save and Save As behavior' = $avaloniaCode.Contains('SaveProjectAsync(bool saveAs)')
    'New project behavior' = $avaloniaXaml.Contains('Click="NewProject_Click"') -and $avaloniaCode.Contains('_viewModel.NewProject()')
    'Project schema excludes global libraries' = $projectStore.Contains('SchemaVersion { get; set; } = 2') -and $projectStore.Contains('public List<MasterLibraryDefinition>? MasterLibraries') -and $viewModel.Contains('Master Libraries are application-level resources')
    'All-series quality action' = $avaloniaCode.Contains('AnalyzeAllQuality_Click') -and $avaloniaXaml.Contains('CanRunAllQualityAnalysis')
    'Distributed quality sampling' = $qualityAnalyzer.Contains('ReadAnalysisTilesAsync') -and $qualityAnalyzer.Contains('CollapseBayer') -and $qualityAnalyzer.Contains('localBackground') -and $viewModel.Contains('var corroborated =')
    'Visible Windows update progress' = $avaloniaCode.Contains('startInfo.ArgumentList.Add("/SILENT")') -and -not $avaloniaCode.Contains('startInfo.ArgumentList.Add("/VERYSILENT")')
    'Visible progress from legacy updaters' = $avaloniaCode.Contains('startInfo.ArgumentList.Add("/APFVISIBLE=1")') -and $installer.Contains('CurInstallProgressChanged') -and $installer.Contains('NeedsCompatibilityProgress')
    'Windows update relaunch' = $installer.Contains('Parameters: "--updated"') -and $avaloniaCode.Contains('UpdatedArgument = "--updated"')
    'Project file association' = $installer.Contains('""%1""') -and $avaloniaCode.Contains('ProjectFileExtension = ".astroforge"')
    'Installer ships the Avalonia executable' = $installer.Contains('#define MyAppExeName "AstroProjectForge.exe"') -and $installer.Contains('[InstallDelete]') -and $installer.Contains('Type: files; Name: "{app}\{#LegacyAppExeName}"')
    'Operational onboarding' = $avaloniaXaml.Contains('x:Name="OnboardingStep5"') -and $avaloniaCode.Contains('if (_viewModel.CanAnalyzeProject)')
    'First-run language choice' = $avaloniaXaml.Contains('OnboardingEnglish_Click') -and $avaloniaXaml.Contains('OnboardingItalian_Click')
    'English default language' = (Read-Raw $appStatePath).Contains('UiLanguage { get; set; } = UiLocalization.English')
}

$failed = @($contracts.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object Key)
if ($failed.Count -gt 0) { throw "Contratti UI mancanti: $($failed -join ', ')" }

Write-Host "UI CONTRACT PASSED · $($literalValues.Count) stringhe localizzate · $($contracts.Count) controlli" -ForegroundColor Green
