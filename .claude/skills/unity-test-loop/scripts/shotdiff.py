"""Compare two capture sets shot by shot, exactly.

    shotdiff.py A B                 every channel of every pixel must match
    shotdiff.py A B --noise N1 N2   pixels that already differ between two runs of unchanged
                                    code (N1, N2) are forgiven -- nothing else is

A and B are folder names under the captures folder (Captures/current is where a capture lands),
not paths. Compares every shot the two folders share, and names any shot only one of them has.
A pixel counts as changed if any channel differs by any amount: the reference shots are held
to identity, with no tolerance, so this is too.
"""
import os
import sys
from PIL import Image, ImageChops, ImageFilter

# Where the capture fixture writes: Application.persistentDataPath on Windows.
D = os.path.join(os.environ.get("USERPROFILE", os.path.expanduser("~")),
                 "AppData", "LocalLow", "ZADZ", "BitSorter", "Captures") + os.sep

args = sys.argv[1:]
noise = None
if "--noise" in args:
    i = args.index("--noise"); noise = (args[i+1], args[i+2]); args = args[:i]
a, b = args

def names(folder):
    return {f[:-4] for f in os.listdir(D + folder) if f.endswith(".png")}

def load(look, n): return Image.open(os.path.join(D + look, n + ".png")).convert("RGB")

def changed(x, y):
    """A mask of every pixel where any channel differs at all, and the largest difference."""
    diff = ImageChops.difference(x, y)
    worst = max(hi for lo, hi in diff.getextrema())
    r, g, bb = diff.split()
    mask = ImageChops.lighter(ImageChops.lighter(r, g), bb).point(lambda v: 255 if v > 0 else 0)
    return mask, worst

shared = sorted(names(a) & names(b))
for n in sorted(names(a) ^ names(b)):
    print(f"{n:16s} only in {a if n in names(a) else b}")

worst = 0
for n in shared:
    ia, ib = load(a, n), load(b, n)
    if ia.size != ib.size:
        print(f"{n:16s} SIZE DIFFERS"); worst = 999; continue
    diff, largest = changed(ia, ib)
    excused = 0
    if noise:
        mask, _ = changed(load(noise[0], n), load(noise[1], n))
        mask = mask.filter(ImageFilter.MaxFilter(5))
        excused = sum(1 for v in mask.get_flattened_data() if v)
        diff = ImageChops.subtract(diff, mask)
    bad = sum(1 for v in diff.get_flattened_data() if v)
    worst = max(worst, bad)
    note = f"(noise excuses {excused} px)" if noise else ""
    detail = f"largest channel difference {largest:3d}  {diff.getbbox() or ''}" if bad else ""
    print(f"{n:16s} {'SAME' if bad == 0 else 'DIFFERS'}  changed pixels: {bad:7d}  {detail} {note}")
print(f"IDENTICAL, {len(shared)} shots" if worst == 0 else "DIFFERENCES FOUND")
