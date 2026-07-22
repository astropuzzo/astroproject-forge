[CmdletBinding()]
param(
    [ValidateSet('Stable','Beta')][string]$Channel,
    [string]$Version,
    [switch]$SkipQa,
    [switch]$RequireSignature,
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$identity = Get-Content -LiteralPath (Join-Path $root 'version.json') -Raw | ConvertFrom-Json
if (-not $Channel) { $Channel = $identity.channel }
if (-not $Version) { $Version = $identity.version }
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$') { throw "Versione SemVer non valida: $Version" }
if ($Channel -eq 'Stable' -and $Version.Contains('-')) { throw 'Una build Stable non può avere un prerelease SemVer.' }

$localDotnet = Join-Path $root '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$appProject = Join-Path $root 'dotnet\AstroForge.App\AstroForge.App.csproj'
$distribution = Join-Path $root 'artifacts\distribution'
$stage = Join-Path $root 'artifacts\stage'
$portableName = "AstroProjectForge-$Channel-$Version-win-x64-portable.zip"
$setupName = "AstroProjectForge-$Channel-$Version-win-x64-setup.exe"
$publishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

foreach ($path in @($distribution, $stage)) {
    $resolvedParent = [IO.Path]::GetFullPath((Split-Path -Parent $path))
    if (-not $resolvedParent.StartsWith([IO.Path]::GetFullPath($root), [StringComparison]::OrdinalIgnoreCase)) { throw "Percorso di build non sicuro: $path" }
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

if (-not $SkipQa) { & (Join-Path $root 'qa-gate.ps1'); if ($LASTEXITCODE -ne 0) { throw 'QA gate non superato.' } }

& $dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version -p:InformationalVersion=$Version -p:ReleaseChannel=$Channel -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Publish distribuzione fallita.' }
$app = Join-Path $stage 'AstroForge.App.exe'
Copy-Item -LiteralPath (Join-Path $root 'docs\CHANGELOG.md') -Destination (Join-Path $stage 'RELEASE-NOTES.md')
& $dotnet list $appProject package --include-transitive --format json | Set-Content -LiteralPath (Join-Path $stage 'sbom-dotnet.json') -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Generazione SBOM fallita.' }
$qaReportSource = Join-Path $root 'artifacts\qa\qa-report.json'
$qaReportFile = if (Test-Path -LiteralPath $qaReportSource) { 'qa-report.json' } else { $null }
if ($qaReportFile) { Copy-Item -LiteralPath $qaReportSource -Destination (Join-Path $stage $qaReportFile) }

function Get-SignTool {
    if ($env:ASTROFORGE_SIGNTOOL -and (Test-Path -LiteralPath $env:ASTROFORGE_SIGNTOOL)) { return $env:ASTROFORGE_SIGNTOOL }
    $candidate = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue | Where-Object FullName -Match '\\x64\\signtool.exe$' | Sort-Object FullName -Descending | Select-Object -First 1
    return $candidate.FullName
}
function Sign-And-Verify([string]$Path) {
    $signTool = Get-SignTool
    if (-not $signTool -or -not $env:ASTROFORGE_SIGN_THUMBPRINT) { return $false }
    & $signTool sign /sha1 $env:ASTROFORGE_SIGN_THUMBPRINT /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw "Firma fallita: $Path" }
    & $signTool verify /pa /v $Path
    if ($LASTEXITCODE -ne 0) { throw "Verifica firma fallita: $Path" }
    return $true
}

$appSigned = Sign-And-Verify $app
if ($RequireSignature -and -not $appSigned) { throw 'Release bloccata: certificato o SignTool autentico non configurato.' }

$preManifest = [ordered]@{
    schema = 1; product = 'AstroProject Forge'; channel = $Channel; version = $Version; publishedAtUtc = $publishedAtUtc
    executable = [ordered]@{ fileName = 'AstroForge.App.exe'; sha256 = (Get-FileHash $app -Algorithm SHA256).Hash.ToLowerInvariant(); sizeBytes = (Get-Item $app).Length; signed = $appSigned }
    qaReport = $qaReportFile; sbom = 'sbom-dotnet.json'; releaseEligible = $false
}
$preManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'release-manifest.json') -Encoding utf8
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $distribution $portableName) -CompressionLevel Optimal

if (-not $InnoCompiler) {
    $localInno = Join-Path $root '.tools\Inno Setup 7\ISCC.exe'
    if (Test-Path -LiteralPath $localInno) { $InnoCompiler = $localInno }
    else { $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($found) { $InnoCompiler = $found.Source } }
}
$installerPath = Join-Path $distribution $setupName
$installerSigned = $false
if ($InnoCompiler -and (Test-Path -LiteralPath $InnoCompiler)) {
    $numericVersion = ([regex]::Match($Version, '^(\d+)\.(\d+)\.(\d+)')).Groups[1..3].Value -join '.'
    $numericVersion += '.0'
    & $InnoCompiler "/DMyAppVersion=$Version" "/DMyNumericVersion=$numericVersion" "/DMyChannel=$Channel" "/DSourceDir=$stage" "/DOutputDir=$distribution" (Join-Path $root 'installer\AstroProjectForge.iss')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) { throw 'Compilazione installer fallita.' }
    $installerSigned = Sign-And-Verify $installerPath
    if ($RequireSignature -and -not $installerSigned) { throw 'Release bloccata: installer non firmato.' }
}
elseif ($RequireSignature) { throw 'Release bloccata: compilatore Inno Setup non disponibile.' }
else { Write-Warning 'Inno Setup non disponibile: prodotto soltanto il pacchetto portabile.' }

$manifest = [ordered]@{
    schema = 1; product = 'AstroProject Forge'; channel = $Channel; version = $Version; publishedAtUtc = $publishedAtUtc
    signed = ($appSigned -and $installerSigned); releaseEligible = ($appSigned -and $installerSigned -and (Test-Path -LiteralPath $installerPath))
    executable = [ordered]@{ fileName = 'AstroForge.App.exe'; sha256 = (Get-FileHash $app -Algorithm SHA256).Hash.ToLowerInvariant(); sizeBytes = (Get-Item $app).Length; signed = $appSigned }
    portable = [ordered]@{ fileName = $portableName; sha256 = (Get-FileHash (Join-Path $distribution $portableName) -Algorithm SHA256).Hash.ToLowerInvariant(); sizeBytes = (Get-Item (Join-Path $distribution $portableName)).Length }
    installer = if (Test-Path -LiteralPath $installerPath) { [ordered]@{ fileName = $setupName; sha256 = (Get-FileHash $installerPath -Algorithm SHA256).Hash.ToLowerInvariant(); sizeBytes = (Get-Item $installerPath).Length; signed = $installerSigned } } else { $null }
    sbom = 'sbom-dotnet.json'; qaReport = $qaReportFile
}
$manifestPath = Join-Path $distribution 'release-manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8

if (Test-Path -LiteralPath $installerPath) {
    $feed = [ordered]@{
        schema = 1; product = 'AstroProject Forge'; channel = $Channel; version = $Version; publishedAtUtc = $publishedAtUtc
        installer = [ordered]@{ url = "https://github.com/astropuzzo/astroproject-forge/releases/download/v$Version/$setupName"; sha256 = $manifest.installer.sha256; sizeBytes = $manifest.installer.sizeBytes; fileName = $setupName }
        releaseNotesUrl = "https://github.com/astropuzzo/astroproject-forge/releases/tag/v$Version"; minimumWindowsVersion = '10.0.19045'; signed = $installerSigned
    }
    $feed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $distribution "$($Channel.ToLowerInvariant()).json") -Encoding utf8
}

Copy-Item -LiteralPath (Join-Path $stage 'sbom-dotnet.json') -Destination $distribution
if ($qaReportFile) { Copy-Item -LiteralPath $qaReportSource -Destination $distribution }
$hashLines = Get-ChildItem -LiteralPath $distribution -File | Where-Object Name -NotIn @('SHA256SUMS.txt') | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
$hashLines | Set-Content -LiteralPath (Join-Path $distribution 'SHA256SUMS.txt') -Encoding ascii

Get-ChildItem -LiteralPath $distribution -File | Select-Object Name, Length, @{n='SHA256';e={(Get-FileHash $_.FullName -Algorithm SHA256).Hash}} | Format-Table -AutoSize
if (-not $manifest.releaseEligible) { Write-Warning 'Build di sviluppo verificata ma NON eleggibile alla vendita: manca una firma Authenticode valida.' }
