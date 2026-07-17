[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$localDotnet = Join-Path $root '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$testProject = Join-Path $root 'dotnet\AstroForge.Core.Tests\AstroForge.Core.Tests.csproj'
$appProject = Join-Path $root 'dotnet\AstroForge.App\AstroForge.App.csproj'
$output = Join-Path $root 'dist-dotnet'
$reportDirectory = Join-Path $root 'artifacts\qa'
$reportPath = Join-Path $reportDirectory 'qa-report.json'
$started = [DateTimeOffset]::UtcNow
$steps = [System.Collections.Generic.List[object]]::new()
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Invoke-GateStep([string]$Name, [scriptblock]$Action) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        if ($LASTEXITCODE -ne 0) { throw "Exit code $LASTEXITCODE" }
        $steps.Add([pscustomobject]@{ name = $Name; status = 'passed'; durationMs = $watch.ElapsedMilliseconds })
    }
    catch {
        $steps.Add([pscustomobject]@{ name = $Name; status = 'failed'; durationMs = $watch.ElapsedMilliseconds; error = $_.Exception.Message })
        throw
    }
}

New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$status = 'failed'
try {
    Push-Location $root
    Invoke-GateStep 'core-regression-suite' { & $dotnet run --project $testProject -c Release }
    Invoke-GateStep 'wpf-release-build' { & $dotnet build $appProject -c Release }
    if (-not $SkipPublish) {
        Invoke-GateStep 'self-contained-publish' {
            & $dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $output
        }
        if (-not (Test-Path -LiteralPath (Join-Path $output 'AstroForge.App.exe'))) { throw 'Eseguibile self-contained assente.' }
    }
    $status = 'passed'
}
finally {
    Pop-Location
    $version = Get-Content -LiteralPath (Join-Path $root 'version.json') -Raw | ConvertFrom-Json
    [pscustomobject]@{
        schema = 1
        status = $status
        product = 'AstroProject Forge'
        version = $version.version
        channel = $version.channel
        startedAtUtc = $started
        completedAtUtc = [DateTimeOffset]::UtcNow
        os = [Environment]::OSVersion.VersionString
        runtime = (& $dotnet --version)
        machineDataDependency = $false
        steps = $steps
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
}

Write-Host "QA GATE PASSED · $reportPath" -ForegroundColor Green
