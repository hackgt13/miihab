#!/usr/bin/env python3
"""The UI seal, checked. See UI.md.

Fails on:
  1. Palette. Every var(--x) under UI/ is defined in Palette.uss, and no stylesheet outside Palette.uss
     holds a hex colour. UI/ additionally holds no rgb()/rgba() except rgba(0, 0, 0, 0), which is the
     absence of a colour rather than one.
  2. Selecting a component. No stylesheet outside UI/ has a .k- selector: a screen picks a variant, it
     does not restyle the component.
  3. The variant API. A component's [UxmlAttribute] is content or a count — string, int, float, bool —
     or an enum declared in the same file. A Color or a length attribute is the seal leaking.
  4. Paint in a sealed screen. A screen whose UXML links UI/Base.uss is sealed: every stylesheet it
     links outside UI/ sets layout only.

Screens that do not link Base.uss yet are legacy. They are counted, not failed, so this can hold the
line from the first commit while screens move over one at a time.

    python3 scripts/check_ui.py            # from the repo root
"""
import argparse
import re
import sys
from pathlib import Path

PAINT = re.compile(
    r'^(color|opacity|scale|letter-spacing|font-size|white-space|text-shadow|word-spacing'
    r'|background(-[a-z-]+)?|border(-(top|right|bottom|left))?(-(width|color|radius))?'
    r'|border-(top|bottom)-(left|right)-radius|padding(-(top|right|bottom|left))?'
    r'|transition(-[a-z-]+)?|-unity-(font|font-definition|font-style|text-align|text-outline(-[a-z-]+)?'
    r'|background-[a-z-]+|slice-[a-z-]+|paragraph-spacing))$')
ALLOWED_ATTRIBUTE_TYPES = {'string', 'int', 'float', 'bool'}


def strip_comments(text):
    return re.sub(r'/\*.*?\*/', '', text, flags=re.S)


def rules(text):
    """(selector, declarations) for each rule in a stylesheet."""
    for match in re.finditer(r'([^{}]+)\{([^{}]*)\}', strip_comments(text)):
        yield match.group(1).strip(), match.group(2)


def declarations(block):
    for part in block.split(';'):
        if ':' in part:
            name, value = part.split(':', 1)
            yield name.strip(), value.strip()


def linked_sheets(uxml):
    text = uxml.read_text()
    for src in re.findall(r'<(?:ui:)?Style\s+src="([^"]+)"', text):
        yield (uxml.parent / src).resolve()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--root', type=Path,
                        default=Path(__file__).resolve().parents[1] / 'unity/KinestheticUnity/Assets/Kinesthetic')
    root = parser.parse_args().root.resolve()
    ui, palette = root / 'UI', root / 'Palette.uss'
    rel = lambda p: p.relative_to(root)
    inside_ui = lambda p: ui in p.parents
    failures = []

    tokens = set(re.findall(r'--([a-z0-9-]+)\s*:', palette.read_text()))
    sheets = sorted(root.rglob('*.uss'))

    # 1. palette
    for sheet in sheets:
        if sheet == palette:
            continue
        text = strip_comments(sheet.read_text())
        for hex_colour in re.findall(r'#[0-9A-Fa-f]{3,8}\b', text):
            failures.append(f'{rel(sheet)}: hex colour {hex_colour} — use a var(--token) from Palette.uss')
        if inside_ui(sheet):
            for token in re.findall(r'var\(--([a-z0-9-]+)\)', text):
                if token not in tokens:
                    failures.append(f'{rel(sheet)}: var(--{token}) is not defined in Palette.uss')
            for literal in re.findall(r'rgba?\([^)]*\)', text):
                if re.sub(r'\s', '', literal) != 'rgba(0,0,0,0)':
                    failures.append(f'{rel(sheet)}: colour literal {literal} — use a var(--token) from Palette.uss')

    # 2. nothing outside UI/ selects a component
    for sheet in sheets:
        if inside_ui(sheet):
            continue
        for selector, _ in rules(sheet.read_text()):
            if re.search(r'\.k-[a-z]', selector):
                failures.append(f'{rel(sheet)}: selector "{selector}" styles a component — pick a variant instead')

    # 3. the variant API is enums, content and counts
    for source in sorted(ui.rglob('*.cs')) if ui.exists() else []:
        text = source.read_text()
        enums = set(re.findall(r'\benum\s+(\w+)', text))
        for kind, name in re.findall(r'\[UxmlAttribute[^\]]*\]\s*public\s+([\w.<>\[\]]+)\s+(\w+)', text):
            if kind not in ALLOWED_ATTRIBUTE_TYPES | enums:
                failures.append(f'{rel(source)}: [UxmlAttribute] {name} is a {kind} — a variant is an enum; '
                                'content is a string, int, float or bool')

    # 4. sealed screens are layout-only
    sealed, legacy = set(), set()
    for uxml in sorted(root.rglob('*.uxml')):
        if inside_ui(uxml):
            continue
        linked = list(linked_sheets(uxml))
        own = [s for s in linked if s != palette.resolve() and not inside_ui(s) and s.exists()]
        (sealed if (ui / 'Base.uss').resolve() in linked else legacy).update(own)
    for sheet in sorted(sealed):
        for selector, block in rules(sheet.read_text()):
            for name, _ in declarations(block):
                if PAINT.match(name):
                    failures.append(f'{rel(sheet)}: "{selector}" sets {name} — a sealed screen sets layout only; '
                                    'the look belongs to a component variant')

    legacy -= sealed
    legacy_paint = sum(1 for sheet in legacy for _, block in rules(sheet.read_text())
                       for name, _ in declarations(block) if PAINT.match(name))

    for failure in failures:
        print('✗', failure)
    print(f'{len(failures)} violation(s). Sealed screen sheets: {len(sealed)}. '
          f'Legacy screen sheets: {len(legacy)}, still setting {legacy_paint} paint declaration(s).')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
