#!/usr/bin/env python3
"""Builds src/PoeOverlay.Core/Catalog/Catalog/item-catalog.json -- slug -> {category, English name}.

Input
    data/ninja-items.json   what fetch-ko-sources.py captured from the eighteen poe.ninja
                            exchange overviews: {"generatedFor": league,
                            "items": {slug: {"en": name, "cat": category}}}

Run it from the repository root:

    python3 tools/fetch-ko-sources.py --catalog-only     # needs the network
    python3 tools/build-item-catalog.py                  # does not

Nothing in the build calls this (S4 2.5). The catalogue is committed; an ordinary build only
copies the file.

Why this file exists at all: the shipped dictionary (ko.json) is slug -> name, and a name alone
cannot be added to the watchlist. A WatchlistEntry carries a category and prices are fetched per
category, so without this map an item the app has never fetched is unreachable -- which is how a
build shipping 115 scarab names could not find a scarab (00-api-contract.md 6.8).

The category is knowable in exactly one place: which overview request answered. It is not in the
response body, and the GGG static grouping is GGG's own -- its entry ids are trade ids ("alt"),
not poe.ninja slugs.
"""

import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / "data" / "ninja-items.json"
TARGET = ROOT / "src" / "PoeOverlay.Core" / "Catalog" / "Catalog" / "item-catalog.json"

# ExchangeCategory's members (S4 3.3). A value outside this set would be dropped in silence by
# the app, taking its items out of search with it, so it is refused here instead.
CATEGORIES = {
    "Currency", "Fragment", "Runegraft", "AllflameEmber", "Tattoo", "Omen", "DjinnCoin",
    "Ducat", "EnshroudingCrystal", "DivinationCard", "Artifact", "Oil", "DeliriumOrb",
    "Scarab", "Astrolabe", "Fossil", "Resonator", "Essence",
}


def main():
    if not SOURCE.exists():
        raise SystemExit(f"{SOURCE} is missing. Run: python3 tools/fetch-ko-sources.py --catalog-only")

    document = json.loads(SOURCE.read_text(encoding="utf-8"))
    items = document.get("items", {})
    league = document.get("generatedFor", "?")

    problems = []
    catalog = {}

    for slug, meta in sorted(items.items()):
        category, english = meta.get("cat", ""), (meta.get("en") or "").strip()

        if category not in CATEGORIES:
            problems.append(f"{slug!r}: category {category!r} is not an ExchangeCategory member")
            continue

        # Slugs and ui.* keys share the dictionary's key space (S2 3.1). Nothing here is written
        # into that file, but a slug shaped like a ui key means the capture is not what we think.
        if not slug or slug.startswith("ui."):
            problems.append(f"{slug!r}: not a usable slug")
            continue

        if not english:
            problems.append(f"{slug!r}: no English name; the row would have nothing to match on")
            continue

        catalog[slug] = {"cat": category, "en": english}

    # A capture that silently returned nothing would otherwise be committed as a valid artefact,
    # and the app would read it as "this league has no items" rather than "the fetch failed".
    if not catalog:
        problems.append("the catalogue is empty")

    if problems:
        print(f"refusing to write {TARGET.name} -- {len(problems)} problem(s):", file=sys.stderr)
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        return 1

    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_text(
        json.dumps(catalog, ensure_ascii=False, indent=1, sort_keys=True) + "\n", encoding="utf-8")

    counts = {}
    for entry in catalog.values():
        counts[entry["cat"]] = counts.get(entry["cat"], 0) + 1

    print(f"wrote {TARGET.relative_to(ROOT)}: {len(catalog)} slugs from league {league}")
    for category in sorted(counts, key=lambda c: -counts[c]):
        print(f"  {category:20} {counts[category]}")
    for category in sorted(CATEGORIES - counts.keys()):
        print(f"  {category:20} 0   (no items in this league -- not a defect)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
