(function(root,factory){if(typeof module==='object'&&module.exports)module.exports=factory(require('./config.generated.js'),require('./math.js'),require('./simulation.js'),require('./renderer.js'),require('./audio.js'));else root.DiceApp=factory(root.DiceConfig,root.DiceMath,root.DiceSim,root.DiceRenderer,root.DiceAudio);})(typeof globalThis!=='undefined'?globalThis:this,function(Config,M,S,View,Audio){
  'use strict';
  const C=Config.game,KEY='DICE_RICOCHET_SAVE_V1';
  class App{
    constructor(canvas,platform={}){
      this.platform=platform;this.renderer=new View.Renderer(canvas,platform);this.audio=new Audio.Sound(platform);
      this.settings={sound:true,music:false,reduceMotion:false};this.meta={bestWave:0,bestScore:0};this.deck=Config.dice.map(d=>d.id);this.editingDeck=[];
      this.scene='menu';this.helpReturn='menu';this.sim=null;this.resumeData=null;this.pointer=null;this.selectedSlot=-1;this.aimAngle=-Math.PI/2;
      this.toast=null;this.toastCooldown=0;this.saveClock=0;this.accumulator=0;this.lastFrame=0;this.running=false;this.destroyed=false;this.storageFailed=false;this.frameId=0;
      this.load();this.audio.setEnabled(this.settings.sound);this.audio.music=this.settings.music;
    }
    load(){
      try{
        const raw=this.platform.readStorage?this.platform.readStorage(KEY):null;if(!raw)return;
        if(typeof raw!=='string'||raw.length>2500000)throw new Error('Invalid saved data');const data=JSON.parse(raw);if(data.version!==C.schema)return;
        if(S.validDeck(data.deck))this.deck=data.deck.slice();
        if(data.settings)for(const k of Object.keys(this.settings))if(typeof data.settings[k]==='boolean')this.settings[k]=data.settings[k];
        if(data.meta)for(const k of Object.keys(this.meta))if(Number.isFinite(data.meta[k])&&data.meta[k]>=0&&data.meta[k]<=1e18)this.meta[k]=data.meta[k];
        if(data.run){S.Simulation.restore(data.run);if(!data.run.over)this.resumeData=data.run;}
      }catch(_){this.resumeData=null;this.notify('旧存档无法读取；可正常开始新的一局。');}
    }
    save(){
      if(this.sim&&!this.sim.over){this.recordRun();this.resumeData=this.sim.exportSave();}
      if(this.sim&&this.sim.over)this.resumeData=null;
      const payload={version:C.schema,settings:this.settings,meta:this.meta,deck:this.deck,run:this.resumeData};
      try{if(this.platform.writeStorage)this.platform.writeStorage(KEY,JSON.stringify(payload));}
      catch(_){if(!this.storageFailed){this.storageFailed=true;this.notify('无法保存到本地；游戏仍可继续。');}}
    }
    recordRun(){if(this.sim){this.meta.bestWave=Math.max(this.meta.bestWave,this.sim.wave);this.meta.bestScore=Math.max(this.meta.bestScore,this.sim.score);}}
    clearFX(){const r=this.renderer;r.particles=[];r.rings=[];r.floaters=[];r.arcs=[];r.conduits=[];r.pulses=[];r.banner=null;r.shake=0;r.flash=0;}
    startNew(seed){
      this.clearFX();this.resumeData=null;this.sim=new S.Simulation({deck:this.deck,seed:seed===undefined?((Date.now()^(Math.random()*0xFFFFFFFF))>>>0):seed});
      this.scene='play';this.pointer=null;this.aimAngle=-Math.PI/2;this.accumulator=0;this.saveClock=0;this.consumeEvents();this.save();
    }
    resume(){
      if(!this.resumeData){this.startNew();return;}
      try{this.clearFX();this.sim=S.Simulation.restore(this.resumeData);this.aimAngle=this.sim.lastAim;this.scene=this.sim.awaitingUpgrade?'upgrade':'play';this.accumulator=0;this.pointer=null;this.audio.unlock();}
      catch(_){this.resumeData=null;this.scene='menu';this.notify('存档校验失败，请开始新的一局。');}
    }
    notify(message,duration=2){if(this.toast&&this.toast.text===message&&this.toast.life>1)return;this.toast={text:message,life:duration};}
    consumeEvents(){
      if(!this.sim)return;
      for(const e of this.sim.takeEvents()){
        this.renderer.onEvent(e);
        if(e.type==='hit')this.audio.play('hit',Math.min(6,Math.floor(this.sim.combo/3)));
        else if(e.type==='kill')this.audio.play('kill',Math.min(6,Math.floor(e.combo/2)));
        else if(e.type==='explosion')this.audio.play('explode');
        else if(e.type==='volley')this.audio.play('launch');
        else if(e.type==='merge'){this.audio.play('merge');this.haptic('medium');}
        else if(e.type==='summon')this.audio.play('summon');
        else if(e.type==='breach'){this.audio.play('breach');this.haptic('medium');}
        else if(e.type==='clear')this.audio.play('clear');
        else if(e.type==='upgrade'){this.cancelPointer();this.scene='upgrade';this.audio.play('upgrade');this.save();}
        else if(e.type==='upgraded')this.audio.play('upgrade');
        else if(e.type==='gameover'){this.cancelPointer();this.scene='over';this.recordRun();this.resumeData=null;this.save();}
      }
    }
    haptic(type){if(!this.settings.reduceMotion&&this.platform.vibrate)try{this.platform.vibrate(type);}catch(_){} }
    resize(width,height,dpr,safeTop=0,safeBottom=0){this.cancelPointer();this.renderer.resize(width,height,dpr,safeTop,safeBottom);this.renderer.render(this);}
    tick(dt){
      dt=M.clamp(Number.isFinite(dt)?dt:0,0,C.limits.maxFrameDelta);
      if(this.toast){this.toast.life-=dt;if(this.toast.life<=0)this.toast=null;}
      this.toastCooldown=Math.max(0,this.toastCooldown-dt);
      const active=this.scene==='play';
      if(active&&this.sim){
        this.accumulator+=dt;let steps=0;
        while(this.accumulator>=C.limits.fixedStep&&steps<C.limits.maxSteps&&this.scene==='play'){
          this.sim.step(C.limits.fixedStep);this.accumulator-=C.limits.fixedStep;steps++;this.consumeEvents();
        }
        if(steps===C.limits.maxSteps)this.accumulator=0;
        this.saveClock+=dt;if(this.saveClock>=4){this.saveClock=0;this.save();}
      }else this.accumulator=0;
      this.renderer.update(dt,active);this.audio.tick(dt,this.scene==='play'||this.scene==='menu');
    }
    start(){
      if(this.running||this.destroyed)return;this.running=true;this.lastFrame=0;
      const frame=ms=>{if(!this.running||this.destroyed)return;const dt=this.lastFrame?(ms-this.lastFrame)/1000:0;this.lastFrame=ms;this.tick(dt);this.renderer.render(this);this.frameId=this.platform.raf(frame);};
      this.frameId=this.platform.raf(frame);
    }
    destroy(){this.save();this.running=false;this.destroyed=true;if(this.platform.cancelRaf)this.platform.cancelRaf(this.frameId);this.audio.destroy();if(this.platform.cleanup)this.platform.cleanup();}
    onHide(){
      this.cancelPointer();if(this.scene==='play'||this.scene==='die')this.scene='paused';this.save();this.audio.suspend();this.accumulator=0;this.lastFrame=0;
    }
    onShow(){this.lastFrame=0;this.accumulator=0;/* A deliberate tap is required to resume after leaving the app. */}
    pause(){if(this.scene==='play'){this.cancelPointer();this.scene='paused';this.save();}}
    inside(p,r){return p.x>=r.x&&p.x<=r.x+r.w&&p.y>=r.y&&p.y<=r.y+r.h;}
    findButton(point){for(let i=this.renderer.buttons.length-1;i>=0;i--){const b=this.renderer.buttons[i];if(this.inside(point,b))return b;}return null;}
    slotAt(point){for(let i=0;i<8;i++){const p=S.slotPosition(i);if(Math.abs(point.x-p.x)<=41&&point.y>=p.y-42&&point.y<=p.y+44)return i;}return-1;}
    onDown(x,y,id=1){
      if(this.pointer)return;this.audio.unlock();const point={x,y};
      if(x<0||x>C.view.width||y<0||y>C.view.height)return;
      const b=this.findButton(point);
      if(b){this.pointer={id,mode:'button',button:b.id,x,y,startX:x,startY:y};return;}
      if(this.scene!=='play'||!this.sim)return;
      const slot=this.slotAt(point);
      if(slot>=0){this.pointer={id,mode:this.sim.board[slot]?'diepress':'button',button:this.sim.board[slot]?null:'summonAt:'+slot,slot,x,y,startX:x,startY:y};return;}
      if(x>=22&&x<=410&&y>=124&&y<=575){this.pointer={id,mode:'aim',x,y,startX:x,startY:y};this.updateAim(x,y);}
    }
    onMove(x,y,id=1){
      const p=this.pointer;if(!p||p.id!==id)return;p.x=x;p.y=y;
      if(p.mode==='diepress'&&Math.hypot(x-p.startX,y-p.startY)>8)p.mode='drag';
      if(p.mode==='aim')this.updateAim(x,y);
    }
    updateAim(x,y){if(!this.sim)return;this.aimAngle=this.sim.clampAim(Math.atan2(Math.min(y-C.arena.launchY,-24),x-216));}
    onUp(x,y,id=1){
      const p=this.pointer;if(!p||p.id!==id)return;this.pointer=null;
      if(p.mode==='button'){
        if(p.button&&p.button.startsWith('summonAt:')){const slot=this.slotAt({x,y});if(slot===p.slot)this.action(p.button);}
        else{const b=this.findButton({x,y});if(b&&b.id===p.button)this.action(p.button);}
      }else if(p.mode==='aim'&&this.scene==='play'){
        if(x>=22&&x<=410&&y>=124&&y<=575){this.updateAim(x,y);const result=this.sim.fire(this.aimAngle);
          if(!result.ok&&this.toastCooldown<=0){this.notify(result.reason==='reloading'?'骰子正在装填，稍后再松手发射。':result.reason==='busy'?'弹丸正在通过回廊，请稍后发射。':'先召唤一颗骰子。',1.1);this.toastCooldown=1;}
          this.consumeEvents();
        }
      }else if(p.mode==='diepress'&&this.scene==='play'&&this.sim.board[p.slot]){this.selectedSlot=p.slot;this.scene='die';this.save();this.audio.play('tap');}
      else if(p.mode==='drag'&&this.scene==='play'){
        const target=this.slotAt({x,y});
        if(target>=0&&target!==p.slot){
          if(!this.sim.board[target])this.sim.move(p.slot,target);
          else{const result=this.sim.merge(p.slot,target);if(!result.ok){this.audio.play('error');const d=this.sim.board[p.slot];this.notify(d&&d.pips===6?'六点骰子已达上限，可保留或回收。':'只能合成同种类、同点数的骰子。');}}
          this.consumeEvents();this.save();
        }
      }
      this.renderer.render(this);
    }
    cancelPointer(id){if(id===undefined||(this.pointer&&this.pointer.id===id))this.pointer=null;}
    summon(preferred=-1){
      if(this.scene!=='play')return;const r=this.sim.summon();
      if(!r.ok){this.audio.play('error');this.notify(r.reason==='full'?'八格已满：合成配对，或点按骰子回收。':'能量不足，击破敌人或等待自然恢复。');}
      else{if(preferred>=0&&preferred!==r.slot&&!this.sim.board[preferred]){this.sim.move(r.slot,preferred);for(const e of this.sim.events)if(e.type==='summon')e.slot=preferred;}this.consumeEvents();this.save();}
    }
    action(id){
      this.audio.play('tap');
      if(id==='start'||id==='restart'||id==='confirmStart'){this.startNew();}
      else if(id==='new'){this.scene='confirmNew';}
      else if(id==='cancelNew'){this.scene='menu';}
      else if(id==='resume'){this.resume();}
      else if(id==='pause'){this.pause();}
      else if(id==='continue'){this.scene=this.sim&&this.sim.awaitingUpgrade?'upgrade':'play';this.accumulator=0;this.audio.unlock();}
      else if(id==='sound'){this.settings.sound=!this.settings.sound;this.audio.setEnabled(this.settings.sound);this.save();}
      else if(id==='music'){this.settings.music=!this.settings.music;this.audio.music=this.settings.music;this.save();}
      else if(id==='motion'){this.settings.reduceMotion=!this.settings.reduceMotion;this.save();}
      else if(id==='home'){this.save();this.sim=null;this.scene='menu';this.pointer=null;this.clearFX();}
      else if(id==='summon'){this.summon();}
      else if(id.startsWith('summonAt:')){this.summon(Number(id.split(':')[1]));}
      else if(id==='help'){this.helpReturn=this.scene==='menu'?'menu':this.scene;this.scene='help';this.save();}
      else if(id==='closeHelp'){this.scene=this.helpReturn;this.accumulator=0;}
      else if(id==='closeDie'){this.scene='play';this.selectedSlot=-1;this.accumulator=0;}
      else if(id.startsWith('recycle:')){const i=Number(id.split(':')[1]);const r=this.sim.recycle(i);this.scene='play';this.selectedSlot=-1;if(r.ok){this.notify('已回收，获得 '+r.amount+' 能量。');this.consumeEvents();this.save();}}
      else if(id.startsWith('upgrade:')){const chosen=id.split(':')[1];if(this.sim.chooseUpgrade(chosen)){this.scene='play';this.accumulator=0;this.consumeEvents();this.save();}}
      else if(id==='editDeck'){this.editingDeck=this.deck.slice();this.scene='deck';}
      else if(id==='deckBack'){this.scene='menu';}
      else if(id.startsWith('deck:')){
        const type=id.split(':')[1];if(!S.TYPES[type])return;const i=this.editingDeck.indexOf(type);
        if(i>=0)this.editingDeck.splice(i,1);else if(this.editingDeck.length<C.rules.maxDeck)this.editingDeck.push(type);else this.notify('一个卡组最多携带六种骰子。');
      }else if(id==='deckSave'){
        if(!S.validDeck(this.editingDeck)){this.notify('请携带一到六种不同骰子。');return;}
        this.deck=this.editingDeck.slice();this.scene='menu';this.save();if(this.resumeData)this.notify('卡组已更新，将在新的一局生效。');
      }
      this.pointer=null;this.renderer.render(this);
    }
    key(key){
      if(key==='Escape'){if(this.scene==='play')this.pause();else if(this.scene==='paused')this.action('continue');else if(this.scene==='die')this.action('closeDie');else if(this.scene==='help')this.action('closeHelp');}
      else if(key===' '&&this.scene==='play')this.summon();
      else if(key.toLowerCase()==='m')this.action('sound');
    }
    debugSnapshot(){const s=this.sim;return{scene:this.scene,deck:this.deck.slice(),pointer:this.pointer?{...this.pointer}:null,aimAngle:this.aimAngle,toast:this.toast&&this.toast.text,simulation:s?{wave:s.wave,health:s.health,energy:s.energy,score:s.score,kills:s.kills,merges:s.merges,manualVolleys:s.manualVolleys,projectiles:s.projectiles.length,pending:s.pendingShots.length,enemies:s.enemies.length,board:s.board.map(d=>d?{...d}:null),offers:s.offers.slice(),time:s.time}:null};}
  }
  return{App,STORAGE_KEY:KEY};
});
