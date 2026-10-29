# Crystal Caves HD — моды FenixUzb

Три неофициальных мода для **Crystal Caves HD 1.1.7 (Steam, Windows x64)**.
Автор: **[FenixUzb](https://github.com/FenixUzb)**.

## Скачать

Готовые ZIP-архивы находятся в [последнем релизе](https://github.com/FenixUzb/CrystalCavesHD-Mods/releases/latest).
Для установки используйте эти архивы, а не кнопку GitHub «Code → Download ZIP».

| Мод | Версия | Возможности | Скачать |
|---|---|---|---|
| [Русский перевод](RussianTranslation/README.md) | 0.2.11 | Интерфейс, сюжет, подсказки, достижения и редактор уровней | [ZIP](https://github.com/FenixUzb/CrystalCavesHD-Mods/releases/download/mods-2026-10-29/CrystalCavesHD-Russian-v0.2.11.zip) |
| [Widescreen Fix](WidescreenFix/README.md) | 0.2.0 | Расширение игрового обзора Hor+, переключение в исходный 16:10 клавишей F10 | [ZIP](https://github.com/FenixUzb/CrystalCavesHD-Mods/releases/download/mods-2026-10-29/CrystalCavesHD-Widescreen-v0.2.0.zip) |
| [Completion Tracker](CompletionTracker/README.md) | 0.1.1 | Кристаллы, пропущенные уровни, достижения; F9 — панель, F8 — следующая страница | [ZIP](https://github.com/FenixUzb/CrystalCavesHD-Mods/releases/download/mods-2026-10-29/CrystalCavesHD-CompletionTracker-v0.1.1.zip) |

**[Скачать все три мода одним архивом](https://github.com/FenixUzb/CrystalCavesHD-Mods/releases/download/mods-2026-10-29/CrystalCavesHD-Mods-2026-10-29.zip)** ·
[SHA-256 архивов](https://github.com/FenixUzb/CrystalCavesHD-Mods/releases/download/mods-2026-10-29/SHA256SUMS.txt)

## Установка

Требуется Steam-версия Crystal Caves HD **1.1.7, Windows x64**.

1. Закройте игру.
2. Если BepInEx 5 ещё не установлен, скачайте официальный
   [BepInEx 5.4.23.5 для Windows x64](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip) и распакуйте его в папку
   с `Crystal Caves HD.exe`. Запустите игру один раз и закройте её.
3. Распакуйте ZIP мода в ту же папку игры, согласившись на объединение папок.
4. Запустите игру из Steam обычным способом.

В ZIP мода находятся папка `BepInEx/plugins` и документация.
**Сам загрузчик BepInEx в ZIP мода не включён.** Если он уже установлен,
повторно устанавливать его не требуется. Все три мода можно устанавливать вместе.


## Настройка и удаление

Настройки создаются после первого запуска в `BepInEx/config`.
Подробности и ограничения описаны в README каждого мода по ссылкам выше.
Для удаления отдельного мода удалите соответствующую папку:

- русский перевод: `BepInEx/plugins/CrystalCavesHD.Russian`;
- Widescreen Fix: `BepInEx/plugins/CrystalCavesHD.Widescreen`;
- Completion Tracker: `BepInEx/plugins/CrystalCavesHD.CompletionTracker`.

Оставьте BepInEx, если он нужен другим установленным модам.
Журнал диагностики: `BepInEx/LogOutput.log`.
Сообщить об ошибке можно через [Issues](https://github.com/FenixUzb/CrystalCavesHD-Mods/issues), указав версию игры,
мод и шаги воспроизведения.

## Исходники

Код всех трёх модов, таблицы перевода и инструменты доступны в этом репозитории.
Инструкция по сборке: [BUILDING.md](BUILDING.md).
Состав публикации и проверка DLL: [PACKAGING.md](PACKAGING.md).


## Автор и распространение

Автор мода: **FenixUzb** — https://github.com/FenixUzb.

Мод распространяется по лицензии MIT. Его можно использовать, изменять и
распространять, сохраняя `Copyright (c) 2026 FenixUzb` и полный текст лицензии.
Лицензия относится к авторским материалам мода. Права на игру, её исходные
тексты и сторонние компоненты остаются у соответствующих правообладателей.

Полный текст: [LICENSE](LICENSE). Сторонние компоненты и материалы:
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## English

Unofficial mods by **FenixUzb** for Crystal Caves HD 1.1.7, Steam, Windows x64:
Russian Translation 0.2.11, Widescreen Fix 0.2.0, and Completion Tracker 0.1.1.
Download individual mods or the all-in-one ZIP from [Releases](https://github.com/FenixUzb/CrystalCavesHD-Mods/releases/latest).
Install [BepInEx 5.4.23.5 Windows x64](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip) into the game folder first,
then extract the mod ZIP next to `Crystal Caves HD.exe` and launch the game.
All three mods can be installed together. BepInEx is a separate prerequisite.
Redistribution is permitted under MIT with the copyright notice crediting
**FenixUzb** and the license text preserved. Third-party rights remain with their owners.
