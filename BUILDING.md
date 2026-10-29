# Сборка из исходников

Нужны Windows, .NET SDK 8, Python 3.10 или новее и собственная Steam-копия
Crystal Caves HD 1.1.7 с установленным BepInEx 5.4.23.5 для Windows x64.
Папку репозитория можно разместить отдельно от игры.

Из корня репозитория выполните:

```powershell
.\scripts\Build.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Crystal Caves HD'
```

Сценарий проверяет таблицы перевода и все девять сцен игры, собирает три DLL
и подготавливает их вместе с данными в папке `dist/built-plugins/BepInEx/plugins`.
Он не устанавливает результат в игру. Для проверки скопируйте содержимое
`dist/built-plugins` в папку игры самостоятельно.

По умолчанию ссылки на BepInEx берутся из `<GameDirectory>/BepInEx/core`.
Другой каталог с этими DLL можно указать параметром `-BepInExDirectory`.
Unity DLL и `Assembly-CSharp.dll` используются только как локальные ссылки
компилятора и не копируются в результат.

Исходные таблицы находятся в `RussianTranslation/translations`.
`build_translation.py` объединяет их и копирует шрифт в каталог сборки.
`review_text.py` проверяет покрытие, теги и длину строк. Для его отдельного
запуска укажите папку игры через переменную окружения:

```powershell
$env:CRYSTAL_CAVES_HD_DIR = 'D:\SteamLibrary\steamapps\common\Crystal Caves HD'
python RussianTranslation/tools/review_text.py
```

`audit.py` — дополнительный инструмент повторного извлечения каталога из игры;
он требует UnityPy, а для анализа строк C# — установленный `ilspycmd`.
Для обычной сборки и проверки перевода он не нужен.

Ссылка на поддерживаемую сборку игры:

| Файл | SHA-256 |
|---|---|
| `Crystal Caves HD.exe` | `EBB99DB4D0A0A88572B774BF185FD36C991D01A97F846748A248F9A6335A474B` |
| `Crystal Caves HD_Data/Managed/Assembly-CSharp.dll` | `EBF4852F9CBAAA9D1C4B45CFDF9B30058197BD45F3E3A0BAF28EAAFEA9E82B56` |
