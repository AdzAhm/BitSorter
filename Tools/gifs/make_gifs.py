"""Make the README's GIFs from the frames the ReadmeGifs fixture recorded.

    make_gifs.py [CAPTURES] [--only NAME ...] [--out DIR] [--fps N] [--width N] [--max-mb N]

CAPTURES is the Captures folder the fixture writes to (Application.persistentDataPath/Captures on
Windows: %USERPROFILE%/AppData/LocalLow/ZADZ/BitSorter/Captures, the default), or the gifs folder
inside it. Each folder under it, Captures/gifs/<name>, holds f0000.png, f0001.png, ... recorded at
20 frames a second by Assets/Tests/PlayMode/ReadmeGifs.cs; each becomes Docs/gifs/<name>.gif at
the repository's root.

A GIF is 800 pixels wide and 10 frames a second (--width, --fps; 12 works), made from every
second frame or so of the recording -- the frames kept are spread evenly through it, and each
lasts as long as the recorded frames up to the next kept one, so a GIF takes as long to play as
its scenario took to record. One palette is made for all of it, from frames sampled right through,
and no dithering is used: the workbench is flat colour, and a palette shared by every frame lets
the encoder write only what changed between two of them.

A GIF over --max-mb (2 by default) is made again smaller, a step at a time -- narrower first,
then fewer frames a second -- until it fits, and the size it came to is printed with the width and
frame rate it took. Needs Pillow.
"""
import argparse
import os
import sys
from pathlib import Path

from PIL import Image

# The rate the fixture records at (ReadmeGifs.FramesPerSecond).
SOURCE_FPS = 20

# How a GIF that is too big is made smaller: the width and the frame rate as shares of what was asked for,
# least harm first. A step is only taken when the one before it was over the limit.
LADDER = [(1.0, 1.0), (0.9, 1.0), (0.8, 1.0), (0.8, 0.8), (0.7, 0.8), (0.6, 0.8), (0.6, 0.6), (0.5, 0.6)]

# How many frames the palette is made from.
PALETTE_SAMPLES = 24

REPO = Path(__file__).resolve().parents[2]


def default_captures():
    home = os.environ.get("USERPROFILE", os.path.expanduser("~"))
    return Path(home) / "AppData" / "LocalLow" / "ZADZ" / "BitSorter" / "Captures"


def frames_of(folder):
    """The folder's frames in order, as RGB images."""
    return [Image.open(p).convert("RGB") for p in sorted(folder.glob("f*.png"))]


def resized(images, width):
    """The frames at <width> pixels across, keeping their aspect."""
    w, h = images[0].size
    if width >= w:
        return images
    height = max(2, round(h * width / w))
    return [im.resize((width, height), Image.LANCZOS) for im in images]


def spread(count, source_fps, fps):
    """Which recorded frames are kept, evenly through them, and how long each is shown (milliseconds).

    A frame is shown from where it is to where the next kept one is, so the total is the recording's.
    """
    kept = int(count * fps / source_fps)
    kept = max(1, kept)
    at = [min(count - 1, int(i * source_fps / fps)) for i in range(kept)]
    ends = at[1:] + [count]
    return at, [max(20, round((end - start) * 1000 / source_fps)) for start, end in zip(at, ends)]


def palette_of(images):
    """One 256-colour palette for all of them, from frames sampled through them."""
    step = max(1, len(images) // PALETTE_SAMPLES)
    sample = images[::step]
    w, h = sample[0].size
    sheet = Image.new("RGB", (w, h * len(sample)))
    for i, im in enumerate(sample):
        sheet.paste(im, (0, i * h))
    return sheet.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)


def write(images, durations, path):
    palette = palette_of(images)
    frames = [im.quantize(palette=palette, dither=Image.Dither.NONE) for im in images]
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=False)
    return path.stat().st_size


def make(name, folder, out, width, fps, limit):
    recorded = frames_of(folder)
    if not recorded:
        print(f"{name}: no frames in {folder}, skipped")
        return False

    path = out / f"{name}.gif"
    size = 0
    for share_w, share_f in LADDER:
        w = max(160, int(round(width * share_w / 2)) * 2)
        f = max(4.0, fps * share_f)
        at, durations = spread(len(recorded), SOURCE_FPS, f)
        images = resized([recorded[i] for i in at], w)
        size = write(images, durations, path)
        if size <= limit:
            break

    mark = "" if size <= limit else f"  (over the {limit / 1e6:.1f} MB limit at the smallest size tried)"
    h = images[0].size[1]
    print(f"{name}.gif  {size / 1e6:.2f} MB  {images[0].size[0]}x{h}  {f:g} fps  "
          f"{len(images)} frames from {len(recorded)} recorded ({len(recorded) / SOURCE_FPS:.1f} s){mark}")
    return True


def main():
    parser = argparse.ArgumentParser(description="Make the README's GIFs from the frames ReadmeGifs recorded.")
    parser.add_argument("captures", nargs="?", type=Path, default=default_captures(),
                        help="the Captures folder, or the gifs folder in it (default: the game's persistent data path)")
    parser.add_argument("--only", nargs="+", metavar="NAME", help="make just these")
    parser.add_argument("--out", type=Path, default=REPO / "Docs" / "gifs", help="where the GIFs go (default: Docs/gifs)")
    parser.add_argument("--width", type=int, default=800, help="how wide a GIF is (default 800)")
    parser.add_argument("--fps", type=float, default=10.0, help="frames a second (default 10; 12 works)")
    parser.add_argument("--max-mb", type=float, default=2.0, help="the size a GIF is made smaller to fit (default 2)")
    args = parser.parse_args()

    gifs = args.captures / "gifs" if (args.captures / "gifs").is_dir() else args.captures
    if not gifs.is_dir():
        print(f"no folder {gifs}: record the GIFs first (Assets/Tests/PlayMode/ReadmeGifs.cs)")
        return 1

    names = sorted(p.name for p in gifs.iterdir() if p.is_dir())
    if args.only:
        missing = [n for n in args.only if n not in names]
        for n in missing:
            print(f"{n}: nothing recorded in {gifs}")
        names = [n for n in names if n in args.only]
    if not names:
        print(f"nothing to make in {gifs}")
        return 1

    args.out.mkdir(parents=True, exist_ok=True)
    made = [make(n, gifs / n, args.out, args.width, args.fps, int(args.max_mb * 1e6)) for n in names]
    return 0 if any(made) else 1


if __name__ == "__main__":
    sys.exit(main())
