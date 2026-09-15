"""Local package/integrity checks. This is NOT a C# compiler or Godot runtime test.
Optional original folder argument verifies the frozen three legacy data files byte-for-byte.
Python 3 standard library only.
"""
from pathlib import Path
import sys, json, hashlib, struct, math, re, xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
checks=[]
def check(name,condition,detail=''):
    checks.append({'name':name,'status':'passed' if condition else 'failed','detail':detail})
    print(('PASS ' if condition else 'FAIL ')+name)
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def png_size(path):
    b=path.read_bytes()
    if b[:8]!=b'\x89PNG\r\n\x1a\n':raise ValueError(str(path)+' is not PNG')
    return struct.unpack('>II',b[16:24])

def main():
    for relative in ['Data/game.json','Data/dice.json','Data/upgrades.json','Data/dice_skills.json','Assets/Reference/manifest.json','Tests/Fixtures/fixture_manifest.json']:
        value=json.loads((ROOT/relative).read_text(encoding='utf-8'));check('JSON '+relative,value is not None)
    project=ET.parse(ROOT/'DiceGame.csproj').getroot()
    tests=ET.parse(ROOT/'Tests/DiceGame.Tests.csproj').getroot()
    check('Godot SDK pinned to 4.6.0',project.attrib['Sdk']=='Godot.NET.Sdk/4.6.0')
    check('Both projects target .NET 8',all(p.find('./PropertyGroup/TargetFramework').text=='net8.0' for p in [project,tests]))
    check('Tests excluded from game compilation',project.find("./ItemGroup/Compile[@Remove='Tests/**/*.cs']") is not None)
    settings=(ROOT/'project.godot').read_text(encoding='utf-8')
    check('1080p default viewport','viewport_width=1920' in settings and 'viewport_height=1080' in settings)
    game=json.loads((ROOT/'Data/game.json').read_text(encoding='utf-8'))
    skills=json.loads((ROOT/'Data/dice_skills.json').read_text(encoding='utf-8'))
    check('16-slot 4-column board',game['board']['slots']==16 and game['board']['columns']==4)
    check('Exactly six distinct deck entries required',game['rules']['minDeck']==6 and game['rules']['maxDeck']==6 and len(set(game['rules']['starterDeck']))==6)
    check('Direct damage budget matches sixteen-slot capacity',abs(game['rules']['damageScale']-.5)<1e-12)
    check('Opening six-die identity and paced economy',game['rules']['startingDicePattern']==[0,1,2,3,4,5] and game['rules']['startEnergy']==16 and game['rules']['summonCost']==10 and game['rules']['passiveEnergy']<=.25 and game['waves']['killEnergy']==0)
    check('Every die has A/B and C/D',{s['type'] for s in skills}=={d['id'] for d in json.loads((ROOT/'Data/dice.json').read_text(encoding='utf-8'))} and all([x['key'] for x in s['level3']]==['A','B'] and [x['key'] for x in s['level6']]==['C','D'] for s in skills))
    check('Skill regression project and capacity analysis present',all((ROOT/p).is_file() for p in ['Tests/Skills/Skills.Tests.csproj','Tools/analyze_board_capacity.py']))
    check('Frozen original balance copied for honest reference tests',all((ROOT/'Tests/LegacyBalanceData'/p).is_file() for p in ['game.json','dice.json','upgrades.json','campaign.json']))
    dice=json.loads((ROOT/'Data/dice.json').read_text(encoding='utf-8'))
    check('Content enabled without changing six-card random deck',game['rules'].get('enableDiceContent') is True and game['rules']['starterDeck']==['pulse','blast','arc','frost','split','bank'])
    check('Every die has a supported rarity',all(d.get('rarity') in ['common','rare','epic','legendary','mythic'] for d in dice))
    check('Expanded faces have authored native glyphs',all(d.get('glyphPath') for d in dice if d['id'] not in ['pulse','blast','arc','frost','split','bank']))
    check('Content regression and player catalogue present',all((ROOT/p).is_file() for p in ['Tests/Content/Content.Tests.csproj','Tests/Content/Program.cs','Docs/DICE_CONTENT.md']))
    check('Content regression participates in CI','Tests/Content/Content.Tests.csproj' in (ROOT/'.github/workflows/verify.yml').read_text(encoding='utf-8'))
    campaign=json.loads((ROOT/'Data/campaign.json').read_text(encoding='utf-8'))
    check('Fresh campaign has enough unlocked dice to launch',len(set(campaign['initialDice']))>=game['rules']['minDeck'])
    check('Eight finite three-phase regions',len(campaign['regions'])==8 and all(len(r['phases'])==3 for r in campaign['regions']))
    check('Fully node-authored UI pages',all((ROOT/n).is_file() for n in ['Scenes/UI/CampaignUi.tscn','Scenes/UI/TownPage.tscn','Scenes/UI/ExpeditionPage.tscn','Scenes/UI/DeckPage.tscn','Scenes/UI/BattlePage.tscn','Scenes/UI/SettlementPage.tscn','Scenes/UI/SettingsPage.tscn','Scenes/UI/HelpPage.tscn','Scenes/UI/OverlayLayer.tscn','Scenes/UI/BattleSlot.tscn']))
    check('Campaign independent test project',(ROOT/'Tests/Campaign/Campaign.Tests.csproj').is_file())
    check('Main scene exists',(ROOT/'Scenes/Main.tscn').is_file() and 'res://Scenes/Main.tscn' in settings)
    check('Main C# entry exists',(ROOT/'Scripts/App/GameRoot.cs').is_file() and 'res://Scripts/App/GameRoot.cs' in (ROOT/'Scenes/Main.tscn').read_text())
    check('Mouse-only platform switches','pointing/emulate_touch_from_mouse=false' in settings and 'pointing/emulate_mouse_from_touch=false' in settings)
    preset=(ROOT/'export_presets.cfg').read_text()
    check('Desktop-only export preset','platform="Windows Desktop"' in preset and not any(x in preset for x in ['platform="Web"','platform="Android"','platform="iOS"']))
    check('Test and tool files excluded from exports',all(x in preset for x in ['Tests/**/*','Tools/**/*','Docs/**/*','Artifacts/**/*','Data/*.json']))
    check('Non-runtime folders ignored by Godot',all((ROOT/x/'.gdignore').is_file() for x in ['Tests','Tools','Docs','Artifacts']))
    shader=(ROOT/'Shaders/RoundedTexture.gdshader').read_text()
    check('No duplicate texture multiplication in round mask','vec4 sample_color = COLOR;' in shader and 'texture(TEXTURE, UV) * COLOR' not in shader)
    check('Background image size',png_size(ROOT/'Assets/Reference/background.png')==(864,1728))
    check('Icon atlas size',png_size(ROOT/'Assets/Reference/icons.png')==(480,384))
    atlas=json.loads((ROOT/'Assets/Reference/manifest.json').read_text())
    for size in atlas['dice_sizes']:
        side=(size+16)*3*6
        check('Dice atlas '+str(size),png_size(ROOT/f'Assets/Reference/dice_{size}.png')==(side,side))
    for color in ['72EAC8','FFAD76','CBA7FF','87D6FF','F8DE87','FF97B8']:
        check('Glow atlas '+color,png_size(ROOT/f'Assets/Reference/glow_{color}.png')==(48,48))
    fixtures=ROOT/'Tests/Fixtures'
    manifest=json.loads((fixtures/'fixture_manifest.json').read_text())
    for name,expected in manifest['sourceFiles'].items():
        check('Original source hash '+name,sha(ROOT/'Tests/Original/src'/name)==expected)
    check('Art source hash',sha(ROOT/'Tests/Original/src/renderer.js')==atlas['source_sha256'])
    cases=json.loads((fixtures/'simulation_cases.json').read_text())
    check('17 reference cases / 179 checkpoints',len(cases)==17 and sum(len(c['steps']) for c in cases)==179)
    check('72 stat profiles',len(json.loads((fixtures/'stats.json').read_text()))==72)
    audio=json.loads((fixtures/'audio_index.json').read_text());raw=(fixtures/'audio_reference.f32').read_bytes()
    check('84 audio profiles / 670677 samples',len(audio)==84 and len(raw)==670677*4 and sum(x['count'] for x in audio)==670677)
    check('All reference audio samples finite',all(math.isfinite(x[0]) for x in struct.iter_unpack('<f',raw)))
    expected_names=['menu','deck','battle_aim','upgrade','pause','dice_info','help','game_over','effects']
    check('Nine original reference screenshots',all(png_size(ROOT/'Tests/ReferenceScreenshots'/(n+'.png'))==(864,1728) for n in expected_names))
    check('No font files redistributed',not any(p.suffix.lower() in ['.ttf','.otf','.woff','.woff2','.ttc'] for p in ROOT.rglob('*') if p.is_file()))
    check('No placeholder implementation exceptions',not any('NotImplementedException' in p.read_text() for p in (ROOT/'Scripts').rglob('*.cs')))
    runtime='\n'.join(p.read_text() for p in (ROOT/'Scripts').rglob('*.cs'))
    player_ui=(ROOT/'Scripts/UI/CampaignUi.cs').read_text(encoding='utf-8')
    check('Player UI never creates controls at runtime',not any(x in player_ui for x in ['new Button','new Label','new PanelContainer','new ConfirmationDialog','Instantiate<','GD.Load<PackedScene>']))
    check('No JavaScript engine or embedded browser dependency',not any(x in runtime for x in ['using Jint','using Microsoft.Web.WebView2','using CefSharp','using Microsoft.ClearScript']))
    if len(sys.argv)>1:
        original=Path(sys.argv[1])
        for name in ['game.json','dice.json','upgrades.json']:
            check('Unchanged original data '+name,(ROOT/'Tests/LegacyBalanceData'/name).read_bytes()==(original/'data'/name).read_bytes())
    report={'kind':'package_integrity_only','not_a_compiler_or_runtime_test':True,'passed':sum(x['status']=='passed' for x in checks),'failed':sum(x['status']=='failed' for x in checks),'checks':checks}
    (ROOT/'Artifacts').mkdir(exist_ok=True)
    (ROOT/'Artifacts/static-check.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(f"{report['passed']} integrity checks passed; {report['failed']} failed. C# and Godot were NOT executed.")
    return 1 if report['failed'] else 0

if __name__=='__main__':raise SystemExit(main())
