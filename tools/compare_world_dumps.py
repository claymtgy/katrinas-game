"""Compare two world dumps (scripts/world/WorldDump.cs): one built as is, one with
--region-shift=x,0,z. Every piece should either stay put (the home plateau) or have moved by
exactly the shift (a region). Anything else is printed: a place that moved twice (a region
position added to twice), or one that should have moved and didn't.

    python tools/compare_world_dumps.py base.tsv shifted.tsv X Z

A few unmatched pieces of scatter are expected: where trees and plants land depends on what's
nearby, and the regions moved away from the plateau's roads.
"""
import sys
from collections import Counter, defaultdict


def load(path):
    rows = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            kind, x, z = line.rstrip("\n").split("\t")
            rows.append((kind, float(x), float(z)))
    return rows


def main():
    base, shifted = load(sys.argv[1]), load(sys.argv[2])
    sx, sz = float(sys.argv[3]), float(sys.argv[4])
    pool = defaultdict(list)  # (kind, rounded x, rounded z) -> count in the shifted world
    for k, x, z in shifted:
        pool[(k, round(x, 1), round(z, 1))].append(1)

    def take(k, x, z):
        key = (k, round(x, 1), round(z, 1))
        if pool.get(key):
            pool[key].pop()
            return True
        return False

    stayed = moved = 0
    missing = []
    for k, x, z in base:
        if take(k, x + sx, z + sz):
            moved += 1
        elif take(k, x, z):
            stayed += 1
        else:
            missing.append((k, x, z))
    extra = [(k, x, z) for (k, x, z), left in pool.items() for _ in left]

    print(f"{len(base)} pieces: {moved} moved with their region, {stayed} stayed put")
    print(f"{len(missing)} in the base world with no match, {len(extra)} in the shifted world with no match")
    if missing:
        print("\nBase pieces with no match (by kind):")
        for kind, n in Counter(k.split('/')[0] if k.startswith(('Greenery', 'HollowMine', 'SpireCliffs')) else k for k, _, _ in missing).most_common():
            print(f"  {n:4d}  {kind}")
        print("\nFirst 40:")
        for k, x, z in missing[:40]:
            print(f"  {k:30s} {x:9.2f} {z:9.2f}")
    if extra:
        print("\nShifted pieces with no match (first 40, positions minus the shift):")
        for k, x, z in extra[:40]:
            print(f"  {k:30s} {x - sx:9.2f} {z - sz:9.2f}")


if __name__ == "__main__":
    main()
