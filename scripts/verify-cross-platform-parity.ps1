[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'dotnet/AstroForge.CrossPlatform/AstroForge.CrossPlatform.csproj'
$window = Join-Path $root 'dotnet/AstroForge.CrossPlatform/MainWindow.axaml'
$viewModel = Join-Path $root 'dotnet/AstroForge.App/ViewModels/MainViewModel.cs'

if (-not $SkipBuild) {
    $dotnet = if ($IsWindows) { Join-Path $root '.dotnet/dotnet.exe' } else { 'dotnet' }
    & $dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Cross-platform build failed.' }
}

$projectText = Get-Content -LiteralPath $project -Raw
$windowText = Get-Content -LiteralPath $window -Raw
if ($projectText -notmatch 'AstroForge\.App\\ViewModels\\MainViewModel\.cs') { throw 'The cross-platform app is not linked to the shared MainViewModel.' }
if (Test-Path (Join-Path $root 'dotnet/AstroForge.CrossPlatform/ViewModels/CrossPlatformViewModel.cs')) { throw 'Reduced preview ViewModel must not exist.' }

$requiredWorkspaces = @('Analisi','Struttura','WBPP','Dati','Quality','Revisione','Master Lab','Log')
$missing = @($requiredWorkspaces | Where-Object { $windowText -notmatch [regex]::Escape(('Header="{0}"' -f $_)) })
if ($missing.Count -gt 0) { throw "Missing workspaces: $($missing -join ', ')" }

$requiredModelCapabilities = @(
    'TreeRoots','PlannedTreeRoots','ApplyOverridesCommand','LinkFlatSetCommand','WbppKeywords',
    'FilterStatistics','QualitySeries','AnalyzeQualityAsync','ReviewQueue','ExportAsync',
    'MasterOrganizerItems','OrganizeMasterLibraryAsync','DiagnosticEvents','RestoreRecoveryAsync'
)
$viewModelText = Get-Content -LiteralPath $viewModel -Raw
$missing = @($requiredModelCapabilities | Where-Object { $viewModelText -notmatch [regex]::Escape($_) })
if ($missing.Count -gt 0) { throw "Shared model capabilities missing: $($missing -join ', ')" }
$requiredUiContracts = @('TreeRoots','PlannedTreeRoots','ApplyOverridesCommand','LinkFlatSetCommand','WbppKeywords','FilterStatistics','QualitySeries','ReviewQueue','MasterOrganizerItems','DiagnosticEvents','Export_Click','RestoreRecovery_Click','ShowOnboarding','CompleteOnboarding_Click')
$missing = @($requiredUiContracts | Where-Object { $windowText -notmatch [regex]::Escape($_) })
if ($missing.Count -gt 0) { throw "Capabilities not exposed by the cross-platform UI: $($missing -join ', ')" }

Write-Host "PASS: shared application model and $($requiredWorkspaces.Count) cross-platform workspaces verified."
