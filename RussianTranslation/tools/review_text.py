#!/usr/bin/env python3
"""Validate the Russian text layer, including raw length-prefixed scene strings."""

from __future__ import annotations

import csv
import os
import json
import re
import struct
from pathlib import Path


PROJECT_DIR = Path(__file__).resolve().parents[1]
GAME_DIR = Path(os.environ.get("CRYSTAL_CAVES_HD_DIR", str(PROJECT_DIR.parent))).resolve()
TRANSLATION_DIR = PROJECT_DIR / "translations"
CANDIDATE_PATH = PROJECT_DIR / "catalog" / "translation_candidates.csv"
REPORT_JSON = PROJECT_DIR / "catalog" / "text_review.json"
REPORT_MD = PROJECT_DIR / "catalog" / "text_review.md"

TAG_PATTERN = re.compile(r"</?color(?:=[^>]+)?>", re.IGNORECASE)
LATIN_PATTERN = re.compile(r"[A-Za-z]")
STORY_MARKERS = re.compile(
    r"Personal Entry|Mylo|Twibble|VeeGer|Millennium|crystal caves|"
    r"salvage ship|turbovator|Rip Eweoff|galaxy|quadrant",
    re.IGNORECASE,
)
IGNORED_RAW_STRINGS = {
    "<Show message before lighting hits the vegee>",
    "<Play lighting bolt hitting Veegee>",
}
DYNAMIC_PATTERNS = [
    re.compile(r"^Congratulations, all fruit picked!\nExtra .+$"),
    re.compile(r"^Perfect health! .+$"),
    re.compile(r"^Max number of '.+' reached \(.+\)!$"),
    re.compile(r"^Entries: \d+$", re.IGNORECASE),
    re.compile(r"^<?\s*Page:?\s*\d+(?:/\d+)?\s*>?$"),
    re.compile(r"^Autosave - Episode \d+$"),
    re.compile(r"^Autosave .+$"),
    re.compile(r"^(Terrain|Grid|Tiles|Pickups|Enemies|Objects|Navigation)( \(\d+\))?$"),
]
DYNAMIC_CANDIDATE_FRAGMENTS = {
    "' reached (",
    "Congratulations, all fruit picked!\nExtra",
    "Max number of '",
    "Perfect health!",
}


def unescape(value: str) -> str:
    output: list[str] = []
    index = 0
    replacements = {"n": "\n", "r": "\r", "t": "\t", "\\": "\\"}
    while index < len(value):
        if value[index] == "\\" and index + 1 < len(value):
            escaped = value[index + 1]
            if escaped in replacements:
                output.append(replacements[escaped])
                index += 2
                continue
        output.append(value[index])
        index += 1
    return "".join(output)


def normalize(value: str) -> str:
    value = (
        value.replace("\u200b", "")
        .replace("\x13", "-")
        .replace("\x19", "'")
        .replace("\r\n", "\n")
        .replace("\r", "\n")
    )
    value = "\n".join(line.rstrip(" \t") for line in value.split("\n")).strip()
    return re.sub(r"\n{3,}", "\n\n", value)


def loose(value: str) -> str:
    return re.sub(r"\s+", " ", normalize(value)).strip()


def load_translations() -> tuple[dict[str, str], list[dict[str, object]]]:
    translations: dict[str, str] = {}
    rows: list[dict[str, object]] = []
    for path in sorted(TRANSLATION_DIR.glob("*.tsv")):
        for line_number, raw in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if not raw.strip() or raw.lstrip().startswith("#"):
                continue
            if "\t" not in raw:
                raise ValueError(f"Missing tab: {path.name}:{line_number}")
            source_raw, target_raw = raw.split("\t", 1)
            source = normalize(unescape(source_raw))
            target = normalize(unescape(target_raw))
            if source in translations and translations[source] != target:
                raise ValueError(f"Conflicting translation: {path.name}:{line_number}")
            translations[source] = target
            rows.append(
                {
                    "file": path.name,
                    "line": line_number,
                    "source": source,
                    "target": target,
                }
            )
    return translations, rows


def read_raw_strings(path: Path) -> set[str]:
    data = path.read_bytes()
    strings: set[str] = set()
    for offset in range(0, len(data) - 8):
        length = struct.unpack_from("<I", data, offset)[0]
        if length < 40 or length > 3000 or offset + 4 + length > len(data):
            continue
        payload = data[offset + 4 : offset + 4 + length]
        if b"\x00" in payload:
            continue
        try:
            value = payload.decode("utf-8")
        except UnicodeDecodeError:
            continue
        if LATIN_PATTERN.search(value) and STORY_MARKERS.search(value):
            strings.add(normalize(value))
    return strings


def is_dynamic(value: str) -> bool:
    return any(pattern.match(value) for pattern in DYNAMIC_PATTERNS)


def main() -> None:
    translations, rows = load_translations()
    exact = set(translations)
    loose_sources = {loose(value) for value in exact}

    with CANDIDATE_PATH.open("r", encoding="utf-8-sig", newline="") as handle:
        candidates = list(csv.DictReader(handle))
    missing_candidates = sorted(
        {
            normalize(row["original"])
            for row in candidates
            if normalize(row["original"]) not in exact
            and loose(row["original"]) not in loose_sources
            and normalize(row["original"]) not in DYNAMIC_CANDIDATE_FRAGMENTS
            and not is_dynamic(normalize(row["original"]))
        }
    )

    raw_strings: set[str] = set()
    scanned_levels: list[str] = []
    data_dir = GAME_DIR / "Crystal Caves HD_Data"
    for path in sorted(data_dir.glob("level*")):
        if path.is_file() and path.name[5:].isdigit():
            scanned_levels.append(path.name)
            raw_strings.update(read_raw_strings(path))
    if len(scanned_levels) != 9:
        raise SystemExit("Expected all nine game scenes; set CRYSTAL_CAVES_HD_DIR to the game directory.")
    missing_raw = sorted(
        value
        for value in raw_strings
        if value not in IGNORED_RAW_STRINGS
        and value not in exact
        and loose(value) not in loose_sources
        and not is_dynamic(value)
    )

    tag_mismatches = []
    story_lines_over_40 = []
    for row in rows:
        source_tags = TAG_PATTERN.findall(str(row["source"]))
        target_tags = TAG_PATTERN.findall(str(row["target"]))
        if source_tags != target_tags:
            tag_mismatches.append(f'{row["file"]}:{row["line"]}')
        if row["file"] == "story.tsv":
            for text_line in str(row["target"]).splitlines():
                visible = TAG_PATTERN.sub("", text_line)
                if len(visible) > 40:
                    story_lines_over_40.append(
                        {"location": f'{row["file"]}:{row["line"]}', "length": len(visible), "text": visible}
                    )

    report = {
        "translation_entries": len(translations),
        "static_candidates": len(candidates),
        "missing_static_candidates": missing_candidates,
        "levels_scanned": scanned_levels,
        "raw_story_strings": len(raw_strings),
        "missing_raw_story_strings": missing_raw,
        "dynamic_patterns": len(DYNAMIC_PATTERNS),
        "tag_mismatches": tag_mismatches,
        "story_lines_over_40": story_lines_over_40,
        "texture_translation": "disabled; original game textures are not replaced",
    }
    REPORT_JSON.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    status = "ПРОЙДЕН" if not any((missing_candidates, missing_raw, tag_mismatches, story_lines_over_40)) else "ЕСТЬ ОШИБКИ"
    markdown = f"""# Аудит текстового перевода

Статус: **{status}**

- Записей в словаре: {len(translations)}
- Статических кандидатов: {len(candidates)}
- Проверено сцен: {len(scanned_levels)} ({', '.join(scanned_levels)})
- Сюжетных строк в сыром сканировании: {len(raw_strings)}
- Динамических шаблонов: {len(DYNAMIC_PATTERNS)}
- Пропущено статических строк: {len(missing_candidates)}
- Пропущено сырых сюжетных строк: {len(missing_raw)}
- Несовпадений rich-text-тегов: {len(tag_mismatches)}
- Сюжетных строк длиннее 40 знаков: {len(story_lines_over_40)}
- Перевод текстур: отключён; оригинальные текстуры игры не заменяются.
"""
    REPORT_MD.write_text(markdown, encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    if status != "ПРОЙДЕН":
        raise SystemExit(1)


if __name__ == "__main__":
    main()
