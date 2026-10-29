$ErrorActionPreference = 'Stop'

$gameRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$pluginPath = [System.IO.Path]::GetFullPath(
    (Join-Path $gameRoot 'BepInEx\plugins\CrystalCavesHD.Russian')
)
$expectedPrefix = [System.IO.Path]::GetFullPath(
    (Join-Path $gameRoot 'BepInEx\plugins')
) + [System.IO.Path]::DirectorySeparatorChar

if (-not $pluginPath.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Отказ: вычисленный путь находится вне BepInEx\plugins: $pluginPath"
}

if (Test-Path -LiteralPath $pluginPath) {
    Remove-Item -LiteralPath $pluginPath -Recurse -Force
    Write-Host 'Русификатор Crystal Caves HD удалён.'
} else {
    Write-Host 'Русификатор не установлен.'
}

Write-Host 'BepInEx и оригинальные файлы игры не изменены.'
