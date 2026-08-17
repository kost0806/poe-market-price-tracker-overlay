#!/usr/bin/env python3
"""Builds src/PoeOverlay.Core/Localization/Localization/ko.json.

Inputs
    tools/ko-ui.json        hand written ui.* strings (the half GGG does not supply)
    data/ko-items.json      slug -> Korean name, generated from the two GGG static responses
    src/.../en.json         the key catalogue this must agree with

Run it from the repository root:

    python3 tools/build-ko-dictionary.py

Nothing in the build calls this (S4 2.5): ko.json is committed, so an ordinary build only
copies a file. Re-run it when data/ko-items.json is regenerated -- that is, when the Korean
client catches up with a new league (00-api-contract.md 6.4) and fetch-ko-sources.py has
pulled fresh statics.

It refuses to write a dictionary that would be silently degraded at load time, because
LocalizationCatalog drops a bad entry and carries on (S2 3.7 D-L3): a placeholder count that
disagrees with the catalogue would cost that one string and nothing would fail.
"""

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
KO_UI = ROOT / "tools" / "ko-ui.json"
KO_ITEMS = ROOT / "data" / "ko-items.json"
EN = ROOT / "src" / "PoeOverlay.Core" / "Localization" / "Localization" / "en.json"
OUT = ROOT / "src" / "PoeOverlay.Core" / "Localization" / "Localization" / "ko.json"

# The one ui.* key with no measured Korean term. See the note in ko-ui.json: neither GGG static
# response contains "Djinn" in any form, and poe.ninja lists no items under that type. The
# fallback chain shows the English, which is the honest answer and stays visible as a gap.
UNTRANSLATED = {"ui.category.djinnCoin"}

PLACEHOLDER = re.compile(r"(?<!\{)\{(\d+)\}")


def placeholders(value):
    return {int(index) for index in PLACEHOLDER.findall(value)}


def load(path):
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def main():
    en = load(EN)
    ui = {key: value for key, value in load(KO_UI).items() if not key.startswith("_")}
    items = load(KO_ITEMS)

    problems = []

    # Every ui.* key en.json has must be answered, or that string silently stays English.
    expected = {key for key in en if key.startswith("ui.")}
    missing = expected - set(ui) - UNTRANSLATED
    if missing:
        problems.append(f"ko-ui.json is missing {len(missing)} ui.* key(s): {sorted(missing)}")

    unexpected = set(ui) - expected
    if unexpected:
        problems.append(f"ko-ui.json has {len(unexpected)} key(s) en.json does not: {sorted(unexpected)}")

    stale = UNTRANSLATED & set(ui)
    if stale:
        problems.append(
            f"{sorted(stale)} is listed as untranslated but ko-ui.json now has it. "
            "Drop it from UNTRANSLATED and say in ko-ui.json where the term was measured."
        )

    # The check that matters: a mismatch here is not a build failure, it is a string that
    # disappears at runtime with only a warning in the log.
    for key, value in sorted(ui.items()):
        if placeholders(value) != placeholders(en[key]):
            problems.append(
                f"{key}: placeholders {sorted(placeholders(value))} differ from en.json's "
                f"{sorted(placeholders(en[key]))} -- LocalizationCatalog would drop this entry"
            )

    # Slugs share the file with ui.* keys, told apart by prefix (S2 3.1). A slug that begins
    # with "ui." would be read as an interface key and would never resolve as a name.
    for slug in items:
        if slug.startswith("ui."):
            problems.append(f"item slug {slug!r} collides with the ui.* key space")

    if problems:
        print("refusing to write ko.json:", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1

    merged = dict(sorted(items.items()))
    merged.update(dict(sorted(ui.items())))

    with OUT.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(merged, handle, ensure_ascii=False, indent=2, sort_keys=True)
        handle.write("\n")

    print(f"wrote {OUT.relative_to(ROOT)}: {len(items)} item names + {len(ui)} ui strings")
    if UNTRANSLATED:
        print(f"  untranslated on purpose: {sorted(UNTRANSLATED)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
