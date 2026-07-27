[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Stable', 'Beta')][string]$Channel,
    [string]$DistributionPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\distribution')
)

$ErrorActionPreference = 'Stop'
$channelName = $Channel.ToLowerInvariant()
$feedPath = Join-Path $DistributionPath "$channelName.json"
if (-not (Test-Path -LiteralPath $feedPath)) { throw "Feed non trovato: $feedPath" }

$feed = Get-Content -LiteralPath $feedPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($feed.channel -ne $Channel) { throw "Canale manifest inatteso: $($feed.channel)" }
if (-not $feed.signed) { throw 'Pubblicazione feed bloccata: installer non firmato.' }
if (-not $feed.installer.url -or -not $feed.installer.sha256) { throw 'Manifest incompleto.' }

& gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI non autenticata.' }

$repository = 'astropuzzo/astroproject-forge'
$tag = "channel-$channelName"
$title = "AstroProject Forge · $Channel update channel"
$existing = & gh release view $tag --repo $repository --json tagName 2>$null
if ($LASTEXITCODE -ne 0) {
    if ($PSCmdlet.ShouldProcess("$repository/$tag", 'Crea canale aggiornamenti')) {
        & gh release create $tag --repo $repository --title $title --notes "Machine-readable $Channel update channel. Use the versioned releases for downloads and release notes." --prerelease
        if ($LASTEXITCODE -ne 0) { throw 'Creazione canale GitHub fallita.' }
    }
}

if ($PSCmdlet.ShouldProcess("$repository/$tag/$channelName.json", 'Pubblica feed firmato')) {
    & gh release upload $tag $feedPath --repo $repository --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Upload feed GitHub fallito.' }
}

Write-Host "Feed pubblicato: https://github.com/$repository/releases/download/$tag/$channelName.json"
