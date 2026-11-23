#!/usr/bin/env python3
"""Compare unscaled, pixel-aligned 1006×863 home frames against the montage."""
import argparse
from pathlib import Path
import json
import hashlib
from PIL import Image, ImageChops, ImageDraw

MONTAGE = (1672, 941)
CROP = (20, 38, 1026, 901)
SIZE = (CROP[2]-CROP[0], CROP[3]-CROP[1])
# Whole-window coordinates relative to the crop. Changes are measured per
# semantic region, not combined into a single misleading similarity score.
REGIONS = {
    'header': (20, 8, 984, 74),
    'hero': (45, 113, 599, 231),
    'first_row': (34, 273, 979, 551),
    'second_row': (43, 573, 770, 829),
}

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('reference', type=Path)
    p.add_argument('actual', type=Path, help='Actual 1006×863 PNG, no scaling performed')
    p.add_argument('output', type=Path)
    p.add_argument('--platform', required=True)
    args = p.parse_args()
    ref = Image.open(args.reference).convert('RGB')
    actual = Image.open(args.actual).convert('RGB')
    if ref.size != MONTAGE or actual.size != SIZE:
        p.error(f'expected montage {MONTAGE}, actual {SIZE}; got {ref.size}, {actual.size}; refusing to rescale')
    ref = ref.crop(CROP)
    args.output.mkdir(parents=True, exist_ok=True)
    ref.save(args.output/'01-reference-home.png')
    actual.save(args.output/'02-real-home.png')
    Image.blend(ref, actual, .5).save(args.output/'03-overlay-50-percent.png')
    pair = Image.new('RGB', (2*SIZE[0]+20, SIZE[1]+28), (20, 17, 39))
    pair.paste(ref, (0, 28))
    pair.paste(actual, (SIZE[0]+20, 28))
    draw = ImageDraw.Draw(pair)
    draw.text((10, 6), 'REFERENCE 1006x863', fill='white')
    draw.text((SIZE[0]+30, 6), str(args.platform)+' REAL WINDOW 1006x863', fill='white')
    pair.save(args.output/'00-side-by-side.png')
    diff = ImageChops.difference(ref, actual)
    def mae(image):
        histogram=image.histogram()
        return round(sum((index % 256)*count for index,count in enumerate(histogram))/(3*image.width*image.height),3)
    report = {'platform': args.platform, 'no_rescale': True, 'reference_crop_half_open': CROP,
              'capture_dimensions_px': SIZE,
              'reference_sha256': hashlib.sha256(args.reference.read_bytes()).hexdigest(),
              'capture_sha256': hashlib.sha256(args.actual.read_bytes()).hexdigest(),
              'mean_absolute_rgb_error': mae(diff),
              'regions': {key: {'rect': box, 'mean_absolute_rgb_error': mae(diff.crop(box))} for key,box in REGIONS.items()},
              'caveat': 'RGB differences include real progress, native font rasterization and decoration. Inspect geometry visually; this metric is not proof of a match.'}
    (args.output/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

if __name__ == '__main__':
    main()
