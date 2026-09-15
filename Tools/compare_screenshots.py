"""Compare native captures to original browser captures without inventing a visual pass.
Requires: Python 3, Pillow, NumPy. No screenshots are silently scaled, aligned, or masked.
A zero-error self-comparison can test this script, but says nothing about the C# renderer.
"""
from pathlib import Path
import argparse, json
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
NAMES = ['menu','deck','battle_aim','upgrade','pause','dice_info','help','game_over','effects']

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', type=Path, default=ROOT/'Tests'/'ReferenceScreenshots')
    parser.add_argument('--native', type=Path, default=ROOT/'Artifacts'/'Native')
    parser.add_argument('--output', type=Path, default=ROOT/'Artifacts'/'VisualComparison')
    args = parser.parse_args()
    missing = [str(folder/(name+'.png')) for folder in (args.reference,args.native)
               for name in NAMES if not (folder/(name+'.png')).is_file()]
    if missing:
        print('Missing screenshots. No comparison was performed:\n'+'\n'.join(missing))
        return 2
    args.output.mkdir(parents=True,exist_ok=True)
    rows=[]
    for name in NAMES:
        with Image.open(args.reference/(name+'.png')) as image: expected=image.convert('RGBA')
        with Image.open(args.native/(name+'.png')) as image: actual=image.convert('RGBA')
        if expected.size != actual.size:
            raise ValueError(f'{name}: size mismatch {expected.size} vs {actual.size}; not resized automatically.')
        a=np.asarray(expected,dtype=np.float64); b=np.asarray(actual,dtype=np.float64)
        error=np.abs(a-b); rgb=error[:,:,:3]
        rows.append({'name':name,'width':expected.width,'height':expected.height,
                     'mean_absolute_rgb_0_to_255':float(rgb.mean()),
                     'max_absolute_rgb_0_to_255':float(rgb.max()),
                     'percent_pixels_rgb_error_above_8':float(np.mean(np.max(rgb,axis=2)>8)*100),
                     'percent_pixels_exact_rgba':float(np.mean(np.max(error,axis=2)==0)*100),
                     'mean_absolute_alpha_0_to_255':float(error[:,:,3].mean())})
        heat=np.clip(rgb*4,0,255).astype(np.uint8)
        Image.fromarray(heat).save(args.output/(name+'_difference_x4.png'))
        Image.blend(expected,actual,.5).save(args.output/(name+'_overlay.png'))
        side=Image.new('RGBA',(expected.width*2,expected.height))
        side.paste(expected,(0,0));side.paste(actual,(expected.width,0));side.save(args.output/(name+'_side_by_side.png'))
    report={'status':'comparison_generated_requires_review',
            'reference':str(args.reference.resolve()),'native':str(args.native.resolve()),
            'notes':'No visual acceptance threshold was imposed. Review layout, blending, text, particles and animation in addition to these fixed frames. Use the same OS/fonts for original and native capture.',
            'screens':rows}
    (args.output/'comparison.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    for row in rows: print(f"{row['name']:12} mean RGB error={row['mean_absolute_rgb_0_to_255']:.4f}; changed >8={row['percent_pixels_rgb_error_above_8']:.3f}%")
    print('Generated comparison artifacts; this does NOT automatically declare visual parity.')
    return 0

if __name__=='__main__':
    raise SystemExit(main())
