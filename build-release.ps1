[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
$appProject = Join-Path $root 'dotnet\AstroForge.App\AstroForge.App.csproj'
$testProject = Join-Path $root 'dotnet\AstroForge.Core.Tests\AstroForge.Core.Tests.csproj'
$output = Join-Path $root 'dist-dotnet'
$executable = Join-Path $output 'AstroForge.App.exe'

Push-Location $root
try {
    & $dotnet run --project $testProject -c Release
    if ($LASTEXITCODE -ne 0) { throw 'I test Release non sono riusciti.' }

    & $dotnet publish $appProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -o $output
    if ($LASTEXITCODE -ne 0) { throw 'La pubblicazione self-contained non è riuscita.' }

    $file = Get-Item -LiteralPath $executable
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $executable
    [pscustomobject]@{
        Executable = $file.FullName
        Updated = $file.LastWriteTime
        SizeMiB = [math]::Round($file.Length / 1MB, 2)
        SHA256 = $hash.Hash
    } | Format-List
}
finally {
    Pop-Location
}
