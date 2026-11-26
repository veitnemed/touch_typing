#!/usr/bin/env python3
"""Refresh the shipped Linux PNG and Windows ICO from the brand SVG.

Requires Inkscape 1.4 and Python/Pillow. The app/package does not need either.
"""
from io import BytesIO
from pathlib import Path
from tempfile import TemporaryDirectory
import struct
import subprocess

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def main():
    # One supersampled render. Strip editor metadata and scale each size from
    # the original rather than repeatedly resizing a smaller representation.
    with TemporaryDirectory() as folder:
        png = Path(folder) / 'master.png'
        subprocess.run(['inkscape', str(HERE / 'touch-typing.svg'), '-w', '1024',
                        '-h', '1024', '-o', str(png)], check=True)
        with Image.open(png) as image:
            rgba = image.convert('RGBA')
        frames = []
        for size in SIZES:
            out = BytesIO()
            rgba.resize((size, size), Image.Resampling.LANCZOS).save(out, 'PNG', optimize=True)
            frames.append(out.getvalue())
    (ROOT / 'linux/icon.png').write_bytes(frames[-1])
    directory = []
    offset = 6 + 16 * len(frames)
    for size, frame in zip(SIZES, frames):
        directory.append(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32,
                                     len(frame), offset))
        offset += len(frame)
    (HERE / 'touch-typing.ico').write_bytes(
        struct.pack('<HHH', 0, 1, len(frames)) + b''.join(directory + frames))


if __name__ == '__main__':
    main()
