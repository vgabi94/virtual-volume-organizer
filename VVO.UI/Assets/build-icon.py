"""Regenerate vvo2.ico from vvo2.svg. Run after editing the SVG.

    python build-icon.py

Requires node (the @resvg/resvg-js rasterizer is installed on first run) and
Pillow (pip install Pillow).
"""

import io
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow is required: pip install Pillow")

HERE = Path(__file__).parent
SOURCE = HERE / "vvo2.svg"
TARGET = HERE / "vvo2.ico"

# Sizes Windows Explorer and the taskbar pick from across 100–300% DPI.
# Anything absent gets stretched by the shell instead. 256 is PNG-compressed
# and is what extra-large / jumbo view uses; the rest stay as 32-bit DIBs.
SIZES = (16, 20, 24, 30, 32, 36, 40, 48, 64, 96, 128, 256)

# Rasterize above the target and reduce, so strokes thinner than a pixel land as
# partial coverage rather than dropping out.
SUPERSAMPLE = 4


def _gamma_lut(to_linear: bool) -> list[int]:
    def convert(i: int) -> int:
        v = i / 255.0
        if to_linear:
            v = v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
        else:
            v = v * 12.92 if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055
        return round(v * 255)

    return [convert(i) for i in range(256)]


TO_LINEAR = _gamma_lut(True)
TO_SRGB = _gamma_lut(False)


def _apply(img: Image.Image, lut: list[int]) -> Image.Image:
    r, g, b, a = img.split()
    return Image.merge("RGBA", [r.point(lut), g.point(lut), b.point(lut), a])


def rasterize(size: int, scratch: Path) -> Image.Image:
    png = scratch / f"{size}.png"
    subprocess.run(
        ["node", str(HERE / "render.js"), str(SOURCE), str(size * SUPERSAMPLE), str(png)],
        check=True,
        shell=sys.platform == "win32",
    )
    supersampled = Image.open(png).convert("RGBA")
    # Averaging in linear light; blending sRGB values directly darkens edges.
    reduced = _apply(supersampled, TO_LINEAR).resize((size, size), Image.LANCZOS)
    return _apply(reduced, TO_SRGB)


def to_dib(img: Image.Image) -> bytes:
    """Bottom-up 32-bit DIB plus the 1-bit AND mask the format still mandates."""
    width, height = img.size
    pixels = img.load()
    rows = bytearray()
    for y in reversed(range(height)):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            rows += bytes((b, g, r, a))

    stride = ((width + 31) // 32) * 4
    mask = bytearray()
    for y in reversed(range(height)):
        row = bytearray(stride)
        for x in range(width):
            if pixels[x, y][3] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        mask += row

    header = struct.pack(
        "<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, len(rows) + len(mask), 0, 0, 0, 0
    )
    return header + bytes(rows) + bytes(mask)


def to_png(img: Image.Image) -> bytes:
    buffer = io.BytesIO()
    img.save(buffer, "PNG", optimize=True)
    return buffer.getvalue()


def pack(frames: list[tuple[int, Image.Image]]) -> bytes:
    # 256 goes in compressed; the smaller entries stay raw so shell code paths
    # that predate PNG-in-ICO can still read them.
    blobs = [to_png(img) if size >= 256 else to_dib(img) for size, img in frames]

    directory = b""
    offset = 6 + 16 * len(frames)
    for (size, _), blob in zip(frames, blobs):
        directory += struct.pack(
            "<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(blob), offset
        )
        offset += len(blob)

    return struct.pack("<HHH", 0, 1, len(frames)) + directory + b"".join(blobs)


def ensure_rasterizer() -> None:
    if (HERE / "node_modules" / "@resvg").is_dir():
        return
    print("installing @resvg/resvg-js...")
    subprocess.run(["npm", "install"], cwd=HERE, check=True, shell=sys.platform == "win32")


def main() -> None:
    ensure_rasterizer()
    with tempfile.TemporaryDirectory() as tmp:
        frames = [(size, rasterize(size, Path(tmp))) for size in sorted(SIZES)]
    TARGET.write_bytes(pack(frames))
    print(f"{TARGET.name}: {len(SIZES)} sizes, {TARGET.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
