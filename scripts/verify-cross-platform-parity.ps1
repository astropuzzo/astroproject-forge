[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'dotnet/AstroForge.CrossPlatform/AstroForge.CrossPlatform.csproj'
$window = Join-Path $root 'dotnet/AstroForge.CrossPlatform/MainWindow.axaml'
$viewModel = Join-Path $root 'dotnet/AstroForge.CrossPlatform/Shared/ViewModels/MainViewModel.cs'

if (-not $SkipBuild) {
    $dotnet = if ($IsWindows) { Join-Path $root '.dotnet/dotnet.exe' } else { 'dotnet' }
    & $dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Cross-platform build failed.' }
}

$projectText = Get-Content -LiteralPath $project -Raw
$windowText = Get-Content -LiteralPath $window -Raw
if (-not (Test-Path -LiteralPath $viewModel)) { throw 'The shared MainViewModel is missing from the Avalonia project.' }
if ($projectText -match 'AstroForge\.App[\\/]') { throw 'The Avalonia project must not reference the retired WPF project.' }
if (Test-Path (Join-Path $root 'dotnet/AstroForge.App')) { throw 'The retired WPF project must not come back: the Avalonia app is the only UI.' }
if (Test-Path (Join-Path $root 'dotnet/AstroForge.CrossPlatform/ViewModels/CrossPlatformViewModel.cs')) { throw 'Reduced preview ViewModel must not exist.' }

# The statistics workspace is the Overview (Panoramica).
$requiredWorkspaces = @('Analisi','Esporta','WBPP','Panoramica','Strumento','Qualità','Risolvi','Libreria Master','Log')
$missing = @($requiredWorkspaces | Where-Object { $windowText -notmatch [regex]::Escape(('Header="{0}"' -f $_)) })
if ($missing.Count -gt 0) { throw "Missing workspaces: $($missing -join ', ')" }

$requiredModelCapabilities = @(
    'TreeRoots','PlannedTreeRoots','ApplyOverridesCommand','LinkFlatSetCommand','WbppKeywords',
    'FilterStatistics','QualitySeries','AnalyzeQualityAsync','AnalyzeAllQualityAsync','NewProject','ReviewQueue','ExportAsync',
    'MasterOrganizerItems','OrganizeMasterLibraryAsync','DiagnosticEvents','RestoreRecoveryAsync'
)
$viewModelText = Get-Content -LiteralPath $viewModel -Raw
$missing = @($requiredModelCapabilities | Where-Object { $viewModelText -notmatch [regex]::Escape($_) })
if ($missing.Count -gt 0) { throw "Shared model capabilities missing: $($missing -join ', ')" }
$requiredUiContracts = @('TreeRoots','PlannedTreeRoots','ApplyOverridesCommand','LinkFlatSetCommand','WbppKeywords','FilterCards','SessionStatistics','QualitySeries','ReviewQueue','MasterOrganizerItems','DiagnosticEvents','Export_Click','AnalyzeAllQuality_Click','NewProject_Click','SaveProjectAs_Click','RestoreRecovery_Click','ShowOnboarding','OnboardingNext_Click')
$missing = @($requiredUiContracts | Where-Object { $windowText -notmatch [regex]::Escape($_) })
if ($missing.Count -gt 0) { throw "Capabilities not exposed by the cross-platform UI: $($missing -join ', ')" }

Write-Host "PASS: shared application model and $($requiredWorkspaces.Count) cross-platform workspaces verified."
