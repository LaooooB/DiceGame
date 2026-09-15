"""Lossless export of the supplied procedural art. Build-time only; no browser ships with the Godot game.
Usage: python Tools/extract_reference_assets.py /path/to/original/DiceGame
Requires playwright and an installed Chromium; this does not download fonts or assets.
"""
from pathlib import Path
import sys, json, base64, hashlib, os, shutil
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1]
source=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root/'Tests'/'Original'
out=root/'Assets'/'Reference';out.mkdir(parents=True,exist_ok=True)
with sync_playwright() as p:
    browser_path=os.environ.get('CHROMIUM_PATH') or shutil.which('chromium') or shutil.which('google-chrome')
    options={'headless':True}
    if browser_path: options['executable_path']=browser_path
    browser=p.chromium.launch(**options)
    page=browser.new_page(viewport={'width':1200,'height':960})
    page.set_content('<html><body></body></html>')
    for name in ['config.generated.js','math.js','simulation.js','renderer.js']:
        page.add_script_tag(content=(source/'src'/name).read_text(encoding='utf-8'))
    page.wait_for_function('typeof DiceRenderer !== "undefined"')
    def save(name,url):
        (out/name).write_bytes(base64.b64decode(url.split(',')[1]))
    sizes=[22,41,46,49,53,60,64,92,125]
    for size in sizes:
        result=page.evaluate('''s=>{const cv=document.createElement('canvas');const cell=(s+16)*3;cv.width=cell*6;cv.height=cell*6;const c=cv.getContext('2d');c.scale(3,3);for(let row=0;row<6;row++)for(let col=0;col<6;col++)DiceRenderer.dieFace(c,DiceConfig.dice[row].id,col+1,col*(s+16)+s/2+8,row*(s+16)+s/2+8,s);return cv.toDataURL();}''',size)
        save(f'dice_{size}.png',result)
    kinds=['arc','energy','blast','frost','split','bank','pulse','shield','pause','play','sound','muted','close','back','check','plus','aim','merge','reload','info']
    result=page.evaluate('''kinds=>{const cv=document.createElement('canvas');cv.width=32*3*5;cv.height=32*3*4;const c=cv.getContext('2d');c.scale(3,3);kinds.forEach((k,i)=>DiceRenderer.icon(c,k,(i%5)*32+16,Math.floor(i/5)*32+16,24,'#FFFFFF'));return cv.toDataURL();}''',kinds)
    save('icons.png',result)
    result=page.evaluate('''()=>{const cv=document.createElement('canvas');cv.width=864;cv.height=1728;const c=cv.getContext('2d');c.scale(2,2);const r=new DiceRenderer.Renderer(cv);r.background();return cv.toDataURL();}''')
    save('background.png',result)
    for color in ['#72EAC8','#FFAD76','#CBA7FF','#87D6FF','#F8DE87','#FF97B8']:
        result=page.evaluate('''color=>{const cv=document.createElement('canvas');cv.width=48;cv.height=48;const c=cv.getContext('2d');const g=c.createRadialGradient(24,24,0,24,24,24);g.addColorStop(0,color);g.addColorStop(.14,color+'CC');g.addColorStop(.42,color+'33');g.addColorStop(1,color+'00');c.fillStyle=g;c.fillRect(0,0,48,48);return cv.toDataURL();}''',color)
        save('glow_'+color[1:]+'.png',result)
    manifest={'source':'Original DiceGame/src/renderer.js','source_sha256':hashlib.sha256((source/'src'/'renderer.js').read_bytes()).hexdigest(),'dice_sizes':sizes,'dice_type_rows':['pulse','blast','arc','frost','split','bank'],'dice_columns':'pips 1 through 6','atlas_scale':3,'padding':8,'icon_base_size':24,'icon_cell_size':32,'icons':kinds}
    (out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    browser.close()
print('Extracted original dice, icon, glow and background textures.')
