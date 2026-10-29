#!/usr/bin/env python3
"""Merge translation source files and report catalog coverage."""

from __future__ import annotations

import csv
import json
import re
import shutil
from pathlib import Path


PROJECT_DIR = Path(__file__).resolve().parents[1]
SOURCE_DIR = PROJECT_DIR / "translations"
CATALOG_PATH = PROJECT_DIR / "catalog" / "translation_candidates.csv"
OUTPUT_DIR = PROJECT_DIR / "build" / "plugin"
OUTPUT_PATH = OUTPUT_DIR / "translations.tsv"
REPORT_PATH = PROJECT_DIR / "catalog" / "coverage.json"
FONT_SOURCE_DIR = PROJECT_DIR / "vendor" / "fonts" / "RussoOne"
FONT_OUTPUT_DIR = OUTPUT_DIR / "fonts"

DYNAMIC_CANDIDATES = {
    "' reached (",
    "Congratulations, all fruit picked!\nExtra ",
    "Max number of '",
    "Perfect health! ",
}

EDITOR_CATEGORY = re.compile(
    r"^(Terrain|Grid|Tiles|Pickups|Enemies|Objects|Navigation)( \(\d+\))?$"
)


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


def loose_normalize(value: str) -> str:
    return re.sub(r"\s+", " ", normalize(value)).strip()


def load_sources() -> tuple[dict[str, tuple[str, str, int]], list[str]]:
    translations: dict[str, tuple[str, str, int]] = {}
    output_lines = ["# Crystal Caves HD Russian translation — generated file; edit translations/*.tsv"]

    for source_path in sorted(SOURCE_DIR.glob("*.tsv")):
        for line_number, raw_line in enumerate(source_path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if not raw_line.strip() or raw_line.lstrip().startswith("#"):
                continue
            if "\t" not in raw_line:
                raise SystemExit(f"Missing tab: {source_path.name}:{line_number}")
            source_raw, target_raw = raw_line.split("\t", 1)
            source = normalize(unescape(source_raw))
            target = unescape(target_raw)
            if not source or not target:
                raise SystemExit(f"Empty source or target: {source_path.name}:{line_number}")
            if source in translations and translations[source][0] != target:
                previous = translations[source]
                raise SystemExit(
                    f"Conflicting translation for {source!r}: "
                    f"{previous[1]}:{previous[2]} and {source_path.name}:{line_number}"
                )
            translations[source] = (target, source_path.name, line_number)
            output_lines.append(raw_line)

    return translations, output_lines


def load_candidates() -> list[dict[str, str]]:
    with CATALOG_PATH.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def main() -> None:
    translations, output_lines = load_sources()
    candidates = load_candidates()

    covered_by_priority: dict[str, int] = {}
    dynamic_by_priority: dict[str, int] = {}
    total_by_priority: dict[str, int] = {}
    missing_priority_one: list[dict[str, str]] = []
    loose_translations = {loose_normalize(source) for source in translations}
    for candidate in candidates:
        priority = candidate["priority"]
        total_by_priority[priority] = total_by_priority.get(priority, 0) + 1
        original = candidate["original"]
        if (normalize(original) in translations or
                loose_normalize(original) in loose_translations or
                original in DYNAMIC_CANDIDATES or
                EDITOR_CATEGORY.fullmatch(original)):
            covered_by_priority[priority] = covered_by_priority.get(priority, 0) + 1
            if original in DYNAMIC_CANDIDATES or EDITOR_CATEGORY.fullmatch(original):
                dynamic_by_priority[priority] = dynamic_by_priority.get(priority, 0) + 1
        elif priority == "1":
            missing_priority_one.append(
                {
                    "category": candidate["category"],
                    "original": candidate["original"],
                }
            )

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_text("\n".join(output_lines) + "\n", encoding="utf-8")
    FONT_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    for font_file in ("RussoOne-Regular.ttf", "OFL.txt"):
        source_path = FONT_SOURCE_DIR / font_file
        if not source_path.exists():
            raise SystemExit(f"Bundled font file is missing: {source_path}")
        shutil.copy2(source_path, FONT_OUTPUT_DIR / font_file)

    report = {
        "dictionary_entries": len(translations),
        "candidate_total": len(candidates),
        "candidate_coverage": {
            priority: {
                "translated": covered_by_priority.get(priority, 0),
                "total": total,
                "percent": round(100 * covered_by_priority.get(priority, 0) / total, 1),
                "dynamic_templates": dynamic_by_priority.get(priority, 0),
            }
            for priority, total in sorted(total_by_priority.items())
        },
        "missing_priority_one": missing_priority_one,
    }
    REPORT_PATH.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
