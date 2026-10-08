#!/usr/bin/env python3
"""Build MSIX logo sizes from src/FrameIt/Assets/FrameIt-icon-source.png.

Square tiles keep the full brand graphic. Wide tiles and the splash screen
place that graphic on the outer wall color. Unplated taskbar icons knock out
only the outer wall, so the teal frame stays and the taskbar does not draw a plate.
"""

from __future__ import annotations

import sys
from collections import deque
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "src" / "FrameIt" / "Assets" / "FrameIt-icon-source.png"
OUT = Path(__file__).resolve().parent / "Assets"

SQUARE_SCALES = {
    "Square44x44Logo": {100: 44, 125: 55, 150: 66, 200: 88, 400: 176},
    "Square71x71Logo": {100: 71, 125: 89, 150: 107, 200: 142, 400: 284},
    "Square150x150Logo": {100: 150, 125: 188, 150: 225, 200: 300, 400: 600},
    "StoreLogo": {100: 50, 125: 63, 150: 75, 200: 100, 400: 200},
}

WIDE_SCALES = {
    "Wide310x150Logo": {100: (310, 150), 200: (620, 300), 400: (1240, 620)},
    "SplashScreen": {100: (620, 300), 200: (1240, 600), 400: (2480, 1200)},
}

TARGET_SIZES = (16, 24, 32, 48, 256)


def is_outer_wall(pixel: tuple[int, int, int, int]) -> bool:
    red, green, blue, _alpha = pixel
    # Light blue wall around the outer frame. The gold frame and the dark teal
    # border fail this test, so a flood from the edges stops there.
    return blue > 170 and green > 150 and red > 90 and (blue - red) > 15 and max(red, green, blue) > 180


def knock_out_outer_wall(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    width, height = image.size
    pixels = image.load()
    seen = bytearray(width * height)
    queue: deque[tuple[int, int]] = deque()

    def push(x: int, y: int) -> None:
        index = y * width + x
        if seen[index]:
            return
        seen[index] = 1
        if is_outer_wall(pixels[x, y]):
            queue.append((x, y))

    for x in range(width):
        push(x, 0)
        push(x, height - 1)
    for y in range(height):
        push(0, y)
        push(width - 1, y)

    while queue:
        x, y = queue.popleft()
        red, green, blue, _alpha = pixels[x, y]
        pixels[x, y] = (red, green, blue, 0)
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < width and 0 <= ny < height:
                push(nx, ny)

    # The wall color feathers into the frame shadow. Clear only those fringe
    # pixels that already touch transparency, so the inner mat stays put.
    for _ in range(4):
        fringe: list[tuple[int, int]] = []
        for y in range(height):
            for x in range(width):
                if pixels[x, y][3] == 0 or not is_outer_wall(pixels[x, y]):
                    continue
                if any(
                    0 <= nx < width and 0 <= ny < height and pixels[nx, ny][3] == 0
                    for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1))
                ):
                    fringe.append((x, y))
        for x, y in fringe:
            red, green, blue, _alpha = pixels[x, y]
            pixels[x, y] = (red, green, blue, 0)

    return image


def fit_square(source: Image.Image, size: int) -> Image.Image:
    return source.resize((size, size), Image.Resampling.LANCZOS)


def fit_wide(source: Image.Image, size: tuple[int, int], background: tuple[int, int, int, int]) -> Image.Image:
    width, height = size
    canvas = Image.new("RGBA", size, background)
    margin = max(4, height // 12)
    side = height - (margin * 2)
    logo = source.resize((side, side), Image.Resampling.LANCZOS)
    canvas.paste(logo, ((width - side) // 2, margin), logo)
    return canvas


def save(image: Image.Image, name: str) -> None:
    path = OUT / name
    image.save(path, format="PNG", optimize=True)
    print(path.relative_to(ROOT))


def main() -> int:
    if not SOURCE.is_file():
        print(f"Missing brand art: {SOURCE}", file=sys.stderr)
        return 1

    source = Image.open(SOURCE).convert("RGBA")
    if source.size != (1024, 1024):
        print(f"Expected 1024x1024 brand art, got {source.size}", file=sys.stderr)
        return 1

    OUT.mkdir(parents=True, exist_ok=True)
    wall = source.getpixel((2, 2))
    unplated = knock_out_outer_wall(source)

    for stem, scales in SQUARE_SCALES.items():
        base = fit_square(source, scales[100])
        save(base, f"{stem}.png")
        for scale, pixels in scales.items():
            save(fit_square(source, pixels), f"{stem}.scale-{scale}.png")

    for stem, scales in WIDE_SCALES.items():
        base = fit_wide(source, scales[100], wall)
        save(base, f"{stem}.png")
        for scale, size in scales.items():
            save(fit_wide(source, size, wall), f"{stem}.scale-{scale}.png")

    for pixels in TARGET_SIZES:
        save(fit_square(source, pixels), f"Square44x44Logo.targetsize-{pixels}.png")
        save(fit_square(unplated, pixels), f"Square44x44Logo.targetsize-{pixels}_altform-unplated.png")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
