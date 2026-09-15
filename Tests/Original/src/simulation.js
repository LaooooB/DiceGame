(function(root,factory){
  if(typeof module==='object'&&module.exports) module.exports=factory(require('./config.generated.js'),require('./math.js'));
  else root.DiceSim=factory(root.DiceConfig,root.DiceMath);
})(typeof globalThis!=='undefined'?globalThis:this,function(Config,M){
  'use strict';
  const C=Config.game, R=C.rules, A=C.arena, B=C.board;
  const MAX_CHILDREN=2+Math.floor((R.maxPips-1)/2);
  const MAX_PENDING=R.maxQueuedShots*(MAX_CHILDREN+1)+R.maxProjectiles*MAX_CHILDREN;
  const TYPES=Config.dice.reduce((map,d)=>(map[d.id]=d,map),Object.create(null));
  const UPG=Config.upgrades.reduce((map,u)=>(map[u.id]=u,map),Object.create(null));
  const validDeck=deck=>Array.isArray(deck)&&deck.length>=1&&deck.length<=R.maxDeck&&new Set(deck).size===deck.length&&deck.every(id=>Object.prototype.hasOwnProperty.call(TYPES,id));
  function slotPosition(index){return{x:B.left+(index%B.columns)*B.stepX,y:B.top+Math.floor(index/B.columns)*B.stepY};}
  const finite=(v,min=-Infinity,max=Infinity)=>typeof v==='number'&&Number.isFinite(v)&&v>=min&&v<=max;
  class Simulation{
    constructor(options={}){
      const deck=options.deck||Config.dice.map(d=>d.id);
      if(!validDeck(deck))throw new TypeError('A deck must contain 1–6 distinct, registered dice types.');
      this.deck=deck.slice();this.rng=new M.RNG(options.seed===undefined?Date.now():options.seed);
      this.seed=this.rng.state;this.time=0;this.nextId=1;this.wave=0;this.waveTime=0;
      this.health=R.maxHealth;this.energy=R.startEnergy;this.score=0;this.kills=0;this.merges=0;this.shots=0;
      this.combo=0;this.comboTime=0;this.bestCombo=0;this.clearRewarded=false;this.nextWaveIn=-1;
      this.lastAim=-Math.PI/2;this.board=Array(B.slots).fill(null);
      this.enemies=[];this.projectiles=[];this.pendingShots=[];this.damageQueue=[];
      this.events=[];this.upgrades={};this.offers=[];this.awaitingUpgrade=false;this.over=false;
      this.grid=new M.SpatialGrid(58);this.totalDamage=0;this.manualVolleys=0;this.escaped=0;
      // The opening pair guarantees that the merging rule is learnable, even with six types.
      this.board[0]=this.makeDie(this.deck[0],1);this.board[1]=this.makeDie(this.deck[0],1);
      this.board[2]=this.makeDie(this.deck[1]||this.deck[0],1);
      this.startWave(1);this.grid.rebuild(this.enemies);
    }
    makeDie(type,pips){return{id:this.nextId++,type,pips,cooldown:0,flash:0};}
    emit(type,data={}){if(this.events.length<1600)this.events.push(Object.assign({type},data));}
    takeEvents(){const out=this.events;this.events=[];return out;}
    count(){return this.board.filter(Boolean).length;}
    readyCount(){return this.board.filter(d=>d&&d.cooldown<=1e-5).length;}
    hasPair(){for(let a=0;a<B.slots;a++)for(let b=a+1;b<B.slots;b++)if(this.canMerge(a,b))return true;return false;}
    canMerge(a,b){
      if(!Number.isInteger(a)||!Number.isInteger(b)||a===b||a<0||b<0||a>=B.slots||b>=B.slots)return false;
      const x=this.board[a],y=this.board[b];return!!(x&&y&&x.type===y.type&&x.pips===y.pips&&x.pips<R.maxPips);
    }
    stats(die){
      const type=TYPES[die.type], level=C.levels[die.pips-1],u=this.upgrades;
      let multiplier=1+0.18*(u.power||0);
      if(die.type==='pulse')multiplier*=1+0.2*(u.pulse||0);
      if(die.type==='frost')multiplier*=1+0.1*(u.frost||0);
      const volley=type.baseDamage*level.volleyPower*multiplier;
      return{effect:type.effect,damage:volley/die.pips,volley,count:die.pips,reload:type.reload*level.reloadFactor*Math.pow(0.92,u.reload||0),
        bounces:Math.min(25,R.baseBounces+2*(u.bounce||0)),color:type.color,
        blastRadius:(type.radius||0)*(1+0.2*(u.blast||0)),splashFactor:(type.splashFactor||0)*(1+0.15*(u.blast||0)),
        chainCount:1+Math.floor((die.pips-1)/2)+(u.arc||0),chainRange:type.chainRange||0,chainFactor:type.chainFactor||0,
        slowFactor:type.slowFactor||1,slowSeconds:(type.slowSeconds||0)+die.pips*0.1+0.6*(u.frost||0),
        childCount:2+Math.floor((die.pips-1)/2),childFactor:(type.childFactor||0)*(1+0.25*(u.split||0)),
        wallBoost:(type.wallBoost||0)+0.15*(u.bank||0),maxBoost:type.maxBoost||1};
    }
    summon(){
      if(this.over||this.awaitingUpgrade)return{ok:false,reason:'paused'};
      const slot=this.board.indexOf(null);if(slot<0)return{ok:false,reason:'full'};
      if(this.energy+1e-6<R.summonCost)return{ok:false,reason:'energy'};
      this.energy-=R.summonCost;const die=this.makeDie(this.rng.pick(this.deck),1);this.board[slot]=die;
      this.emit('summon',{slot,die:{...die}});return{ok:true,slot,die};
    }
    move(a,b){
      if(this.over||this.awaitingUpgrade||!Number.isInteger(a)||!Number.isInteger(b)||a<0||b<0||a>=B.slots||b>=B.slots||!this.board[a]||this.board[b])return false;
      this.board[b]=this.board[a];this.board[a]=null;this.emit('move',{a,b});return true;
    }
    merge(a,b){
      if(this.over||this.awaitingUpgrade)return{ok:false,reason:'paused'};
      if(!this.canMerge(a,b))return{ok:false,reason:'mismatch'};
      const pips=this.board[b].pips+1, oldType=this.board[b].type;
      const count=pips;
      if(this.pendingShots.length+count>R.maxQueuedShots)return{ok:false,reason:'busy'};
      const die=this.makeDie(this.rng.pick(this.deck),pips);this.board[a]=null;this.board[b]=die;
      this.merges++;
      // Merging never deletes in-flight bullets or their captured combat data.
      this.queueVolley(die,b,this.lastAim,1+R.mergeSurge+0.35*(this.upgrades.surge||0),true);
      die.cooldown=this.stats(die).reload*0.36;
      this.emit('merge',{a,b,oldType,die:{...die}});return{ok:true,die,slot:b};
    }
    recycle(index){
      if(this.over||this.awaitingUpgrade||!Number.isInteger(index)||index<0||index>=B.slots||!this.board[index])return{ok:false};
      const d=this.board[index],amount=C.levels[d.pips-1].recycle;
      this.energy+=amount;this.board[index]=null;this.emit('recycle',{slot:index,amount});return{ok:true,amount};
    }
    clampAim(angle){
      if(!Number.isFinite(angle))return this.lastAim;
      const limit=R.aimMaxDegrees*Math.PI/180;return M.clamp(angle,-Math.PI/2-limit,-Math.PI/2+limit);
    }
    fire(angle){
      if(this.over||this.awaitingUpgrade)return{ok:false,reason:'paused'};
      this.lastAim=this.clampAim(angle);
      const ready=this.board.map((d,i)=>({d,i})).filter(x=>x.d&&x.d.cooldown<=1e-5);
      if(!ready.length)return{ok:false,reason:'reloading'};
      const needed=ready.reduce((s,x)=>s+x.d.pips,0);
      if(this.pendingShots.length+needed>R.maxQueuedShots)return{ok:false,reason:'busy'};
      for(const {d,i}of ready){this.queueVolley(d,i,this.lastAim,1,false);d.cooldown=this.stats(d).reload;d.flash=0.35;}
      this.manualVolleys++;this.emit('volley',{count:needed,dice:ready.length,angle:this.lastAim});
      return{ok:true,count:needed,dice:ready.length};
    }
    queueVolley(die,slot,angle,multiplier,surge){
      const stats=this.stats(die),source=slotPosition(slot);
      // Copy all data at release. Replacing a die or acquiring a relic cannot mutate these bullets.
      const snapshot={type:die.type,pips:die.pips,stats:{...stats,damage:stats.damage*multiplier},source:{...source},surge:!!surge};
      for(let k=0;k<stats.count;k++)this.pendingShots.push({due:this.time+0.075+k*0.056+(slot%4)*0.019,angle,snapshot});
      this.pendingShots.sort((a,b)=>a.due-b.due);
      this.emit('conduit',{slot,color:stats.color,surge:!!surge});
    }
    makeProjectile(x,y,angle,snapshot,child=false){
      const speed=R.projectileSpeed*(child?1.03:1);
      return{id:this.nextId++,x,y,px:x,py:y,vx:Math.cos(angle)*speed,vy:Math.sin(angle)*speed,
        type:snapshot.type,pips:snapshot.pips,stats:{...snapshot.stats},life:child?3.4:R.projectileLife,
        bounces:snapshot.stats.bounces,child,splitDone:child,wallPower:1,lastEnemy:0,surge:!!snapshot.surge,dead:false,trail:[]};
    }
    startWave(wave){
      this.wave=wave;this.waveTime=0;this.clearRewarded=false;this.nextWaveIn=-1;
      const W=C.waves,boss=wave%R.bossEvery===0;
      const base=Math.min(1e12,W.baseHp*Math.pow(W.hpExponential,Math.min(wave-1,220))+W.hpLinear*(wave-1));
      const speed=Math.min(W.maxSpeed,W.baseSpeed+W.speedPerWave*(wave-1));
      const count=Math.min(W.maxCount,Math.floor(W.baseCount+(wave-1)*W.countPerWave));
      let cells=[];
      if(wave===1)cells=[[1,0],[2,0],[3,0],[4,0],[5,0],[0,1],[2,1],[4,1],[6,1]];
      else{
        const rows=Math.ceil(count/5);
        for(let r=0;r<rows;r++){const lanes=this.rng.shuffle([0,1,2,3,4,5,6]);for(const c of lanes.slice(0,Math.min(5,count-cells.length)))cells.push([c,r]);}
      }
      const room=R.maxEnemies-this.enemies.filter(e=>!e.dead).length;
      if(boss&&room>0)this.addEnemy({x:216,y:166,w:90,h:68,hp:Math.round(base*W.bossHpFactor),speed:speed*0.67,kind:'boss',wave});
      const n=Math.min(cells.length,Math.max(0,room-(boss?1:0)));
      for(let i=0;i<n;i++){
        const [col,row]=cells[i],r=this.rng.next();
        let kind=wave>=2&&r<0.14?'armored':(r<0.34?'volatile':'normal');
        if(wave===1&&i===7)kind='volatile';
        const hp=Math.max(1,Math.round(base*(0.84+this.rng.next()*0.28)*(kind==='armored'?1.7:kind==='volatile'?0.78:1)));
        this.addEnemy({x:66+col*50,y:160+row*49+(boss?92:0),w:39,h:39,hp,speed,kind,wave});
      }
      this.grid.rebuild(this.enemies);this.emit('wave',{wave,boss});
    }
    addEnemy(data){
      const enemy=Object.assign({id:this.nextId++,maxHp:data.hp,slowUntil:0,slowFactor:1,flash:0,dead:false},data);
      this.enemies.push(enemy);return enemy;
    }
    availableUpgrades(){
      return Config.upgrades.filter(u=>(this.upgrades[u.id]||0)<u.max&&(!u.requires||this.deck.includes(u.requires))&&(u.id!=='repair'||this.health<R.maxHealth));
    }
    offerUpgrades(){
      let pool=this.availableUpgrades();
      // Evergreen linear bonuses remain available after specialized upgrades reach their caps.
      // Never uncap reload or recursive/area effects to fabricate three choices.
      if(pool.length<3)pool=Config.upgrades.filter(u=>['power','income','surge'].includes(u.id));
      this.offers=this.rng.shuffle(pool).slice(0,3).map(u=>u.id);this.awaitingUpgrade=true;
      this.emit('upgrade',{offers:this.offers.slice()});
    }
    chooseUpgrade(id){
      if(!this.awaitingUpgrade||!this.offers.includes(id)||!UPG[id])return false;
      this.upgrades[id]=(this.upgrades[id]||0)+1;
      if(id==='repair')this.health=Math.min(R.maxHealth,this.health+4);
      this.awaitingUpgrade=false;this.offers=[];this.emit('upgraded',{id});this.startWave(this.wave+1);return true;
    }
    advanceWave(){
      if(this.wave%R.upgradeEvery===0){this.offerUpgrades();return;}
      this.startWave(this.wave+1);
    }
    advanceEnemies(dt){
      // Preserve lane order from before movement, including on a catch-up frame.
      const sorted=this.enemies.filter(e=>!e.dead).sort((a,b)=>b.y-a.y||a.id-b.id);
      for(const e of this.enemies)if(!e.dead){
        const slowed=this.time<e.slowUntil;const factor=slowed?(e.kind==='boss'?Math.max(0.72,e.slowFactor):e.slowFactor):1;
        e.y+=e.speed*factor*dt;e.flash=Math.max(0,e.flash-dt*6);
      }
      // Lane queues stop a faster block from visually passing through a frozen front block.
      const front=Array(7).fill(Infinity);
      for(const e of sorted){
        const lo=M.clamp(Math.floor((e.x-e.w/2-41)/50),0,6),hi=M.clamp(Math.floor((e.x+e.w/2-41)/50),0,6);
        let limit=Infinity;for(let lane=lo;lane<=hi;lane++)limit=Math.min(limit,front[lane]-e.h/2-4);
        e.y=Math.min(e.y,limit);for(let lane=lo;lane<=hi;lane++)front[lane]=e.y-e.h/2;
        if(e.y+e.h/2>=A.breach){
          e.dead=true;const lost=e.kind==='boss'?3:1;this.health=Math.max(0,this.health-lost);this.escaped++;
          this.emit('breach',{x:e.x,y:A.breach,lost});
          if(this.health<=0){this.over=true;this.emit('gameover',{wave:this.wave,score:this.score});break;}
        }
      }
    }
    step(dt){
      if(this.over||this.awaitingUpgrade)return;
      if(!finite(dt,0,0.101))throw new RangeError('Simulation.step requires a finite dt in [0, 0.1].');
      this.time+=dt;this.waveTime+=dt;this.energy=Math.min(999999,this.energy+R.passiveEnergy*dt);
      this.comboTime=Math.max(0,this.comboTime-dt);if(this.comboTime===0)this.combo=0;
      for(const d of this.board)if(d){d.cooldown=Math.max(0,d.cooldown-dt);d.flash=Math.max(0,d.flash-dt);}
      this.advanceEnemies(dt);if(this.over)return;
      this.grid.rebuild(this.enemies);
      while(this.pendingShots.length&&this.pendingShots[0].due<=this.time&&this.projectiles.length<R.maxProjectiles){
        const s=this.pendingShots.shift();
        const spawned=this.makeProjectile(s.child?s.x:216,s.child?s.y:A.launchY,s.angle,s.snapshot,!!s.child);
        spawned.lastEnemy=s.lastEnemy||0;this.projectiles.push(spawned);
        if(!s.child){this.shots++;this.emit('launch',{x:216,y:A.launchY,color:s.snapshot.stats.color,surge:s.snapshot.surge});}
      }
      const count=this.projectiles.length;
      for(let i=0;i<count;i++)if(!this.projectiles[i].dead)this.moveProjectile(this.projectiles[i],dt);
      this.flushDamage();
      this.projectiles=this.projectiles.filter(p=>!p.dead);this.enemies=this.enemies.filter(e=>!e.dead);
      if(!this.enemies.length&&!this.clearRewarded&&this.waveTime>=2.0){
        this.clearRewarded=true;this.nextWaveIn=1.35;const reward=C.waves.clearEnergy+Math.min(25,this.wave);
        this.energy+=reward;this.score+=this.wave*35;this.emit('clear',{reward,wave:this.wave});
      }
      if(this.nextWaveIn>=0){this.nextWaveIn-=dt;if(this.nextWaveIn<=0)this.advanceWave();}
      else if(this.waveTime>=R.waveSeconds)this.advanceWave();
    }
    moveProjectile(p,dt){
      p.life-=dt;if(p.life<=0){p.dead=true;return;}p.px=p.x;p.py=p.y;
      // Split children can begin a fraction inside a wall; project them back before the sweep.
      p.x=M.clamp(p.x,A.left+R.projectileRadius,A.right-R.projectileRadius);
      p.y=Math.max(A.top+R.projectileRadius,p.y);
      p.trail.push({x:p.x,y:p.y});if(p.trail.length>7)p.trail.shift();
      let remaining=dt;
      for(let iteration=0;iteration<7&&remaining>1e-6&&!p.dead;iteration++){
        const dx=p.vx*remaining,dy=p.vy*remaining,r=R.projectileRadius;
        let hit=null;
        const wall=(t,nx,ny)=>{if(t>=0&&t<=1&&(!hit||t<hit.t))hit={t,nx,ny,wall:true};};
        if(dx<0)wall((A.left+r-p.x)/dx,1,0);else if(dx>0)wall((A.right-r-p.x)/dx,-1,0);
        if(dy<0)wall((A.top+r-p.y)/dy,0,1);
        if(p.y>A.bottom+18&&p.vy>0){p.dead=true;break;}
        const candidates=this.grid.query(Math.min(p.x,p.x+dx)-r,Math.min(p.y,p.y+dy)-r,Math.max(p.x,p.x+dx)+r,Math.max(p.y,p.y+dy)+r);
        for(const e of candidates){
          const box={left:e.x-e.w/2-r,right:e.x+e.w/2+r,top:e.y-e.h/2-r,bottom:e.y+e.h/2+r};
          if(e.id===p.lastEnemy){
            if(p.x>=box.left-0.2&&p.x<=box.right+0.2&&p.y>=box.top-0.2&&p.y<=box.bottom+0.2)continue;
            p.lastEnemy=0;
          }
          const h=M.sweepAABB(p.x,p.y,dx,dy,box);
          if(h&&(!hit||h.t<hit.t))hit={...h,enemy:e};
        }
        if(!hit){p.x+=dx;p.y+=dy;remaining=0;break;}
        p.x+=dx*hit.t;p.y+=dy*hit.t;
        const push=(hit.penetration||0)+0.035;p.x+=hit.nx*push;p.y+=hit.ny*push;
        const dot=p.vx*hit.nx+p.vy*hit.ny;
        if(dot<0){p.vx-=2*dot*hit.nx;p.vy-=2*dot*hit.ny;}
        remaining*=Math.max(0,1-hit.t);p.bounces--;
        if(hit.wall){
          if(p.stats.effect==='bank')p.wallPower=Math.min(p.stats.maxBoost,p.wallPower+p.stats.wallBoost);
          this.emit('wall',{x:p.x,y:p.y,color:p.stats.color,boost:p.stats.effect==='bank'});
        }else{
          p.lastEnemy=hit.enemy.id;this.primaryHit(hit.enemy,p);this.flushDamage();
        }
        if(p.bounces<=0)p.dead=true;
        // A zero-time collision still consumes a small amount of time to guarantee progress.
        if(hit.t<1e-6)remaining=Math.max(0,remaining-1e-5);
      }
      if(p.y>A.bottom+18||p.x<A.left-8||p.x>A.right+8||!Number.isFinite(p.x)||!Number.isFinite(p.y))p.dead=true;
    }
    primaryHit(enemy,p){
      const damage=p.stats.damage*p.wallPower;p.wallPower=1;
      this.applyDamage(enemy,damage,p.stats.color);
      if(p.stats.effect==='blast'){
        this.areaDamage(enemy.x,enemy.y,p.stats.blastRadius,damage*p.stats.splashFactor,p.stats.color,enemy.id);
        this.emit('explosion',{x:enemy.x,y:enemy.y,radius:p.stats.blastRadius,color:p.stats.color});
      }else if(p.stats.effect==='arc'){
        const seen=new Set([enemy.id]);let from=enemy;
        for(let n=0;n<p.stats.chainCount;n++){
          const range=p.stats.chainRange;
          const targets=this.grid.query(from.x-range,from.y-range,from.x+range,from.y+range)
            .filter(e=>!seen.has(e.id)&&M.dist2(e,from)<=range*range).sort((a,b)=>M.dist2(a,from)-M.dist2(b,from)||a.id-b.id);
          if(!targets.length)break;const next=targets[0];seen.add(next.id);
          this.applyDamage(next,damage*p.stats.chainFactor*Math.pow(0.86,n),p.stats.color);
          this.emit('arc',{x:from.x,y:from.y,tx:next.x,ty:next.y,color:p.stats.color});from=next;
        }
      }else if(p.stats.effect==='frost'&&!enemy.dead){
        enemy.slowUntil=Math.max(enemy.slowUntil,this.time+p.stats.slowSeconds);enemy.slowFactor=Math.max(0.35,p.stats.slowFactor);
      }else if(p.stats.effect==='split'&&!p.splitDone){
        p.splitDone=true;const count=p.stats.childCount;
        for(let n=0;n<count;n++){
          const angle=Math.atan2(p.vy,p.vx)+(n-(count-1)/2)*0.38;
          const snapshot={type:p.type,pips:p.pips,stats:{...p.stats,damage:p.stats.damage*p.stats.childFactor,bounces:Math.min(6,p.bounces)},surge:false};
          if(this.projectiles.length<R.maxProjectiles){
            const child=this.makeProjectile(p.x+Math.cos(angle)*5,p.y+Math.sin(angle)*5,angle,snapshot,true);child.lastEnemy=enemy.id;this.projectiles.push(child);
          }else{
            // Defer the real child instead of deleting it or replacing ricochets with fake damage.
            // New player volleys stop at maxQueuedShots; only one-generation children use the reserve.
            this.pendingShots.push({due:this.time+1e-5,angle,snapshot,child:true,x:p.x+Math.cos(angle)*5,y:p.y+Math.sin(angle)*5,lastEnemy:enemy.id});
          }
        }
        this.pendingShots.sort((a,b)=>a.due-b.due);
        this.emit('split',{x:p.x,y:p.y,color:p.stats.color,count});
      }
    }
    areaDamage(x,y,radius,amount,color,exclude=0){
      for(const e of this.grid.query(x-radius,y-radius,x+radius,y+radius))
        if(e.id!==exclude&&Math.hypot(e.x-x,e.y-y)<=radius+Math.min(e.w,e.h)*0.22)this.damageQueue.push({id:e.id,amount,color});
    }
    applyDamage(enemy,amount,color){
      if(enemy.dead||!finite(amount,0)||amount===0)return;
      const actual=Math.min(enemy.hp,amount);enemy.hp-=amount;enemy.flash=1;this.totalDamage+=actual;
      this.emit('hit',{x:enemy.x,y:enemy.y,amount,color,boss:enemy.kind==='boss'});
      if(enemy.hp<=0){
        enemy.dead=true;this.kills++;this.combo++;this.comboTime=2.1;this.bestCombo=Math.max(this.bestCombo,this.combo);
        const reward=C.waves.killEnergy+(this.upgrades.income||0)+(enemy.kind==='boss'?16:0);
        this.energy=Math.min(999999,this.energy+reward);
        const points=Math.round((10+enemy.wave*3)*(enemy.kind==='boss'?12:1)*(1+Math.min(this.combo,30)*0.025));this.score+=points;
        this.emit('kill',{x:enemy.x,y:enemy.y,w:enemy.w,color:enemy.kind==='volatile'?'#FFAD76':color,reward,combo:this.combo,kind:enemy.kind});
        if(enemy.kind==='volatile'){
          const power=enemy.maxHp*0.8;
          this.areaDamage(enemy.x,enemy.y,78,power,'#FFAD76',enemy.id);
          this.emit('explosion',{x:enemy.x,y:enemy.y,radius:78,color:'#FFAD76',volatile:true});
        }
      }
    }
    flushDamage(){
      // Secondary damage never calls primaryHit. Volatile enemies are guarded by dead before enqueueing.
      let budget=1024;
      while(this.damageQueue.length&&budget-->0){const d=this.damageQueue.shift();const e=this.enemies.find(e=>e.id===d.id);if(e&&!e.dead)this.applyDamage(e,d.amount,d.color);}
    }
    traceAim(angle,maxDistance=1050){
      let x=216,y=A.launchY;const points=[{x,y}],r=R.projectileRadius;
      let vx=Math.cos(this.clampAim(angle)),vy=Math.sin(this.clampAim(angle)),left=maxDistance,last=0;
      for(let i=0;i<5&&left>0;i++){
        let hit=null;const wall=(d,nx,ny)=>{if(d>0.02&&d<left&&(!hit||d<hit.d))hit={d,nx,ny};};
        if(vx<0)wall((A.left+r-x)/vx,1,0);else if(vx>0)wall((A.right-r-x)/vx,-1,0);
        if(vy<0)wall((A.top+r-y)/vy,0,1);else wall((A.bottom+8-y)/vy,0,-1);
        for(const e of this.enemies){if(e.dead||e.id===last)continue;
          const h=M.sweepAABB(x,y,vx*left,vy*left,{left:e.x-e.w/2-r,right:e.x+e.w/2+r,top:e.y-e.h/2-r,bottom:e.y+e.h/2+r});
          if(h&&h.t>0.00001&&(!hit||h.t*left<hit.d))hit={d:h.t*left,nx:h.nx,ny:h.ny,id:e.id};
        }
        const d=hit?hit.d:left;x+=vx*d;y+=vy*d;points.push({x,y,enemy:hit&&hit.id});left-=d;
        if(!hit||y>A.bottom+2)break;
        const dot=vx*hit.nx+vy*hit.ny;vx-=2*dot*hit.nx;vy-=2*dot*hit.ny;x+=hit.nx*0.05;y+=hit.ny*0.05;last=hit.id||0;
      }
      return points;
    }
    exportSave(){
      const fields=['seed','time','nextId','wave','waveTime','health','energy','score','kills','merges','shots','combo','comboTime','bestCombo','clearRewarded','nextWaveIn','lastAim','board','enemies','projectiles','pendingShots','damageQueue','upgrades','offers','awaitingUpgrade','over','totalDamage','manualVolleys','escaped'];
      const state={schema:C.schema,deck:this.deck.slice(),rng:this.rng.state};
      for(const k of fields)state[k]=this[k];
      // A save may be requested immediately after a combat event, before end-of-tick cleanup.
      // Dead entities are not part of the next simulation state and may have negative HP/life.
      state.enemies=this.enemies.filter(e=>!e.dead);state.projectiles=this.projectiles.filter(p=>!p.dead);
      return JSON.parse(JSON.stringify(state));
    }
    static restore(data){
      if(!data||data.schema!==C.schema||!validDeck(data.deck)||!Array.isArray(data.board)||data.board.length!==B.slots)throw new TypeError('Invalid saved game.');
      const numeric={time:[0,1e9],nextId:[1,1e12],wave:[1,1e7],waveTime:[0,60],health:[0,R.maxHealth],energy:[0,1e7],score:[0,1e18],kills:[0,1e12],merges:[0,1e12],shots:[0,1e12],combo:[0,1e6],comboTime:[0,10],bestCombo:[0,1e9],nextWaveIn:[-2,10],lastAim:[-4,1],totalDamage:[0,1e30],manualVolleys:[0,1e12],escaped:[0,1e12]};
      for(const k in numeric)if(!finite(data[k],...numeric[k]))throw new TypeError('Invalid saved value: '+k);
      for(const d of data.board)if(d&&(!data.deck.includes(d.type)||!Number.isInteger(d.pips)||d.pips<1||d.pips>R.maxPips||!finite(d.cooldown,0,30)||!finite(d.id,1)||!finite(d.flash,0,2)))throw new TypeError('Invalid saved die.');
      if(!data.upgrades||typeof data.upgrades!=='object'||Array.isArray(data.upgrades))throw new TypeError('Invalid upgrades.');
      for(const key of Object.keys(data.upgrades))if(!Object.prototype.hasOwnProperty.call(UPG,key)||!Number.isInteger(data.upgrades[key])||data.upgrades[key]<0||data.upgrades[key]>1e6)throw new TypeError('Invalid upgrade.');
      if(!Array.isArray(data.enemies)||data.enemies.length>R.maxEnemies||!Array.isArray(data.projectiles)||data.projectiles.length>R.maxProjectiles||!Array.isArray(data.pendingShots)||data.pendingShots.length>MAX_PENDING||!Array.isArray(data.damageQueue)||data.damageQueue.length>50000)throw new TypeError('Invalid saved entities.');
      const statValid=s=>s&&['pulse','blast','arc','frost','split','bank'].includes(s.effect)&&['damage','count','reload','bounces','blastRadius','splashFactor','chainCount','chainRange','chainFactor','slowFactor','slowSeconds','childCount','childFactor','wallBoost','maxBoost'].every(k=>finite(s[k],0,1e18))&&typeof s.color==='string';
      for(const e of data.enemies)if(!finite(e.id,1)||!finite(e.x,-1000,1000)||!finite(e.y,-1e7,1000)||!finite(e.w,1,200)||!finite(e.h,1,200)||!finite(e.hp,0,1e16)||!finite(e.maxHp,1,1e16)||!finite(e.speed,0,100)||!finite(e.slowUntil,0)||!finite(e.slowFactor,0,1)||!finite(e.wave,1)||!['normal','volatile','armored','boss'].includes(e.kind))throw new TypeError('Invalid saved enemy.');
      for(const p of data.projectiles)if(!Object.prototype.hasOwnProperty.call(TYPES,p.type)||!finite(p.x,-100,1000)||!finite(p.y,-100,1000)||!finite(p.vx,-2000,2000)||!finite(p.vy,-2000,2000)||!finite(p.life,0,20)||!finite(p.bounces,0,40)||!finite(p.wallPower,1,4)||!statValid(p.stats)||!Array.isArray(p.trail)||p.trail.length>7||p.trail.some(t=>!finite(t.x,-100,1000)||!finite(t.y,-100,1000)))throw new TypeError('Invalid saved projectile.');
      for(const s of data.pendingShots)if(!finite(s.due,0,1e9)||!finite(s.angle,-Math.PI*2,Math.PI*2)||!s.snapshot||!Object.prototype.hasOwnProperty.call(TYPES,s.snapshot.type)||!statValid(s.snapshot.stats)||!Number.isInteger(s.snapshot.pips)||s.snapshot.pips<1||s.snapshot.pips>R.maxPips||(s.child&&(!finite(s.x,-100,1000)||!finite(s.y,-100,1000)||!finite(s.lastEnemy,0))))throw new TypeError('Invalid pending shot.');
      for(const d of data.damageQueue)if(!finite(d.id,1)||!finite(d.amount,0,1e18)||typeof d.color!=='string')throw new TypeError('Invalid secondary damage.');
      if(!Array.isArray(data.offers)||data.offers.length>3||data.offers.some(id=>!Object.prototype.hasOwnProperty.call(UPG,id))||typeof data.awaitingUpgrade!=='boolean'||(data.awaitingUpgrade&&data.offers.length!==3)||typeof data.over!=='boolean'||typeof data.clearRewarded!=='boolean'||!finite(data.rng,1,4294967295))throw new TypeError('Invalid saved transition.');
      const sim=new Simulation({deck:data.deck,seed:data.seed});
      const clean=JSON.parse(JSON.stringify(data));
      const excluded=new Set(['schema','deck','rng']);
      const expected=Object.keys(sim.exportSave());for(const k of expected)if(!excluded.has(k))sim[k]=clean[k];
      sim.rng.state=data.rng>>>0;sim.events=[];sim.grid.rebuild(sim.enemies);return sim;
    }
  }
  return{Simulation,Config,TYPES,UPG,slotPosition,validDeck};
});
