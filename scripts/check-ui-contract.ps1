[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dictionaryPath = Join-Path $root 'assets\i18n\en.json'
$wpfXamlPath = Join-Path $root 'dotnet\AstroForge.App\MainWindow.xaml'
$avaloniaXamlPath = Join-Path $root 'dotnet\AstroForge.CrossPlatform\MainWindow.axaml'
$wpfCodePath = Join-Path $root 'dotnet\AstroForge.App\MainWindow.xaml.cs'
$avaloniaCodePath = Join-Path $root 'dotnet\AstroForge.CrossPlatform\MainWindow.axaml.cs'
$wpfAdapterPath = Join-Path $root 'dotnet\AstroForge.App\Services\WpfLocalizationAdapter.cs'
$avaloniaAdapterPath = Join-Path $root 'dotnet\AstroForge.CrossPlatform\AvaloniaLocalizationAdapter.cs'
$installerPath = Join-Path $root 'installer\AstroProjectForge.iss'

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
foreach ($path in @($wpfXamlPath, $avaloniaXamlPath)) {
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

$wpfXaml = Read-Raw $wpfXamlPath
$wpfCode = Read-Raw $wpfCodePath
$avaloniaCode = Read-Raw $avaloniaCodePath
$wpfAdapter = Read-Raw $wpfAdapterPath
$avaloniaAdapter = Read-Raw $avaloniaAdapterPath
$installer = Read-Raw $installerPath

$contracts = @{
    'WPF preview keyboard handler' = $wpfXaml.Contains('PreviewKeyDown="Window_PreviewKeyDown"')
    'WPF shortcuts' = $wpfCode.Contains('Window_PreviewKeyDown') -and $wpfCode.Contains('ModifierKeys.Control')
    'Avalonia shortcuts' = $avaloniaCode.Contains('Window_KeyDown') -and $avaloniaCode.Contains('KeyModifiers.Control')
    'WPF accessible names' = $wpfAdapter.Contains('AutomationProperties.SetName')
    'Avalonia accessible names' = $avaloniaAdapter.Contains('AutomationProperties.SetName')
    'Save and Save As behavior (WPF)' = $wpfCode.Contains('SaveProject(bool saveAs)')
    'Save and Save As behavior (Avalonia)' = $avaloniaCode.Contains('SaveProjectAsync(bool saveAs)')
    'Visible Windows update progress' = $wpfCode.Contains('startInfo.ArgumentList.Add("/SILENT")') -and -not $wpfCode.Contains('startInfo.ArgumentList.Add("/VERYSILENT")')
    'Visible progress from legacy updaters' = $wpfCode.Contains('startInfo.ArgumentList.Add("/APFVISIBLE=1")') -and $installer.Contains('CurInstallProgressChanged') -and $installer.Contains('NeedsCompatibilityProgress')
    'WPF operational onboarding' = $wpfXaml.Contains('x:Name="OnboardingStep4"') -and $wpfCode.Contains('if (_viewModel.CanAnalyzeProject) Scan_Click')
    'Avalonia operational onboarding' = (Read-Raw $avaloniaXamlPath).Contains('x:Name="OnboardingStep4"') -and $avaloniaCode.Contains('if (_viewModel.CanAnalyzeProject)')
}

$failed = @($contracts.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object Key)
if ($failed.Count -gt 0) { throw "Contratti UI mancanti: $($failed -join ', ')" }

Write-Host "UI CONTRACT PASSED · $($literalValues.Count) stringhe localizzate · $($contracts.Count) controlli" -ForegroundColor Green
