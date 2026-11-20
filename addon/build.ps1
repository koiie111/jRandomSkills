param(
    [Parameter(Mandatory)]
    [string] $Cs2Root,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-zA-Z0-9_-]+$')]
    [string] $Addon
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Cs2Root).Path
$compiler = Join-Path $root 'game/bin/win64/resourcecompiler.exe'
$content = Join-Path $root "content/csgo_addons/$Addon"
$output = Join-Path $root "game/csgo_addons/$Addon"
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw "Missing Resource Compiler at $compiler"
}
if ((Test-Path -LiteralPath $content) -or (Test-Path -LiteralPath $output)) {
    throw 'Use a fresh addon name for each build.'
}
New-Item -ItemType Directory -Path $content, $output | Out-Null
Copy-Item -Recurse -LiteralPath (Join-Path $PSScriptRoot 'panorama') -Destination $content
$files = @(
    'panorama/styles/custom_game/jrandomskills.vcss',
    'panorama/layout/custom_game/jrandomskills_card.vxml',
    'panorama/layout/custom_game/jrandomskills_menu.vxml'
)
foreach ($file in $files) {
    & $compiler -i (Join-Path $content $file) -f
    if ($LASTEXITCODE -ne 0) { throw "Resource Compiler failed on $file" }
    $compiled = Join-Path $output "${file}_c"
    if (-not (Test-Path -LiteralPath $compiled -PathType Leaf)) {
        throw "Missing compiled resource $compiled"
    }
}
Write-Output $output
