#!/usr/bin/env python3
"""Build a read-only localization inventory for Crystal Caves HD.

The script never writes into Crystal Caves HD_Data.  Reports and selected
texture exports are created under RussianTranslation/ only.
"""

from __future__ import annotations

import csv
import os
import io
import json
import re
import shutil
import subprocess
import tempfile
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator


PROJECT_DIR = Path(__file__).resolve().parents[1]
GAME_DIR = Path(os.environ.get("CRYSTAL_CAVES_HD_DIR", str(PROJECT_DIR.parent))).resolve()
DATA_DIR = GAME_DIR / "Crystal Caves HD_Data"
CATALOG_DIR = PROJECT_DIR / "catalog"
TEXTURE_DIR = PROJECT_DIR / "textures" / "original"
UNITY_VERSION = "2019.4.41f1"

SCENE_FILES = [DATA_DIR / f"level{i}" for i in range(9)]
ASSET_FILES = [
    DATA_DIR / "resources.assets",
    DATA_DIR / "globalgamemanagers.assets",
    *sorted(DATA_DIR.glob("sharedassets*.assets")),
]

TEXT_COMPONENTS = {"TextMeshProUGUI", "TextMeshPro", "Text"}
TEXT_FIELDS = ("m_text", "m_Text")
TEXTURE_EXPORT_NAMES = {
    "map_tiles",
    "cc_tileset2",
    "cc_pickups",
    "screen_main_menu",
}

ASSET_TRANSLATION_RULES: dict[str, re.Pattern[str]] = {
    "AchievementsBank": re.compile(r"achievements\[\d+\]\.(?:name|description)$"),
    "EpisodeInfo": re.compile(r"episodeTitle$"),
    "StoryWindow": re.compile(r"entries\[\d+\]\.text$"),
    "IntroSceneEp4": re.compile(r"sceneActions\[\d+\]\.messageText$"),
    "TextDisplayer": re.compile(r"text$"),
    "Kiwi": re.compile(r"actions\[\d+\]\.messageOnAction$"),
    "PrefObject": re.compile(r"objectName$"),
    "PrefPickup": re.compile(r"pickupName$"),
    "PrefEnemy": re.compile(r"enemyName$"),
    "OptionLabel": re.compile(r"extraTextValue$"),
    "MenuButton": re.compile(r"tooltip$"),
    "UIEditModeButton": re.compile(r"tooltip$"),
}

ASSET_CATEGORIES = {
    "AchievementsBank": "achievements",
    "EpisodeInfo": "episodes",
    "StoryWindow": "story",
    "IntroSceneEp4": "story",
    "TextDisplayer": "story",
    "Kiwi": "story",
    "PrefObject": "editor",
    "PrefPickup": "editor",
    "PrefEnemy": "editor",
    "OptionLabel": "ui",
    "MenuButton": "ui",
    "UIEditModeButton": "ui",
}

CODE_UI_CONTEXT = re.compile(
    r"ShowMessage\s*\(|ShowNotification\s*\(|\.text\s*[+]?=|SetText\s*\(|"
    r"DisplayText\s*\(|ShowPrompt\s*\(|messageText\s*=",
    re.IGNORECASE,
)
CODE_TECHNICAL = re.compile(
    r"(?:\[Tooltip\(|LoggedIn|HasAppLicense|Entries:|does not exist|Autosave|"
    r"^A$|^INVALID$|^Unnamed$|^Update$|^Page\s*$|^Episode\s*$|^Extra\s*$)",
    re.IGNORECASE,
)

TMP_TAG = re.compile(r"<[^>]+>")
LATIN = re.compile(r"[A-Za-z]")
TECHNICAL = re.compile(
    r"^(?:Assets[/\\]|Library[/\\]|https?://|[A-Za-z]:[/\\]|"
    r"UnityEngine\.|System\.|TMPro\.|Hidden/|Sprites/|Shader |"
    r"[a-z]+://)",
    re.IGNORECASE,
)


def write_csv(path: Path, rows: list[dict[str, Any]], columns: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def flatten_strings(value: Any, path: str = "") -> Iterable[tuple[str, str]]:
    if isinstance(value, str):
        yield path, value
        return
    if isinstance(value, dict):
        for key, child in value.items():
            child_path = f"{path}.{key}" if path else str(key)
            yield from flatten_strings(child, child_path)
        return
    if isinstance(value, (list, tuple)):
        for index, child in enumerate(value):
            yield from flatten_strings(child, f"{path}[{index}]")


def visible_text(value: str, field: str = "") -> bool:
    plain = TMP_TAG.sub("", value).strip()
    if not plain or not LATIN.search(plain):
        return False
    if field.endswith("m_Name") or field == "m_Name":
        return False
    if TECHNICAL.search(plain):
        return False
    if "{0:" in plain or "%Y" in plain or "Assembly-CSharp" in plain:
        return False
    if len(plain) > 5000:
        return False
    return True


def script_info(base: Any) -> tuple[str, str, str]:
    script = base.m_Script.read()
    namespace = script.m_Namespace or ""
    fullname = f"{namespace}.{script.m_ClassName}" if namespace else script.m_ClassName
    return script.m_AssemblyName, fullname, script.m_ClassName


def read_monobehaviour(reader: Any, generator: TypeTreeGenerator) -> tuple[Any, dict[str, Any], str]:
    base = reader.read(check_read=False)
    assembly, fullname, class_name = script_info(base)
    nodes = generator.get_nodes_up(assembly, fullname)
    tree = reader.read_typetree(nodes=nodes)
    return base, tree, class_name


def game_object_name(base: Any) -> str:
    try:
        return base.m_GameObject.read().m_Name
    except Exception:
        return ""


def scan_serialized_files(generator: TypeTreeGenerator) -> tuple[list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]]]:
    scene_rows: list[dict[str, Any]] = []
    asset_rows: list[dict[str, Any]] = []
    texture_rows: list[dict[str, Any]] = []
    font_rows: list[dict[str, Any]] = []
    files = [path for path in [*SCENE_FILES, *ASSET_FILES] if path.exists()]

    exported_textures: set[tuple[str, int]] = set()
    seen_objects: set[tuple[str, int]] = set()

    for source_path in files:
        environment = UnityPy.load(str(source_path))
        for reader in environment.objects:
            asset_name = Path(reader.assets_file.name).name
            object_key = (asset_name, reader.path_id)
            if object_key in seen_objects:
                continue
            seen_objects.add(object_key)

            if reader.type.name == "MonoBehaviour":
                try:
                    base, tree, class_name = read_monobehaviour(reader, generator)
                except Exception:
                    continue
                object_name = game_object_name(base) or tree.get("m_Name", "")

                if class_name in TEXT_COMPONENTS:
                    for field in TEXT_FIELDS:
                        if field in tree and isinstance(tree[field], str):
                            original = tree[field]
                            scene_rows.append(
                                {
                                    "source_file": asset_name,
                                    "path_id": reader.path_id,
                                    "game_object": object_name,
                                    "component": class_name,
                                    "field": field,
                                    "original": original,
                                    "user_visible": visible_text(original, field),
                                }
                            )
                            break
                    continue

                for field, original in flatten_strings(tree):
                    if not original:
                        continue
                    asset_rows.append(
                        {
                            "source_file": asset_name,
                            "path_id": reader.path_id,
                            "object_name": object_name,
                            "component": class_name,
                            "field": field,
                            "original": original,
                            "user_visible": visible_text(original, field),
                        }
                    )

            elif reader.type.name == "Texture2D":
                try:
                    texture = reader.read()
                    row = {
                        "source_file": asset_name,
                        "path_id": reader.path_id,
                        "name": texture.m_Name,
                        "width": texture.m_Width,
                        "height": texture.m_Height,
                        "format": str(texture.m_TextureFormat),
                        "stream_path": getattr(getattr(texture, "m_StreamData", None), "path", ""),
                        "stream_offset": getattr(getattr(texture, "m_StreamData", None), "offset", ""),
                        "stream_size": getattr(getattr(texture, "m_StreamData", None), "size", ""),
                    }
                    texture_rows.append(row)
                    export_key = (asset_name, reader.path_id)
                    if texture.m_Name in TEXTURE_EXPORT_NAMES and export_key not in exported_textures:
                        exported_textures.add(export_key)
                        safe_asset = re.sub(r"[^A-Za-z0-9_.-]", "_", Path(asset_name).stem)
                        filename = f"{safe_asset}__{reader.path_id}__{texture.m_Name}.png"
                        texture.image.save(TEXTURE_DIR / filename)
                except Exception:
                    continue

            elif reader.type.name == "Font":
                try:
                    font = reader.read()
                    font_data = bytes(getattr(font, "m_FontData", b""))
                    cyrillic = False
                    glyph_count = ""
                    if font_data:
                        try:
                            from fontTools.ttLib import TTFont

                            parsed = TTFont(io.BytesIO(font_data))
                            characters: set[int] = set()
                            for table in parsed["cmap"].tables:
                                if table.isUnicode():
                                    characters.update(table.cmap)
                            required = set(map(ord, "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя"))
                            cyrillic = required.issubset(characters)
                            glyph_count = len(characters)
                        except Exception:
                            pass
                    font_rows.append(
                        {
                            "source_file": asset_name,
                            "path_id": reader.path_id,
                            "name": font.m_Name,
                            "embedded_bytes": len(font_data),
                            "unicode_characters": glyph_count,
                            "russian_alphabet": cyrillic,
                        }
                    )
                except Exception:
                    continue

    return scene_rows, asset_rows, texture_rows, font_rows


CSHARP_LITERAL = re.compile(r'(?<![\w\'])"((?:\\.|[^"\\])*)"')


def decode_csharp_string(value: str) -> str:
    replacements = {
        r"\n": "\n",
        r"\r": "\r",
        r"\t": "\t",
        r'\"': '"',
        r"\\": "\\",
        r"\0": "\0",
    }
    value = re.sub(
        r"\\u([0-9a-fA-F]{4})",
        lambda match: chr(int(match.group(1), 16)),
        value,
    )
    return re.sub(
        r'\\[nrt"\\0]',
        lambda match: replacements.get(match.group(0), match.group(0)),
        value,
    )


def scan_code_strings() -> list[dict[str, Any]]:
    assembly = DATA_DIR / "Managed" / "Assembly-CSharp.dll"
    ilspy = shutil.which("ilspycmd")
    if not assembly.exists() or not ilspy:
        return []

    temp_dir = Path(tempfile.mkdtemp(prefix="cchd-ru-ilspy-"))
    try:
        subprocess.run(
            [ilspy, "-o", str(temp_dir), str(assembly)],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        source_path = next(temp_dir.glob("*.decompiled.cs"))
        source = source_path.read_text(encoding="utf-8-sig")
        rows: list[dict[str, Any]] = []
        current_type = ""
        type_pattern = re.compile(r"^(?:public|private|internal|protected|sealed|static|abstract|partial|\s)+\s*(?:class|struct|enum)\s+(\w+)")
        for line_number, line in enumerate(source.splitlines(), start=1):
            type_match = type_pattern.match(line.strip())
            if type_match:
                current_type = type_match.group(1)
            for match in CSHARP_LITERAL.finditer(line):
                original = decode_csharp_string(match.group(1))
                if not original:
                    continue
                rows.append(
                    {
                        "source_file": "Assembly-CSharp.dll",
                        "line": line_number,
                        "type": current_type,
                        "original": original,
                        "user_visible": visible_text(original),
                        "context": line.strip()[:500],
                    }
                )
        return rows
    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)


def unique_visible(rows: list[dict[str, Any]]) -> list[str]:
    return sorted(
        {str(row["original"]) for row in rows if row.get("user_visible")},
        key=lambda value: value.casefold(),
    )


def build_translation_candidates(
    scene_rows: list[dict[str, Any]],
    asset_rows: list[dict[str, Any]],
    code_rows: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    grouped: dict[str, dict[str, Any]] = {}

    def add(original: str, category: str, location: str, priority: int) -> None:
        row = grouped.setdefault(
            original,
            {
                "priority": priority,
                "category": category,
                "original": original,
                "russian": "",
                "status": "TODO",
                "occurrences": 0,
                "locations": [],
            },
        )
        row["priority"] = min(row["priority"], priority)
        if category not in row["category"].split("+"):
            row["category"] += f"+{category}"
        row["occurrences"] += 1
        if location not in row["locations"] and len(row["locations"]) < 8:
            row["locations"].append(location)

    for row in scene_rows:
        if not row["user_visible"]:
            continue
        add(
            str(row["original"]),
            "ui",
            f"{row['source_file']}:{row['path_id']}:{row['game_object']}",
            1,
        )

    for row in asset_rows:
        if not row["user_visible"]:
            continue
        rule = ASSET_TRANSLATION_RULES.get(str(row["component"]))
        if rule is None or not rule.search(str(row["field"])):
            continue
        category = ASSET_CATEGORIES[str(row["component"])]
        add(
            str(row["original"]),
            category,
            f"{row['source_file']}:{row['path_id']}:{row['component']}.{row['field']}",
            2 if category == "editor" else 1,
        )

    for row in code_rows:
        original = str(row["original"])
        context = str(row["context"])
        if not row["user_visible"] or not CODE_UI_CONTEXT.search(context):
            continue
        if CODE_TECHNICAL.search(original) or CODE_TECHNICAL.search(context):
            continue
        add(
            original,
            "code",
            f"Assembly-CSharp.dll:{row['line']}",
            1,
        )

    output = []
    for row in grouped.values():
        row["locations"] = " | ".join(row["locations"])
        output.append(row)
    return sorted(output, key=lambda row: (row["priority"], row["category"], row["original"].casefold()))


def main() -> None:
    if not DATA_DIR.exists():
        raise SystemExit(f"Game data directory not found: {DATA_DIR}")

    CATALOG_DIR.mkdir(parents=True, exist_ok=True)
    TEXTURE_DIR.mkdir(parents=True, exist_ok=True)

    generator = TypeTreeGenerator(UNITY_VERSION)
    generator.load_local_game(str(GAME_DIR))

    scene_rows, asset_rows, texture_rows, font_rows = scan_serialized_files(generator)
    code_rows = scan_code_strings()

    write_csv(
        CATALOG_DIR / "scene_strings.csv",
        scene_rows,
        ["source_file", "path_id", "game_object", "component", "field", "original", "user_visible"],
    )
    write_csv(
        CATALOG_DIR / "asset_strings.csv",
        asset_rows,
        ["source_file", "path_id", "object_name", "component", "field", "original", "user_visible"],
    )
    write_csv(
        CATALOG_DIR / "code_strings.csv",
        code_rows,
        ["source_file", "line", "type", "original", "user_visible", "context"],
    )
    write_csv(
        CATALOG_DIR / "textures.csv",
        texture_rows,
        ["source_file", "path_id", "name", "width", "height", "format", "stream_path", "stream_offset", "stream_size"],
    )
    write_csv(
        CATALOG_DIR / "fonts.csv",
        font_rows,
        ["source_file", "path_id", "name", "embedded_bytes", "unicode_characters", "russian_alphabet"],
    )

    candidates = build_translation_candidates(scene_rows, asset_rows, code_rows)
    write_csv(
        CATALOG_DIR / "translation_candidates.csv",
        candidates,
        ["priority", "category", "original", "russian", "status", "occurrences", "locations"],
    )

    sources = Counter()
    for row in scene_rows:
        if row["user_visible"]:
            sources["scene_ui"] += 1
    for row in asset_rows:
        if row["user_visible"]:
            sources["serialized_assets"] += 1
    for row in code_rows:
        if row["user_visible"]:
            sources["code_literals"] += 1

    summary = {
        "game_directory": str(GAME_DIR),
        "unity_version": UNITY_VERSION,
        "files_scanned": len([path for path in [*SCENE_FILES, *ASSET_FILES] if path.exists()]),
        "rows": {
            "scene_strings": len(scene_rows),
            "asset_strings": len(asset_rows),
            "code_strings": len(code_rows),
            "textures": len(texture_rows),
            "fonts": len(font_rows),
        },
        "visible_occurrences": dict(sources),
        "unique_translation_candidates": len(candidates),
        "selected_textures_exported": len(list(TEXTURE_DIR.glob("*.png"))),
    }
    (CATALOG_DIR / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
