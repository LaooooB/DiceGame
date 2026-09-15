"""Capture the original renderer at the same fixed states as Godot's --capture-reference mode.
These are ORIGINAL-BROWSER screenshots, never Godot screenshots.
Requires Python, Playwright and an installed Chromium browser. Runtime game does not use these tools.
"""
from pathlib import Path
import os,sys,json,base64,shutil
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1]
source=Path(sys.argv[1]) if len(sys.argv)>1 else root/'Tests'/'Original'
output=root/'Tests'/'ReferenceScreenshots';output.mkdir(parents=True,exist_ok=True)
with sync_playwright() as p:
    browser_path=os.environ.get('CHROMIUM_PATH') or shutil.which('chromium') or shutil.which('google-chrome')
    options={'headless':True}
    if browser_path: options['executable_path']=browser_path
    browser=p.chromium.launch(**options)
    page=browser.new_page(viewport={'width':900,'height':1800})
    page.set_content('<body style="margin:0"><canvas id="game"></canvas></body>')
    for name in ['config.generated.js','math.js','simulation.js','renderer.js']:
        page.add_script_tag(content=(source/'src'/name).read_text(encoding='utf-8'))
    page.evaluate('''fixture=>{
      window.fixture=fixture; const cv=document.getElementById('game');
      window.renderer=new DiceRenderer.Renderer(cv,{createCanvas:(w,h)=>{const c=document.createElement('canvas');c.width=w;c.height=h;return c;}});
      renderer.resize(432,864,2);renderer.t=1.25;
      window.app={scene:'menu',settings:{sound:true,music:false,reduceMotion:false},deck:DiceConfig.dice.map(d=>d.id),editingDeck:[],sim:null,resumeData:null,pointer:null,selectedSlot:-1,meta:{bestWave:0,bestScore:0},toast:null,aimAngle:-Math.PI/2,helpReturn:'menu'};
      window.resetBattle=()=>{app.sim=DiceSim.Simulation.restore(fixture.battle);app.scene='play';app.pointer=null;renderer.t=1.25;};
    }''',json.loads((root/'Tests'/'Fixtures'/'visual_states.json').read_text()))
    def capture(name,setup=''):
        page.evaluate(setup or '()=>{}')
        url=page.evaluate('()=>{renderer.render(app);return document.getElementById("game").toDataURL();}')
        (output/(name+'.png')).write_bytes(base64.b64decode(url.split(',')[1]))
    capture('menu')
    capture('deck',"()=>{app.scene='deck';app.editingDeck=app.deck.slice();}")
    capture('battle_aim',"()=>{resetBattle();app.pointer={mode:'aim',x:305,y:222};app.aimAngle=app.sim.clampAim(Math.atan2(Math.min(222-DiceConfig.game.arena.launchY,-24),305-216));}")
    capture('upgrade',"()=>{app.pointer=null;app.sim.offerUpgrades();app.scene='upgrade';app.meta.bestWave=3;}")
    capture('pause',"()=>{resetBattle();app.scene='paused';}")
    capture('dice_info',"()=>{app.scene='die';app.selectedSlot=0;}")
    capture('help',"()=>{app.scene='help';app.helpReturn='play';}")
    capture('game_over',"()=>{app.scene='over';app.sim.over=true;}")
    capture('effects',"()=>{resetBattle();for(const e of fixture.effects)renderer.onEvent(e);renderer.update(1/60);renderer.t=1.25;}")
    browser.close()
(output/'README.txt').write_text('These 9 images were rendered by the ORIGINAL JavaScript/Chromium implementation at fixed states. They are NOT Godot runtime evidence. Compare against Artifacts/Native after running the native capture harness.\n',encoding='utf-8')
print('Captured 9 original-browser reference screens.')
