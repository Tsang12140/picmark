"""Build PicMark's private Alibaba PuHuiTi UI font subsets."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont


FAMILY_NAME = "PicMark PuHui UI"
FONT_SOURCES = {
    "Regular": ("Alibaba-PuHuiTi-Regular.ttf", 400),
    "SemiBold": ("Alibaba-PuHuiTi-Medium.ttf", 600),
    "Bold": ("Alibaba-PuHuiTi-Bold.ttf", 700),
}


def collect_ui_characters(project_dir: Path) -> str:
    characters = set(chr(codepoint) for codepoint in range(0x20, 0x7F))
    characters.update("\t\r\n\u00a0\u3000，。！？：；、“”‘’（）【】《》〈〉—…·℃×")

    quoted_text = re.compile(r"""(?s)(?:"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*')""")
    for pattern in ("*.xaml", "*.cs"):
        for path in project_dir.rglob(pattern):
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            text = path.read_text(encoding="utf-8-sig", errors="ignore")
            for match in quoted_text.finditer(text):
                characters.update(match.group(0)[1:-1])

    # UI text can display arbitrary file names. Common CJK glyphs absent from
    # this compact set intentionally fall back to Microsoft YaHei UI.
    return "".join(sorted(characters))


def set_name(font: TTFont, name_id: int, value: str) -> None:
    name_table = font["name"]
    for record in list(name_table.names):
        if record.nameID == name_id:
            name_table.removeNames(
                nameID=name_id,
                platformID=record.platformID,
                platEncID=record.platEncID,
                langID=record.langID,
            )
    name_table.setName(value, name_id, 3, 1, 0x409)
    name_table.setName(value, name_id, 1, 0, 0)


def normalize_identity(font: TTFont, style: str, weight: int) -> None:
    postscript_style = style.replace("SemiBold", "Semibold")
    set_name(font, 1, FAMILY_NAME)
    set_name(font, 2, style)
    set_name(font, 4, f"{FAMILY_NAME} {style}")
    set_name(font, 6, f"PicMarkPuHuiUI-{postscript_style}")
    set_name(font, 16, FAMILY_NAME)
    set_name(font, 17, style)

    os2 = font["OS/2"]
    os2.usWeightClass = weight
    # The 2019 source files use OS/2 v3 but carry newer fsSelection bits.
    # Clear those unsupported flags while normalizing the three UI weights.
    os2.fsSelection &= ~((1 << 5) | (1 << 6) | (1 << 7) | (1 << 8) | (1 << 9))
    if weight >= 700:
        os2.fsSelection |= 1 << 5
    elif weight == 400:
        os2.fsSelection |= 1 << 6

    font["head"].macStyle &= ~1
    if weight >= 700:
        font["head"].macStyle |= 1


def build_subset(source: Path, output: Path, text: str, style: str, weight: int) -> None:
    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.name_legacy = True
    options.name_languages = ["*"]
    options.recommended_glyphs = True
    options.notdef_glyph = True
    options.notdef_outline = True

    font = TTFont(source)
    subsetter = subset.Subsetter(options=options)
    subsetter.populate(text=text)
    subsetter.subset(font)
    normalize_identity(font, style, weight)
    output.parent.mkdir(parents=True, exist_ok=True)
    font.save(output, reorderTables=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("font_source_dir", type=Path)
    parser.add_argument(
        "--project-dir",
        type=Path,
        default=Path(__file__).resolve().parent,
    )
    args = parser.parse_args()

    project_dir = args.project_dir.resolve()
    output_dir = project_dir / "Assets" / "Fonts"
    text = collect_ui_characters(project_dir)

    for style, (file_name, weight) in FONT_SOURCES.items():
        source = args.font_source_dir / file_name
        output = output_dir / f"PicMarkPuHuiUI-{style}.ttf"
        build_subset(source, output, text, style, weight)
        print(f"{output.name}: {output.stat().st_size:,} bytes")

    print(f"Subset characters: {len(text):,}")


if __name__ == "__main__":
    main()
