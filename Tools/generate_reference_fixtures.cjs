#!/usr/bin/env node
'use strict';
// Run against an extracted ORIGINAL DiceGame-project.zip, never the new C# implementation.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const original=path.resolve(process.argv[2]||path.join(__dirname,'../Tests/Original'));
const output=path.resolve(__dirname,'../Tests/Fixtures');fs.mkdirSync(output,{recursive:true});
const Config=require(path.join(original,'src/config.generated.js'));
const {Simulation}=require(path.join(original,'src/simulation.js'));
const M=require(path.join(original,'src/math.js'));
const {Renderer}=require(path.join(original,'src/renderer.js'));
const {synthesize}=require(path.join(original,'src/audio.js'));
const clone=x=>JSON.parse(JSON.stringify(x));
const write=(name,v)=>fs.writeFileSync(path.join(output,name),JSON.stringify(v,null,2)+'\n');
const cases=[];
function makeCase(name,seed=24137,deck=Config.dice.map(d=>d.id),setup=null){
  let s=new Simulation({seed,deck});let initial=null;
  if(setup){setup(s);s.grid.rebuild(s.enemies);initial=s.exportSave();}
  const c={name,seed,deck,initial,expectedInitial:s.exportSave(),steps:[]};s.takeEvents();
  function action(op,...args){
    let result=null;
    if(op==='step'){for(let i=0;i<args[0];i++)s.step(args[1]??1/120);}
    else if(op==='restore'){s=Simulation.restore(s.exportSave());}
    else result=s[op](...args)??null;
    c.steps.push({op,args,result:clone(result),events:s.takeEvents(),expected:s.exportSave()});return api;
  }
  const api={action,state:()=>s,done:()=>cases.push(c)};return api;
}
makeCase('opening-fire-merge-keeps-old-projectiles').action('fire',-1.32).action('step',18).action('merge',0,1).action('step',42).action('summon').action('fire',-2.22).action('step',420).action('restore').action('step',60).done();
makeCase('same-type-same-pips-only',4521).action('merge',0,2).action('move',0,4).action('merge',4,1).action('merge',1,2).action('recycle',2).action('summon').action('step',60).done();
makeCase('max-pips-and-full-board',54321,undefined,s=>{
 s.board=Array.from({length:8},(_,i)=>s.makeDie(i<2?'pulse':Config.dice[i%6].id,i<2?6:1));s.energy=999;
}).action('summon').action('merge',0,1).action('move',2,3).action('recycle',7).action('summon').action('fire',-1.1).action('step',240).done();
for(const type of Config.dice){
 const a=makeCase('effect-'+type.id,9137,[type.id],s=>{
  s.board.fill(null);s.board[0]=s.makeDie(type.id,4);s.board[1]=s.makeDie(type.id,4);s.energy=200;
  s.enemies=[];for(let i=0;i<8;i++)s.addEnemy({x:66+i%7*50,y:205+Math.floor(i/7)*55,w:39,h:39,hp:125,speed:0,kind:i===2?'volatile':i===4?'armored':'normal',wave:1});
 });
 a.action('fire',-1.17).action('step',90).action('step',150).action('merge',0,1).action('step',50).action('fire',-1.93).action('step',180).action('restore').action('step',80).done();
}
makeCase('upgrade-pauses-and-resume',71253,undefined,s=>{s.health=5;}).action('offerUpgrades').action('step',120).action('fire',-1.5).done();
const upgrades=makeCase('upgrade-applies-to-future-shots',45283);upgrades.action('fire',-1.32).action('step',20).action('offerUpgrades');
upgrades.action('chooseUpgrade',upgrades.state().offers[0]).action('step',40).action('fire',-1.85).action('step',180).done();
makeCase('boss-and-frost-lane-queue',28743,undefined,s=>{
 s.enemies=[];s.startWave(5);s.board=Config.dice.map(d=>s.makeDie(d.id,3)).concat([null,null]);
 for(const e of s.enemies){e.slowUntil=100;e.slowFactor=.38;}s.takeEvents();
}).action('fire',-1.15).action('step',300).action('fire',-2.14).action('step',300).action('restore').action('step',120).done();
makeCase('volatile-secondary-chain',4153,['blast'],s=>{
 s.enemies=[];for(let i=0;i<7;i++)s.addEnemy({x:66+i*50,y:380,w:39,h:39,hp:12,speed:0,kind:'volatile',wave:1});
 s.board.fill(null);s.board[0]=s.makeDie('blast',6);
}).action('fire',-Math.PI/2).action('step',100).action('step',240).done();
makeCase('split-child-cap-queue',92516,['split'],s=>{
 s.enemies=[];s.addEnemy({x:216,y:300,w:39,h:39,hp:1e9,speed:0,kind:'normal',wave:1});
 s.board.fill(null);const die=s.makeDie('split',6);s.board[0]=die;const snapshot={type:die.type,pips:die.pips,stats:s.stats(die),source:{x:75,y:638},surge:false};
 for(let i=0;i<420;i++)s.projectiles.push(s.makeProjectile(216,326+i%3*.01,-Math.PI/2,snapshot));
}).action('step',1).action('step',10).action('step',120).action('restore').action('step',30).done();
for(const seed of [1,924153,0xFFFFFFFF]){
 const a=makeCase('long-mixed-'+seed,seed,undefined,s=>{s.energy=1000;s.board=Array.from({length:8},(_,i)=>s.makeDie(Config.dice[i%6].id,2));});
 for(let i=0;i<12;i++){if(a.state().awaitingUpgrade)a.action('chooseUpgrade',a.state().offers[0]);a.action('fire',-1.5707963267948966+Math.sin(i*.61)*.8).action('step',180);}
 a.action('restore').action('step',120).done();
}
write('simulation_cases.json',cases);
const stats=[];
for(const upgraded of [false,true]){
 const s=new Simulation({seed:9137});if(upgraded)s.upgrades={power:2,pulse:2,frost:2,reload:2,bounce:2,blast:2,arc:2,split:2,bank:2};
 for(const type of Config.dice)for(let pips=1;pips<=6;pips++)stats.push({type:type.id,pips,upgrades:s.upgrades,expected:s.stats({type:type.id,pips})});
}
write('stats.json',stats);
const random=[];for(const seed of [0,1,924153,0xFFFFFFFF]){const rng=new M.RNG(seed);random.push({seed,values:Array.from({length:64},()=>rng.next()),state:rng.state});}write('rng.json',random);
const collisions=[];const box={left:100,right:139,top:200,bottom:239};
for(const [x,y,dx,dy]of [[120,260,0,-80],[80,220,80,0],[150,220,-80,0],[120,190,0,80],[100,200,30,30],[110,210,0,0],[20,20,0,500],[20,20,200,400],[139,239,10,10],[120,220,-80,0]])collisions.push({x,y,dx,dy,box,expected:M.sweepAABB(x,y,dx,dy,box)});
write('collisions.json',collisions);
const traces=[];for(const angle of [-3,-2.6,-2.1,-Math.PI/2,-1.17,-.65,0]){const s=new Simulation({seed:24137});traces.push({angle,expected:s.traceAim(angle)});}write('traces.json',traces);
const audio=[],buffers=[];let offset=0;
for(const kind of ['tap','launch','hit','kill','explode','merge','summon','upgrade','breach','clear','music','error'])for(let variant=0;variant<7;variant++){
 const s=synthesize(kind,variant);const bytes=Buffer.from(s.data.buffer);buffers.push(bytes);audio.push({kind,variant,sampleRate:s.sampleRate,offset,count:s.data.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex')});offset+=s.data.length;
}
fs.writeFileSync(path.join(output,'audio_reference.f32'),Buffer.concat(buffers));write('audio_index.json',audio);
const events=[
 {type:'hit',x:166,y:220,amount:51.2,color:'#72EAC8'},
 {type:'kill',x:216,y:300,w:39,color:'#FFAD76',reward:4,combo:6,kind:'volatile'},
 {type:'explosion',x:216,y:300,radius:78,color:'#FFAD76',volatile:true},
 {type:'arc',x:116,y:240,tx:216,ty:292,color:'#CBA7FF'},
 {type:'wall',x:401,y:262,color:'#FF97B8',boost:true},
 {type:'split',x:166,y:333,color:'#F8DE87',count:3},
 {type:'conduit',slot:2,color:'#87D6FF',surge:true},
 {type:'merge',a:0,b:1,oldType:'pulse',die:{id:900,type:'blast',pips:3,cooldown:1,flash:0}},
 {type:'launch',x:216,y:530,color:'#FFAD76',surge:true},
 {type:'breach',x:365,y:512,lost:1}
];
const effects=[];
function effectState(r){return{t:r.t,fxTime:r.fxTime,shake:r.shake,flash:r.flash,particles:r.particles,rings:r.rings,floaters:r.floaters,arcs:r.arcs,conduits:r.conduits,pulses:r.pulses,banner:r.banner,rng:r.rng.state};}
for(const reduced of [false,true]){
 const r=new Renderer({getContext:()=>({})});r.reduceMotion=reduced;const steps=[];
 for(const event of events){r.onEvent(event);steps.push({event,expected:clone(effectState(r))});}
 for(const [dt,active]of [[1/120,true],[1/60,true],[.1,false],[.1,true],[.3,true],[.6,true]]){r.update(dt,active);steps.push({dt,active,expected:clone(effectState(r))});}
 effects.push({reduced,steps});
}
write('effects.json',effects);
const visual=new Simulation({seed:24137});visual.board=Array.from({length:8},(_,i)=>visual.makeDie(Config.dice[i%6].id,i%3+1));visual.energy=68;
visual.enemies=[];for(let i=0;i<18;i++)visual.addEnemy({x:66+i%7*50,y:179+Math.floor(i/7)*55,w:39,h:39,hp:35+i*7,speed:0,kind:i%5===1?'volatile':i%5===3?'armored':'normal',wave:3});
visual.wave=3;visual.time=10;visual.combo=7;visual.comboTime=1.5;visual.score=2450;visual.health=9;
visual.enemies[4].slowUntil=20;visual.enemies[4].slowFactor=.38;
for(let i=0;i<6;i++){const d=visual.board[i],snap={type:d.type,pips:d.pips,stats:visual.stats(d),surge:i===1};const p=visual.makeProjectile(91+i*43,365+(i%2)*41,-1.2-i*.17,snap);p.trail=Array.from({length:7},(_,n)=>({x:p.x-(6-n)*p.vx/120,y:p.y-(6-n)*p.vy/120}));if(d.type==='bank')p.wallPower=1.35;visual.projectiles.push(p);}
visual.grid.rebuild(visual.enemies);write('visual_states.json',{battle:visual.exportSave(),effects:events});
write('fixture_manifest.json',{generatedFrom:'Original DiceGame-project.zip JavaScript implementation, NOT the C# port',simulationCases:cases.length,checkpoints:cases.reduce((n,c)=>n+c.steps.length,0),statProfiles:stats.length,audioProfiles:audio.length,totalAudioSamples:offset,effectCases:effects.length,sourceFiles:Object.fromEntries(['math.js','simulation.js','renderer.js','audio.js'].map(f=>[f,crypto.createHash('sha256').update(fs.readFileSync(path.join(original,'src',f))).digest('hex')]))});
console.log(`Generated ${cases.length} cases / ${cases.reduce((n,c)=>n+c.steps.length,0)} checkpoints, ${stats.length} stats, ${audio.length} sounds.`);
