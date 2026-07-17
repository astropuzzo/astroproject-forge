[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'qa-gate.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Il gate QA Release non è riuscito.' }

$executable = Join-Path $root 'dist-dotnet\AstroForge.App.exe'
$file = Get-Item -LiteralPath $executable
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $executable
[pscustomobject]@{
    Executable = $file.FullName
    Updated = $file.LastWriteTime
    SizeMiB = [math]::Round($file.Length / 1MB, 2)
    SHA256 = $hash.Hash
    QaReport = Join-Path $root 'artifacts\qa\qa-report.json'
} | Format-List
