[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDirectory,
    [string]$BepInExDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path.TrimEnd('\')
if (-not $BepInExDirectory) {
    $BepInExDirectory = Join-Path $gameRoot 'BepInEx\core'
}
$bepRoot = (Resolve-Path -LiteralPath $BepInExDirectory).Path.TrimEnd('\')
foreach ($required in @(
    (Join-Path $gameRoot 'Crystal Caves HD.exe'),
    (Join-Path $gameRoot 'Crystal Caves HD_Data\Managed\Assembly-CSharp.dll'),
    (Join-Path $bepRoot 'BepInEx.dll'),
    (Join-Path $bepRoot '0Harmony.dll')
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Missing build dependency: $required"
    }
}
$previousGameDirectory = $env:CRYSTAL_CAVES_HD_DIR
try {
    $env:CRYSTAL_CAVES_HD_DIR = $gameRoot
    Push-Location -LiteralPath $repoRoot
    try {
        python RussianTranslation/tools/build_translation.py
        if ($LASTEXITCODE -ne 0) { throw 'Translation merge failed.' }
        python RussianTranslation/tools/review_text.py
        if ($LASTEXITCODE -ne 0) { throw 'Translation review failed.' }
        foreach ($mod in @(
            @{ Folder = 'RussianTranslation'; Assembly = 'CrystalCavesHD.Russian' },
            @{ Folder = 'WidescreenFix'; Assembly = 'CrystalCavesHD.Widescreen' },
            @{ Folder = 'CompletionTracker'; Assembly = 'CrystalCavesHD.CompletionTracker' }
        )) {
            $project = Join-Path $repoRoot ($mod.Folder + '\plugin\' + $mod.Assembly + '.csproj')
            dotnet build $project -c Release "-p:GameDirectory=$gameRoot" "-p:BepInExDirectory=$bepRoot" --nologo
            if ($LASTEXITCODE -ne 0) { throw "Build failed: $($mod.Folder)" }
            $target = Join-Path $repoRoot ('dist\built-plugins\BepInEx\plugins\' + $mod.Assembly)
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            Copy-Item -LiteralPath (Join-Path $repoRoot ($mod.Folder + '\build\plugin\' + $mod.Assembly + '.dll')) -Destination $target
            Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $target 'LICENSE.txt')
        }
        $russianTarget = Join-Path $repoRoot 'dist\built-plugins\BepInEx\plugins\CrystalCavesHD.Russian'
        Copy-Item -LiteralPath (Join-Path $repoRoot 'RussianTranslation\build\plugin\translations.tsv') -Destination $russianTarget
        Copy-Item -LiteralPath (Join-Path $repoRoot 'RussianTranslation\build\plugin\fonts') -Destination $russianTarget -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $repoRoot 'CompletionTracker\data\achievements.tsv') -Destination (Join-Path $repoRoot 'dist\built-plugins\BepInEx\plugins\CrystalCavesHD.CompletionTracker')
        Write-Output 'Build complete: dist/built-plugins. Game files were not changed.'
    } finally { Pop-Location }
} finally {
    $env:CRYSTAL_CAVES_HD_DIR = $previousGameDirectory
}
