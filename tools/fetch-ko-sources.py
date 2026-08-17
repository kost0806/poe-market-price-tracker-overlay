#!/usr/bin/env python3
"""Re-fetches the two GGG trade static responses and rebuilds data/ko-items.json.

This is the only script here that needs the network. Run it when the Korean client catches up
with a league and the slugs listed in data/ko-items.meta.json stop being unresolved
(00-api-contract.md 6.4), then run build-ko-dictionary.py.

    python3 tools/fetch-ko-sources.py            # statics + ko-items.json
    python3 tools/fetch-ko-sources.py --icons    # also writes curl.cfg for the icon pull

Then rebuild both committed artefacts: build-ko-dictionary.py (names) and
build-icon-manifest.py (icons). They read what this script wrote and need no network.

Two things about the endpoints, both measured (00-api-contract.md 6.1):

  * poe.game.daum.net is a 301. Follow it or you get 167 bytes of HTML. urllib follows
    redirects; a hand written request with a fixed Host header does not.
  * www.pathofexile.com answers a custom User-Agent with a 403 from Cloudflare. The browser
    UA below is not decoration -- without it the English half is 5,489 bytes of HTML.
"""

import argparse
import collections
import json
import pathlib
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA = ROOT / "data"

EN_STATIC = "https://www.pathofexile.com/api/trade/data/static"
KO_STATIC = "https://poe.kakaogames.com/api/trade/data/static"
NINJA_LEAGUES = "https://poe.ninja/poe1/api/economy/leagues"
NINJA_OVERVIEW = "https://poe.ninja/poe1/api/economy/exchange/current/overview?league={league}&type={type}"
CDN = "https://web.poecdn.com"
# The shared divination card icon. Measured 2026-08-17: 200, 78x78 RGBA PNG.
# The /gen/image/ form of the same art answers 404 (00-api-contract.md 6.6).
CARD_ICON_URL = "https://web.poecdn.com/image/Art/2DItems/Divination/InventoryIcon.png"

HEADERS = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36"
    ),
    "Accept": "application/json",
}

# poe.ninja's eighteen exchange types, which are also ExchangeCategory's members (S4 3.3).
CATEGORIES = [
    "Currency", "Fragment", "Runegraft", "AllflameEmber", "Tattoo", "Omen", "DjinnCoin",
    "Ducat", "EnshroudingCrystal", "DivinationCard", "Artifact", "Oil", "DeliriumOrb",
    "Scarab", "Astrolabe", "Fossil", "Resonator", "Essence",
]


def get(url):
    request = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read().decode("utf-8"))


def entries(document):
    return [entry for group in document["result"] for entry in group.get("entries", [])]


def build_name_map(en, ko):
    """English display name -> Korean, joined by position with the id asserted each time.

    The assert is the whole safety of this join (00-api-contract.md 6.3). The two responses
    agree on entry order, so if one of them is from a different day the pairing slides by one
    and every name after that point is wrong -- quietly, and in a way no later check notices.
    """
    en_entries, ko_entries = entries(en), entries(ko)
    if len(en_entries) != len(ko_entries):
        raise SystemExit(
            f"static responses disagree in size ({len(en_entries)} vs {len(ko_entries)}); "
            "re-fetch both so they are from the same moment"
        )

    names = {}
    for english, korean in zip(en_entries, ko_entries):
        assert english["id"] == korean["id"], (english["id"], korean["id"])
        english_text, korean_text = english.get("text", "").strip(), korean.get("text", "").strip()
        if english_text and korean_text:
            names.setdefault(english_text, korean_text)
    return names


def icon_config(ko):
    """Writes curl.cfg for the icon pull.

    Colliding basenames get the second-to-last path segment appended: 675 distinct paths share
    only 640 basenames, so saving by basename alone silently overwrites 35 images
    (00-api-contract.md 6.6).
    """
    paths = sorted({e["image"] for e in entries(ko) if e.get("image")})
    counts = collections.Counter(path.rsplit("/", 1)[-1] for path in paths)

    lines = []
    for path in paths:
        segments = path.split("/")
        base = segments[-1]
        name = base if counts[base] == 1 else f"{base[:-4]}__{segments[-2]}.png"
        lines.append(f'url = "{CDN}{path}"\noutput = "data/images/{name}"')

    # The 676th. Divination cards carry no `image` at all -- they share one icon in game
    # (00-api-contract.md 6.6) -- so this one comes from the raw art path rather than /gen/image,
    # which 404s for it. Without it 392 of 968 slugs render with a blank slot.
    lines.append(f'url = "{CARD_ICON_URL}"\noutput = "data/images/DivinationCard.png"')

    (ROOT / "curl.cfg").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"wrote curl.cfg for {len(paths)} icons + the shared divination card icon. Now run:")
    print("  curl -sS --fail --create-dirs --retry 3 --connect-timeout 20 --max-time 60 \\")
    print('       --parallel --parallel-max 8 -A "Mozilla/5.0" -K curl.cfg')


def fetch_ninja():
    """slug -> {en, cat} for every exchange category, saved as data/ninja-items.json.

    The category half is the reason this is written out rather than kept in a local. It is the
    only place either source states which category a slug belongs to, and the shipped catalogue
    (build-item-catalog.py) cannot be regenerated without it -- the GGG static groups are GGG's
    own and their entry ids are trade ids ("alt"), not poe.ninja slugs.

    The first version of this script threw the categories away here, which is why the app could
    ship 968 item names and still be unable to search for a scarab.
    """
    league = get(NINJA_LEAGUES)[0]["id"]
    print(f"poe.ninja league: {league}")

    ninja = {}
    for category in CATEGORIES:
        overview = get(NINJA_OVERVIEW.format(league=league, type=category))
        for item in overview.get("items", []):
            ninja[item["id"]] = {"en": item.get("name", ""), "cat": category}

    (DATA / "ninja-items.json").write_text(
        json.dumps(
            {"generatedFor": league, "items": dict(sorted(ninja.items()))},
            ensure_ascii=False,
            indent=1,
        ) + "\n",
        encoding="utf-8",
    )
    print(f"wrote data/ninja-items.json: {len(ninja)} slugs over {len(CATEGORIES)} categories")
    return league, ninja


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--icons", action="store_true", help="also write curl.cfg for the icon pull")
    parser.add_argument(
        "--catalog-only",
        action="store_true",
        help="fetch only the poe.ninja overviews (data/ninja-items.json) and leave the statics alone",
    )
    args = parser.parse_args()

    if args.catalog_only:
        fetch_ninja()
        print("\nnow run: python3 tools/build-item-catalog.py")
        return 0

    print("fetching English static ...")
    en = get(EN_STATIC)
    print("fetching Korean static ...")
    ko = get(KO_STATIC)

    (DATA / "statics.en.json").write_text(
        json.dumps(en, ensure_ascii=False, indent=1), encoding="utf-8")
    (DATA / "statics.json").write_text(
        json.dumps(ko, ensure_ascii=False, indent=1), encoding="utf-8")

    names = build_name_map(en, ko)
    print(f"joined {len(names)} English names to Korean")

    league, ninja = fetch_ninja()

    resolved = {slug: names[meta["en"]] for slug, meta in sorted(ninja.items()) if meta["en"] in names}
    unresolved = [
        {"slug": slug, **meta} for slug, meta in sorted(ninja.items()) if meta["en"] not in names
    ]

    (DATA / "ko-items.json").write_text(
        json.dumps(resolved, ensure_ascii=False, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    (DATA / "ko-items.meta.json").write_text(
        json.dumps(
            {
                "generatedFor": league,
                "sources": {
                    "ko": f"{KO_STATIC} -> data/statics.json",
                    "en": f"{EN_STATIC} -> data/statics.en.json",
                    "slugs": "poe.ninja/poe1/api/economy/exchange/current/overview",
                },
                "joinKey": "GGG static entry id (positional identity verified), then EN display name -> poe.ninja slug",
                "counts": {
                    "ninjaItems": len(ninja),
                    "resolved": len(resolved),
                    "unresolved": len(unresolved),
                },
                "unresolved": unresolved,
            },
            ensure_ascii=False,
            indent=1,
        ) + "\n",
        encoding="utf-8",
    )

    print(f"resolved {len(resolved)}/{len(ninja)}; {len(unresolved)} unresolved (KR lag, not a defect)")
    for entry in unresolved:
        print(f"  {entry['slug']} ({entry['cat']})")

    if args.icons:
        icon_config(ko)

    print("\nnow run: python3 tools/build-ko-dictionary.py")
    print("then:    python3 tools/build-icon-manifest.py")
    return 0


if __name__ == "__main__":
    sys.exit(main())
