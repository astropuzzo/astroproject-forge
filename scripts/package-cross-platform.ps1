[CmdletBinding()]
param([string]$Version = '0.9.0-alpha.1')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
$project = Join-Path $root 'dotnet\AstroForge.CrossPlatform\AstroForge.CrossPlatform.csproj'
$output = Join-Path $root 'artifacts\cross-platform'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$safeRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
$safeOutput = [IO.Path]::GetFullPath($output)
if (-not $safeOutput.StartsWith($safeRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output packaging non sicuro.' }
Get-ChildItem -LiteralPath $output -Filter 'AstroProjectForge-*.zip' -File -ErrorAction SilentlyContinue | Remove-Item -Force
foreach ($rid in @('linux-x64','linux-arm64','osx-x64','osx-arm64')) {
    $publish = Join-Path $output $rid
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    & $dotnet publish $project -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw "Publish fallita: $rid" }
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $output "AstroProjectForge-$Version-$rid.zip") -Force
}
Get-ChildItem $output -Filter "AstroProjectForge-$Version-*.zip" | Select-Object Name,Length,@{n='SHA256';e={(Get-FileHash $_.FullName -Algorithm SHA256).Hash}}
