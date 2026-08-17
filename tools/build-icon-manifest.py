#!/usr/bin/env python3
"""Builds src/PoeOverlay.Shell/Icons/item-icons.json — the slug -> icon file map (FR-04-6).

Inputs
    data/statics.json       the Korean GGG trade static response (carries entries[].image)
    data/ko-items.json      slug -> Korean name, the committed record of the join in
                            00-api-contract.md 6.3
    data/images/            the icons themselves, as fetch-ko-sources.py --icons saved them

Run it from the repository root, after build-ko-dictionary.py:

    python3 tools/build-icon-manifest.py

Nothing in the build calls this (S4 2.5). The manifest is committed; an ordinary build only
copies files.

The join goes slug -> Korean name -> GGG static entry -> image path -> file name. It reaches
the same entry as 00-api-contract.md 6.3 by the other end of the same pair: that chain went
slug -> English name -> entry, and ko-items.json is what it produced. Going back through the
Korean name is exact only while no two entries share a Korean name and disagree about the
icon -- so that is checked below rather than assumed. (Sixteen names do disagree, all of them
`지도 (N등급)`, and no poe.ninja slug reaches any of them today.)

Divination cards have no image of their own in either source. That is not a gap: in game they
all share one icon (00-api-contract.md 6.6), and the shared art is DivinationCard.png.

Like build-ko-dictionary.py, this refuses to write a manifest that would be silently degraded
at runtime -- ItemIconSource drops a bad entry and draws the row without an icon, so a wrong
manifest costs a picture and nothing fails.
"""

import collections
import json
import pathlib
import struct
import sys
import zlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
STATICS_KO = ROOT / "data" / "statics.json"
KO_ITEMS = ROOT / "data" / "ko-items.json"
IMAGES = ROOT / "data" / "images"
OUT = ROOT / "src" / "PoeOverlay.Shell" / "Icons" / "item-icons.json"

# The one icon that is not in entries[].image. Source and measurement: 00-api-contract.md 6.6.
CARD_ICON = "DivinationCard.png"

# The GGG static group whose entries are divination cards.
CARD_GROUP = "Cards"

# The overlay's LWA_COLORKEY, as (r, g, b). An opaque pixel of exactly this colour becomes a
# hole in the window and passes clicks through (00-shell-measurements.md 14).
COLOR_KEY = (255, 0, 255)


def load(path):
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def entries(document):
    return [entry for group in document["result"] for entry in group.get("entries", [])]


def file_names(document):
    """image path -> saved file name, using fetch-ko-sources.py's collision rule.

    Kept identical to icon_config() there: 675 distinct paths share only 640 basenames.
    """
    paths = sorted({entry["image"] for entry in entries(document) if entry.get("image")})
    counts = collections.Counter(path.rsplit("/", 1)[-1] for path in paths)

    names = {}
    for path in paths:
        segments = path.split("/")
        base = segments[-1]
        names[path] = base if counts[base] == 1 else f"{base[:-4]}__{segments[-2]}.png"
    return names


def decode_png(path):
    """(width, height, channels, colour type, palette, tRNS, pixel bytes). No PIL here."""
    raw = path.read_bytes()
    idat, palette, transparency = b"", None, None
    width = height = depth = colour = None

    offset = 8
    while offset < len(raw):
        (length,) = struct.unpack(">I", raw[offset:offset + 4])
        kind = raw[offset + 4:offset + 8]
        body = raw[offset + 8:offset + 8 + length]
        offset += 12 + length

        if kind == b"IHDR":
            width, height, depth, colour, _, _, interlace = struct.unpack(">IIBBBBB", body[:13])
            if interlace:
                raise ValueError(f"{path.name}: interlaced PNG, not handled")
            if depth != 8:
                raise ValueError(f"{path.name}: bit depth {depth}, not handled")
        elif kind == b"IDAT":
            idat += body
        elif kind == b"PLTE":
            palette = body
        elif kind == b"tRNS":
            transparency = body

    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[colour]
    data = zlib.decompress(idat)
    stride = width * channels
    out = bytearray(stride * height)
    previous = bytearray(stride)
    pos = 0

    for row in range(height):
        filter_type = data[pos]
        pos += 1
        line = bytearray(data[pos:pos + stride])
        pos += stride

        if filter_type == 1:
            for x in range(channels, stride):
                line[x] = (line[x] + line[x - channels]) & 0xFF
        elif filter_type == 2:
            for x in range(stride):
                line[x] = (line[x] + previous[x]) & 0xFF
        elif filter_type == 3:
            for x in range(stride):
                left = line[x - channels] if x >= channels else 0
                line[x] = (line[x] + ((left + previous[x]) >> 1)) & 0xFF
        elif filter_type == 4:
            for x in range(stride):
                left = line[x - channels] if x >= channels else 0
                upper_left = previous[x - channels] if x >= channels else 0
                up = previous[x]
                estimate = left + up - upper_left
                da, db, dc = abs(estimate - left), abs(estimate - up), abs(estimate - upper_left)
                if da <= db and da <= dc:
                    predictor = left
                elif db <= dc:
                    predictor = up
                else:
                    predictor = upper_left
                line[x] = (line[x] + predictor) & 0xFF
        elif filter_type != 0:
            raise ValueError(f"{path.name}: filter type {filter_type}")

        out[row * stride:(row + 1) * stride] = line
        previous = line

    return channels, colour, palette, transparency, out


def key_pixels(path):
    """(exact colour-key hits, Chebyshev distance of the nearest opaque pixel to the key).

    Only fully opaque pixels can reach the key: anything translucent is blended onto the body
    panel (#1E1E1E) before the key is applied, which moves it away from magenta.
    """
    channels, colour, palette, transparency, pixels = decode_png(path)
    hits, nearest = 0, 255

    for i in range(0, len(pixels), channels):
        if colour == 6:
            r, g, b, a = pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]
        elif colour == 2:
            r, g, b, a = pixels[i], pixels[i + 1], pixels[i + 2], 255
        elif colour == 3:
            index = pixels[i]
            r, g, b = palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2]
            a = transparency[index] if transparency and index < len(transparency) else 255
        elif colour == 4:
            r = g = b = pixels[i]
            a = pixels[i + 1]
        else:
            r = g = b = pixels[i]
            a = 255

        if a != 255:
            continue
        if (r, g, b) == COLOR_KEY:
            hits += 1
        distance = max(abs(r - COLOR_KEY[0]), abs(g - COLOR_KEY[1]), abs(b - COLOR_KEY[2]))
        nearest = min(nearest, distance)

    return hits, nearest


def main():
    statics = load(STATICS_KO)
    items = load(KO_ITEMS)
    names = file_names(statics)

    # Korean display name -> {icon file}, and -> {static group}. Both are sets on purpose: a name
    # that lands in more than one icon is the failure this script exists to refuse.
    by_name = collections.defaultdict(set)
    groups = collections.defaultdict(set)
    for group in statics["result"]:
        for entry in group.get("entries", []):
            text = entry.get("text", "").strip()
            if not text:
                continue
            groups[text].add(group["id"])
            if entry.get("image"):
                by_name[text].add(names[entry["image"]])

    manifest, ambiguous, unmapped = {}, [], []
    cards = 0

    for slug, korean in sorted(items.items()):
        icons = by_name.get(korean, set())
        if len(icons) > 1:
            ambiguous.append((slug, korean, sorted(icons)))
        elif icons:
            manifest[slug] = next(iter(icons))
        elif groups.get(korean) == {CARD_GROUP}:
            manifest[slug] = CARD_ICON
            cards += 1
        else:
            unmapped.append((slug, korean, sorted(groups.get(korean, set()))))

    problems = []

    for slug, korean, icons in ambiguous:
        problems.append(
            f"{slug} ({korean}): {len(icons)} different icons share that Korean name ({icons}). "
            "Picking one would draw the wrong picture -- join through the English name instead."
        )

    missing = sorted({name for name in manifest.values() if not (IMAGES / name).is_file()})
    if missing:
        problems.append(
            f"{len(missing)} referenced icon(s) are not in data/images/: {missing[:10]}"
            f"{' ...' if len(missing) > 10 else ''}. Re-run fetch-ko-sources.py --icons and the curl pull."
        )

    bad_names = sorted({name for name in manifest.values() if "/" in name or "\\" in name or ".." in name})
    if bad_names:
        problems.append(f"file names must stay inside Icons/: {bad_names}")

    # Every card must be covered, or half the catalogue renders blank.
    uncovered_cards = [slug for slug, korean, _ in unmapped if groups.get(korean) == {CARD_GROUP}]
    if uncovered_cards:
        problems.append(f"{len(uncovered_cards)} divination card(s) got no icon: {uncovered_cards[:10]}")

    # The colour-key scan. 00-shell-measurements.md 14 measured 0 hits with 4/255 to spare; that
    # margin guarantees nothing about art a future league brings in.
    worst_file, worst_distance = None, 255
    if not missing:
        for name in sorted(set(manifest.values())):
            hits, nearest = key_pixels(IMAGES / name)
            if hits:
                problems.append(
                    f"{name}: {hits} opaque pixel(s) are exactly the overlay's colour key "
                    f"#FF00FF -- they would punch a click-through hole "
                    f"(00-shell-measurements.md 14)"
                )
            if nearest < worst_distance:
                worst_file, worst_distance = name, nearest

    if problems:
        print("refusing to write item-icons.json:", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1

    OUT.parent.mkdir(parents=True, exist_ok=True)
    with OUT.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=2, sort_keys=True)
        handle.write("\n")

    individual = len(manifest) - cards
    print(f"wrote {OUT.relative_to(ROOT)}: {len(manifest)} slugs "
          f"({individual} individual icons + {cards} divination cards)")
    print(f"  distinct files referenced: {len(set(manifest.values()))} of {len(list(IMAGES.glob('*.png')))} on disk")
    print(f"  closest opaque pixel to the colour key: distance {worst_distance} in {worst_file}")
    if unmapped:
        print(f"  {len(unmapped)} slug(s) have no icon and are not cards -- they render with a blank slot:")
        for slug, korean, in_groups in unmapped[:10]:
            print(f"    {slug} ({korean}) in {in_groups or ['<not in statics>']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
