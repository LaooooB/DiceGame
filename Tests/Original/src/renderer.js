(function(root,factory){if(typeof module==='object'&&module.exports)module.exports=factory(require('./config.generated.js'),require('./math.js'),require('./simulation.js'));else root.DiceRenderer=factory(root.DiceConfig,root.DiceMath,root.DiceSim);})(typeof globalThis!=='undefined'?globalThis:this,function(Config,M,S){
  'use strict';
  const C=Config.game,A=C.arena,R=C.rules,W=C.view.width,H=C.view.height;
  const P={bg:'#0A1220',panel:'#121F30',line:'#26364A',muted:'#8394AC',text:'#EDF5F7',mint:'#72EAC8',gold:'#F8DE87',orange:'#FFAD76',pink:'#FF97B8'};
  const FONT='system-ui, -apple-system, "PingFang SC", "Microsoft YaHei", sans-serif';
  function rr(c,x,y,w,h,r=10){r=Math.max(0,Math.min(r,w/2,h/2));c.beginPath();c.moveTo(x+r,y);c.arcTo(x+w,y,x+w,y+h,r);c.arcTo(x+w,y+h,x,y+h,r);c.arcTo(x,y+h,x,y,r);c.arcTo(x,y,x+w,y,r);c.closePath();}
  function box(c,x,y,w,h,r,fill,stroke,width=1){rr(c,x,y,w,h,r);if(fill){c.fillStyle=fill;c.fill();}if(stroke){c.strokeStyle=stroke;c.lineWidth=width;c.stroke();}}
  function text(c,value,x,y,size=14,color=P.text,weight=500,align='left'){c.font=weight+' '+size+'px '+FONT;c.textAlign=align;c.textBaseline='middle';c.fillStyle=color;c.fillText(String(value),x,y);}
  function track(c,value,x,y,size=12,color=P.muted,spacing=3,align='center'){c.font='600 '+size+'px '+FONT;const letters=String(value).split('');const width=letters.reduce((sum,l)=>sum+c.measureText(l).width,0)+spacing*(letters.length-1);let xx=align==='center'?x-width/2:x;for(const l of letters){text(c,l,xx,y,size,color,600);xx+=c.measureText(l).width+spacing;}}
  function line(c,x,y,tx,ty,color,width=1){c.beginPath();c.moveTo(x,y);c.lineTo(tx,ty);c.strokeStyle=color;c.lineWidth=width;c.stroke();}
  function circle(c,x,y,r,fill,stroke,width=1){c.beginPath();c.arc(x,y,r,0,Math.PI*2);if(fill){c.fillStyle=fill;c.fill();}if(stroke){c.lineWidth=width;c.strokeStyle=stroke;c.stroke();}}
  function wrap(c,value,x,y,width,size=12,color=P.muted,lineHeight=19,maxLines=5){
    c.font='500 '+size+'px '+FONT;let row='',rows=[];
    for(const ch of value){if(ch==='\n'){rows.push(row);row='';continue;}if(c.measureText(row+ch).width>width&&row){rows.push(row);row=ch;}else row+=ch;}
    if(row)rows.push(row);rows=rows.slice(0,maxLines);rows.forEach((s,i)=>text(c,s,x,y+i*lineHeight,size,color));return rows.length*lineHeight;
  }
  function compact(n){if(n>=1e9)return(n/1e9).toFixed(1)+'B';if(n>=1e6)return(n/1e6).toFixed(1)+'M';if(n>=1e4)return(n/1e3).toFixed(1)+'k';return String(Math.ceil(n));}
  function icon(c,kind,x,y,size,color=P.text){
    c.save();c.translate(x,y);c.scale(size/24,size/24);c.strokeStyle=color;c.fillStyle=color;c.lineWidth=1.75;c.lineCap='round';c.lineJoin='round';c.beginPath();
    if(kind==='arc'||kind==='energy'){c.moveTo(2,-10);c.lineTo(-7,2);c.lineTo(0,2);c.lineTo(-2,10);c.lineTo(7,-2);c.lineTo(0,-2);c.closePath();c.fill();}
    else if(kind==='blast'){for(let k=0;k<12;k++){const a=k*Math.PI/6-Math.PI/2,r=k%2?5:10;const xx=Math.cos(a)*r,yy=Math.sin(a)*r;k?c.lineTo(xx,yy):c.moveTo(xx,yy);}c.closePath();c.stroke();circle(c,0,0,2,color);}
    else if(kind==='frost'){for(let i=0;i<3;i++){const a=i*Math.PI/3,xx=Math.cos(a)*10,yy=Math.sin(a)*10;line(c,-xx,-yy,xx,yy,color,1.6);}for(let i=0;i<6;i++){c.save();c.rotate(i*Math.PI/3);c.beginPath();c.moveTo(4,-3);c.lineTo(7,0);c.lineTo(4,3);c.stroke();c.restore();}}
    else if(kind==='split'){c.moveTo(0,9);c.lineTo(0,1);c.moveTo(-7,-8);c.lineTo(0,1);c.lineTo(7,-8);c.moveTo(-7,-3);c.lineTo(-7,-8);c.lineTo(-2,-8);c.moveTo(2,-8);c.lineTo(7,-8);c.lineTo(7,-3);c.stroke();}
    else if(kind==='bank'){c.moveTo(-9,7);c.lineTo(7,-7);c.lineTo(7,7);c.moveTo(7,-7);c.lineTo(1,-6);c.moveTo(7,-7);c.lineTo(8,-1);c.stroke();line(c,-9,-11,10,-11,color,2);}
    else if(kind==='pulse'){c.moveTo(-10,1);c.lineTo(-5,1);c.lineTo(-2,-8);c.lineTo(2,8);c.lineTo(5,-1);c.lineTo(10,-1);c.stroke();}
    else if(kind==='shield'){c.moveTo(0,-10);c.lineTo(8,-6);c.lineTo(7,4);c.quadraticCurveTo(4,9,0,11);c.quadraticCurveTo(-4,9,-7,4);c.lineTo(-8,-6);c.closePath();c.stroke();}
    else if(kind==='pause'){box(c,-7,-8,4,16,1,color);box(c,3,-8,4,16,1,color);}
    else if(kind==='play'){c.moveTo(-5,-9);c.lineTo(9,0);c.lineTo(-5,9);c.closePath();c.fill();}
    else if(kind==='sound'||kind==='muted'){c.moveTo(-9,-3);c.lineTo(-5,-3);c.lineTo(1,-8);c.lineTo(1,8);c.lineTo(-5,3);c.lineTo(-9,3);c.closePath();c.stroke();if(kind==='sound'){c.beginPath();c.arc(0,0,7,-0.9,0.9);c.stroke();c.beginPath();c.arc(0,0,11,-0.75,0.75);c.stroke();}else{line(c,5,-4,11,4,color,1.5);line(c,11,-4,5,4,color,1.5);}}
    else if(kind==='close'){line(c,-6,-6,6,6,color,2);line(c,6,-6,-6,6,color,2);}
    else if(kind==='back'){c.moveTo(3,-8);c.lineTo(-5,0);c.lineTo(3,8);c.stroke();}
    else if(kind==='check'){c.moveTo(-7,0);c.lineTo(-2,5);c.lineTo(8,-6);c.stroke();}
    else if(kind==='plus'){line(c,-7,0,7,0,color,2);line(c,0,-7,0,7,color,2);}
    else if(kind==='aim'){circle(c,0,0,7,null,color,1.5);circle(c,0,0,2,color);line(c,-11,0,-7,0,color,1.5);line(c,7,0,11,0,color,1.5);line(c,0,-11,0,-7,color,1.5);line(c,0,7,0,11,color,1.5);}
    else if(kind==='merge'){box(c,-10,-7,10,10,2,null,color,1.4);box(c,0,-1,10,10,2,null,color,1.4);line(c,-3,-11,5,-11,color,1.5);}
    else if(kind==='reload'){c.arc(0,0,8,-1,4);c.stroke();c.beginPath();c.moveTo(-8,-6);c.lineTo(-6,0);c.lineTo(-1,-4);c.stroke();}
    else if(kind==='info'){circle(c,0,0,10,null,color,1.4);circle(c,0,-4,1.2,color);line(c,0,0,0,5,color,2);}
    c.restore();
  }
  const DOTS=[[],[[0,0]],[[-1,-1],[1,1]],[[-1,-1],[0,0],[1,1]],[[-1,-1],[1,-1],[-1,1],[1,1]],[[-1,-1],[1,-1],[0,0],[-1,1],[1,1]],[[-1,-1],[1,-1],[-1,0],[1,0],[-1,1],[1,1]]];
  function dieFace(c,type,pips,x,y,size,angle=0,alpha=1){
    c.save();c.globalAlpha*=alpha;c.translate(x,y);c.rotate(angle);const s=size,half=s/2,t=S.TYPES[type]||Config.dice[0];
    box(c,-half-1,-half+5,s+2,s,Math.max(8,s*0.18),'#050C15');
    box(c,-half,-half+3,s,s,Math.max(8,s*0.18),t.shade);
    const gradient=c.createLinearGradient(-half,-half,half,half);gradient.addColorStop(0,t.color);gradient.addColorStop(0.48,t.color);gradient.addColorStop(1,t.shade);
    box(c,-half,-half,s,s,Math.max(8,s*0.18),gradient,t.color,1);
    c.save();rr(c,-half+1,-half+1,s-2,s-2,Math.max(8,s*0.18));c.clip();
    const gloss=c.createLinearGradient(0,-half,0,half);gloss.addColorStop(0,'rgba(255,255,255,.32)');gloss.addColorStop(0.5,'rgba(255,255,255,.03)');gloss.addColorStop(1,'rgba(0,0,0,.04)');c.fillStyle=gloss;c.fillRect(-half,-half,s,s);
    c.beginPath();c.moveTo(-half,-half);c.lineTo(half,-half);c.lineTo(-half,half*0.25);c.closePath();c.fillStyle='rgba(255,255,255,.09)';c.fill();c.restore();
    box(c,-half+2,-half+2,s-4,s-4,Math.max(7,s*0.15),null,'rgba(255,255,255,.34)',0.8);
    const d=s*0.205,r=s*0.060;
    for(const [dx,dy]of DOTS[M.clamp(pips,1,6)]){circle(c,dx*d,dy*d+1,r+0.45,'rgba(0,0,0,.15)');circle(c,dx*d,dy*d,r,'#F6FFFC');circle(c,dx*d-r*0.18,dy*d-r*0.22,r*0.42,'#FFFFFF');}
    c.restore();
  }
  class Renderer{
    constructor(canvas,platform={}){
      this.canvas=canvas;this.c=canvas.getContext('2d');this.platform=platform;this.width=W;this.height=H;this.scale=1;this.ox=0;this.oy=0;this.dpr=1;
      this.buttons=[];this.particles=[];this.rings=[];this.floaters=[];this.arcs=[];this.conduits=[];this.pulses=[];this.banner=null;
      this.t=0;this.fxTime=0;this.shake=0;this.flash=0;this.glows={};this.reduceMotion=false;this.rng=new M.RNG(924153);this.hitGate=new Map();
    }
    resize(width,height,dpr=1,safeTop=0,safeBottom=0){
      this.width=Math.max(1,width);this.height=Math.max(1,height);this.dpr=Math.min(2.5,Math.max(1,dpr||1));
      this.canvas.width=Math.round(this.width*this.dpr);this.canvas.height=Math.round(this.height*this.dpr);
      this.scale=Math.min(this.width/W,(this.height-safeTop-safeBottom)/H);this.ox=(this.width-W*this.scale)/2;this.oy=safeTop+(this.height-safeTop-safeBottom-H*this.scale)/2;
    }
    toPoint(x,y){return{x:(x-this.ox)/this.scale,y:(y-this.oy)/this.scale};}
    glow(color){
      if(this.glows[color])return this.glows[color];
      if(!this.platform.createCanvas)return null;
      const cv=this.platform.createCanvas(48,48);cv.width=48;cv.height=48;const c=cv.getContext('2d');const g=c.createRadialGradient(24,24,0,24,24,24);g.addColorStop(0,color);g.addColorStop(0.14,color+'CC');g.addColorStop(0.42,color+'33');g.addColorStop(1,color+'00');c.fillStyle=g;c.fillRect(0,0,48,48);this.glows[color]=cv;return cv;
    }
    addParticle(x,y,color,count=10,power=100){
      count=Math.floor(count*(this.reduceMotion?0.45:1));
      for(let i=0;i<count;i++){
        if(this.particles.length>=C.limits.particles)this.particles.shift();
        const angle=this.rng.next()*Math.PI*2,speed=(0.35+this.rng.next()*0.8)*power,life=0.3+this.rng.next()*0.38;
        this.particles.push({x,y,vx:Math.cos(angle)*speed,vy:Math.sin(angle)*speed,life,max:life,color,size:1.8+this.rng.next()*3,angle,shape:i%3});
      }
    }
    addRing(x,y,radius,color,life=0.4){if(this.rings.length>=C.limits.rings)this.rings.shift();this.rings.push({x,y,radius,color,life,max:life});}
    floater(label,x,y,color,size=14,life=0.7){if(this.floaters.length>=C.limits.floaters)this.floaters.shift();this.floaters.push({label,x,y,color,size,life,max:life});}
    onEvent(e){
      if(e.type==='hit'){
        this.addParticle(e.x,e.y,e.color,3,48);const key=Math.floor(e.x/16)+':'+Math.floor(e.y/16);
        if(this.fxTime-(this.hitGate.get(key)||-100)>0.11){this.hitGate.set(key,this.fxTime);this.floater(compact(e.amount),e.x+(this.rng.next()-.5)*15,e.y-12,e.color,e.amount>50?16:12,0.5);}
      }else if(e.type==='kill'){
        this.addParticle(e.x,e.y,e.color,e.kind==='boss'?38:14,e.kind==='boss'?230:115);this.addRing(e.x,e.y,e.w*0.85,e.color,0.34);
        if(e.combo%3===0)this.floater('+'+e.reward+' 能量',e.x,e.y+10,P.gold,11,0.9);
        if(e.combo>=5)this.shake=Math.max(this.shake,1.0);if(e.kind==='boss'){this.shake=4;this.banner={label:'首领击破',sub:'BOSS ELIMINATED',life:2,max:2,color:P.gold};}
      }else if(e.type==='explosion'){
        this.addRing(e.x,e.y,e.radius,e.color,0.38);this.addRing(e.x,e.y,e.radius*0.6,'#FFF7E9',0.23);this.addParticle(e.x,e.y,e.color,e.volatile?20:9,e.radius*2.4);this.shake=Math.max(this.shake,e.volatile?3:1.3);
      }else if(e.type==='arc'){
        if(this.arcs.length>=C.limits.arcs)this.arcs.shift();
        const points=[{x:e.x,y:e.y}];for(let i=1;i<5;i++)points.push({x:M.lerp(e.x,e.tx,i/5)+(this.rng.next()-.5)*16,y:M.lerp(e.y,e.ty,i/5)+(this.rng.next()-.5)*16});points.push({x:e.tx,y:e.ty});this.arcs.push({points,color:e.color,life:0.19,max:0.19});
      }else if(e.type==='wall'){this.addParticle(e.x,e.y,e.color,e.boost?6:2,55);this.addRing(e.x,e.y,e.boost?20:11,e.color,0.19);}
      else if(e.type==='split'){this.addRing(e.x,e.y,26,e.color,0.25);this.addParticle(e.x,e.y,e.color,8,85);}
      else if(e.type==='launch'){this.addRing(e.x,e.y,e.surge?19:11,e.color,0.12);}
      else if(e.type==='conduit'){this.conduits.push({slot:e.slot,color:e.color,life:0.30,max:0.30,surge:e.surge});}
      else if(e.type==='summon'){this.pulses.push({slot:e.slot,color:S.TYPES[e.die.type].color,life:0.65,max:0.65});}
      else if(e.type==='merge'){
        this.pulses.push({slot:e.b,color:S.TYPES[e.die.type].color,life:0.90,max:0.90,merge:true,pips:e.die.pips});this.shake=2.8;
      }else if(e.type==='breach'){this.addRing(e.x,e.y,62,P.pink,0.5);this.addParticle(e.x,e.y,P.pink,18,150);this.flash=0.18;this.shake=4;}
      else if(e.type==='wave'){this.banner={label:e.boss?'首领压境':'第 '+String(e.wave).padStart(2,'0')+' 波',sub:e.boss?'BREAK THE WARDEN':'ENDLESS CORRIDOR',life:e.boss?1.9:1.25,max:e.boss?1.9:1.25,color:e.boss?P.pink:P.mint};}
      else if(e.type==='clear'){this.banner={label:'漂亮，清场。',sub:'+'+e.reward+' 能量  /  下一波准备',life:1.2,max:1.2,color:P.gold};}
      else if(e.type==='upgraded'){this.flash=0.07;}
    }
    update(dt,active=true){
      this.t+=dt;if(!active)return;this.fxTime+=dt;
      for(const p of this.particles){p.life-=dt;p.x+=p.vx*dt;p.y+=p.vy*dt;p.vx*=Math.exp(-dt*2.3);p.vy+=80*dt;p.angle+=dt*3;}
      this.particles=this.particles.filter(p=>p.life>0);
      for(const a of [this.rings,this.floaters,this.arcs,this.conduits,this.pulses]){for(const x of a)x.life-=dt;}
      this.rings=this.rings.filter(x=>x.life>0);this.floaters=this.floaters.filter(x=>x.life>0);this.arcs=this.arcs.filter(x=>x.life>0);this.conduits=this.conduits.filter(x=>x.life>0);this.pulses=this.pulses.filter(x=>x.life>0);
      if(this.banner){this.banner.life-=dt;if(this.banner.life<=0)this.banner=null;}
      this.shake=Math.max(0,this.shake-dt*12);this.flash=Math.max(0,this.flash-dt);if(this.hitGate.size>200)this.hitGate.clear();
    }
    button(id,x,y,w,h,label,style='default',options={}){
      const c=this.c,disabled=!!options.disabled;this.buttons.push({id,x,y,w,h,disabled});
      const pressed=this.app&&this.app.pointer&&this.app.pointer.button===id;
      const yy=y+(pressed?1.5:0),fill=style==='primary'?P.mint:style==='danger'?'#442635':style==='ghost'?'rgba(22,36,53,.6)':'#1A2A3D';
      c.save();if(disabled)c.globalAlpha=0.5;
      if(style==='primary'){box(c,x,yy+4,w,h,14,'#245F57');const g=c.createLinearGradient(x,yy,x+w,yy+h);g.addColorStop(0,'#9EF7DB');g.addColorStop(1,'#5ECBAE');box(c,x,yy,w,h,14,g,'#BCFFE8',0.8);}
      else box(c,x,yy,w,h,options.radius||12,fill,style==='danger'?'#83445B':P.line,1);
      if(options.icon)icon(c,options.icon,x+(options.iconOnly?w/2:24),yy+h/2,options.iconSize||20,style==='primary'?'#102C2A':(options.color||P.text));
      if(label)text(c,label,options.align==='left'?x+20:x+w/2,yy+h/2,options.fontSize||14,style==='primary'?'#102C2A':options.color||P.text,650,options.align==='left'?'left':'center');
      c.restore();
    }
    background(){
      const c=this.c;const g=c.createLinearGradient(0,0,W,H);g.addColorStop(0,'#101C2B');g.addColorStop(0.46,'#0B1422');g.addColorStop(1,'#101B2C');c.fillStyle=g;c.fillRect(0,0,W,H);
      const glow=c.createRadialGradient(380,40,0,380,40,330);glow.addColorStop(0,'rgba(83,159,152,.09)');glow.addColorStop(1,'rgba(40,80,100,0)');c.fillStyle=glow;c.fillRect(0,0,W,H);
      for(let y=18;y<H;y+=24)for(let x=15;x<W;x+=24){c.fillStyle='rgba(163,198,216,.035)';c.fillRect(x,y,1,1);}
    }
    render(app){
      this.app=app;this.reduceMotion=app.settings.reduceMotion;const c=this.c;
      c.setTransform(1,0,0,1,0,0);c.fillStyle='#070E18';c.fillRect(0,0,this.canvas.width,this.canvas.height);
      c.setTransform(this.dpr*this.scale,0,0,this.dpr*this.scale,this.dpr*this.ox,this.dpr*this.oy);this.buttons=[];this.background();
      if(app.scene==='menu'||(app.scene==='help'&&app.helpReturn==='menu'))this.menu(app);
      else if(app.scene==='deck')this.deck(app);
      else if(app.sim)this.game(app);
      else this.menu(app);
      if(app.scene==='paused')this.pause(app);
      else if(app.scene==='upgrade')this.upgrade(app);
      else if(app.scene==='over')this.gameOver(app);
      else if(app.scene==='die')this.dieInfo(app);
      else if(app.scene==='help')this.help(app);
      else if(app.scene==='confirmNew')this.confirmNew(app);
      if(app.toast&&app.toast.life>0){
        const a=Math.min(1,app.toast.life*4);c.save();c.globalAlpha=a;c.font='600 12px '+FONT;const tw=Math.min(380,c.measureText(app.toast.text).width+36);box(c,(W-tw)/2,555,tw,33,12,'#213448','#537387',1);text(c,app.toast.text,W/2,571.5,12,'#F1FCFF',600,'center');c.restore();
      }
    }
    menu(app){
      const c=this.c,t=this.t;track(c,'A LITTLE CHAOS. A LOT OF IMPACT.',216,40,9,P.muted,1.5);
      this.button('sound',369,64,34,34,'','ghost',{icon:app.settings.sound?'sound':'muted',iconOnly:true,iconSize:17});
      box(c,29,71,117,25,12,'#1D302F','#365750',0.8);circle(c,42,83.5,2.5,P.mint);text(c,'单指 · 无尽弹射',53,83.5,10,P.mint,600);
      text(c,'骰子回响',216,156,52,P.text,800,'center');track(c,'D I C E   R I C O C H E T',216,205,12,P.mint,1.2);
      text(c,'合出火力，让每一次碰撞都有回响。',216,236,12,P.muted,500,'center');
      c.save();
      const g=c.createRadialGradient(216,381,16,216,381,169);g.addColorStop(0,'rgba(75,188,165,.15)');g.addColorStop(1,'rgba(40,80,100,0)');c.fillStyle=g;c.fillRect(24,260,384,257);
      c.strokeStyle='#33535C';c.lineWidth=1;c.setLineDash([3,8]);c.beginPath();c.moveTo(58,420);c.lineTo(372,292);c.lineTo(372,444);c.lineTo(68,321);c.stroke();c.setLineDash([]);
      const flow=(t*0.24)%1;const pts=[{x:58,y:420},{x:372,y:292},{x:372,y:444},{x:68,y:321}];const pathIndex=Math.min(2,Math.floor(flow*3)),f=flow*3-pathIndex;const p={x:M.lerp(pts[pathIndex].x,pts[pathIndex+1].x,f),y:M.lerp(pts[pathIndex].y,pts[pathIndex+1].y,f)};
      const sp=this.glow(P.gold);if(sp)c.drawImage(sp,p.x-18,p.y-18,36,36);circle(c,p.x,p.y,3.8,'#FFFFFF');
      c.save();c.translate(216,470);c.scale(1,0.25);circle(c,0,0,118,null,'#315B5D',1.8);circle(c,0,0,84,null,'rgba(114,234,200,.18)',2);c.restore();
      dieFace(c,'arc',2,95,390+Math.sin(t*1.2)*5,53,-0.25);
      dieFace(c,'blast',4,330,318+Math.cos(t)*6,64,0.23);
      dieFace(c,'pulse',5,215,371+Math.sin(t*1.4)*6,125,-0.13);
      circle(c,342,441,4,P.gold);circle(c,84,290,2.5,P.mint);circle(c,153,299,2,'#9EAED0');
      for(let i=0;i<6;i++){const a=t*0.14+i*Math.PI/3;circle(c,216+Math.cos(a)*145,376+Math.sin(a)*94,1.6,'#547F84');}
      c.restore();
      const steps=[['aim','按住瞄准'],['play','松手齐射'],['merge','同种同点合成']];steps.forEach((s,i)=>{const x=81+i*135;icon(c,s[0],x,520,20,i===1?P.gold:P.mint);text(c,s[1],x,548,12,P.text,550,'center');});
      line(c,144,511,144,548,P.line);line(c,280,511,280,548,P.line);
      text(c,'本局卡组',32,597,13,P.text,650);text(c,app.deck.length+' / 6',102,597,11,P.muted);this.button('editDeck',331,581,69,29,'配置','ghost',{fontSize:11});
      const n=app.deck.length,step=Math.min(64,365/n),start=216-(n-1)*step/2;
      app.deck.forEach((id,i)=>{const x=start+i*step;dieFace(c,id,1,x,645,41,0);text(c,S.TYPES[id].name,x,681,10,P.muted,600,'center');});
      if(app.resumeData){
        this.button('resume',32,715,368,55,'继续战斗 · 第 '+app.resumeData.wave+' 波','primary');
        this.button('new',32,785,236,39,'重新开始','ghost',{fontSize:12});this.button('help',280,785,120,39,'玩法说明','ghost',{fontSize:12});
      }else{
        this.button('start',32,723,368,59,'进入回廊','primary',{fontSize:18});icon(c,'play',355,752,19,'#123A32');
        this.button('help',145,805,142,30,'玩法说明','ghost',{fontSize:11});
      }
      if(app.meta.bestWave>0)text(c,'最佳纪录  /  第 '+app.meta.bestWave+' 波',216,705,10,P.gold,550,'center');
    }
    header(app){
      const c=this.c,s=app.sim;dieFace(c,'pulse',4,34,33,22,-0.13);text(c,'骰子回响',54,31,17,P.text,750);track(c,'DICE RICOCHET',55,51,8,P.muted,1.6,'left');
      this.button('sound',327,20,35,35,'','ghost',{icon:app.settings.sound?'sound':'muted',iconOnly:true,iconSize:17});
      this.button('pause',373,20,35,35,'','ghost',{icon:'pause',iconOnly:true,iconSize:17});
      text(c,String(s.wave).padStart(2,'0'),28,89,32,P.text,750);text(c,'WAVE',81,79,9,P.muted,700);text(c,s.wave%5===0?'首领波次':'无尽回廊',81,97,10,s.wave%5===0?P.pink:P.muted,550);
      icon(c,'shield',180,87,21,s.health<=4?P.pink:P.mint);text(c,s.health,199,87,22,s.health<=4?P.pink:P.text,700);text(c,'/ '+R.maxHealth,228,91,11,P.muted,500);
      for(let i=0;i<R.maxHealth;i++)box(c,170+i*7,108,4.5,2.5,1,i<s.health?(s.health<=4?P.pink:P.mint):'#2B394A');
      icon(c,'energy',313,84,20,P.gold);text(c,Math.floor(s.energy),398,83,26,P.gold,700,'right');text(c,'召唤能量',398,105,10,P.muted,500,'right');
    }
    game(app){
      const c=this.c,s=app.sim;this.header(app);
      box(c,16,122,400,427,19,'#07101C','#28394B',1);box(c,21,127,390,417,15,null,'#162D3A',1);
      c.save();rr(c,25,132,382,405,10);c.clip();
      if(this.shake>0&&!this.reduceMotion)c.translate(Math.sin(this.t*71)*this.shake,Math.cos(this.t*83)*this.shake*.6);
      const field=c.createLinearGradient(0,136,0,534);field.addColorStop(0,'#0D1725');field.addColorStop(1,'#0C1B29');c.fillStyle=field;c.fillRect(A.left,A.top,A.right-A.left,410);
      for(let y=137;y<535;y+=25)line(c,28,y,404,y,'rgba(102,141,164,.055)');for(let x=28;x<=404;x+=25)line(c,x,136,x,535,'rgba(102,141,164,.055)');
      for(let i=0;i<22;i++){const x=42+(i*71)%352,y=146+((i*53+this.t*(2+i%3))%370);circle(c,x,y,i%3===0?1.1:0.7,'rgba(123,197,200,.16)');}
      c.fillStyle='#203948';c.fillRect(27,136,3,399);c.fillRect(402,136,3,399);
      const wall=c.createLinearGradient(0,136,0,535);wall.addColorStop(0,'rgba(114,234,200,.13)');wall.addColorStop(.6,'rgba(114,234,200,.5)');wall.addColorStop(1,'rgba(114,234,200,.85)');c.fillStyle=wall;c.fillRect(28,136,1.5,399);c.fillRect(402.5,136,1.5,399);
      for(let y=153;y<510;y+=34){line(c,30,y,34,y,'#496E78',1);line(c,398,y,402,y,'#496E78',1);}
      for(const e of s.enemies)this.enemy(e,s.time);
      if(app.pointer&&app.pointer.mode==='aim')this.aim(app);
      this.drawProjectiles(s.projectiles);this.drawFX();
      const danger=s.health<=4;const shield= danger?P.pink:P.mint;
      const sg=c.createLinearGradient(0,487,0,521);sg.addColorStop(0,shield+'00');sg.addColorStop(1,shield+'15');c.fillStyle=sg;c.fillRect(31,487,370,35);
      line(c,34,519,398,519,shield+'77',1.2);
      for(let x=42;x<400;x+=24){line(c,x,526,x+7,526,shield+'44',1);}
      if(s.combo>=3&&s.comboTime>0){const alpha=Math.min(1,s.comboTime);c.save();c.globalAlpha=alpha;track(c,'CHAIN',55,449,8,P.gold,2,'left');text(c,String(s.combo).padStart(2,'0'),54,479,38,P.gold,800);text(c,'连锁',55,506,11,P.gold,600);c.restore();}
      if(this.banner){const b=this.banner,a=Math.min(1,(b.max-b.life)*8,b.life*5);c.save();c.globalAlpha=Math.max(0,a);const bg=c.createLinearGradient(64,0,368,0);bg.addColorStop(0,'#0A142000');bg.addColorStop(0.25,'#0A1420E8');bg.addColorStop(0.75,'#0A1420E8');bg.addColorStop(1,'#0A142000');c.fillStyle=bg;c.fillRect(64,287,304,74);text(c,b.label,216,311,26,b.color,750,'center');track(c,b.sub,216,342,9,P.muted,1,'center');c.restore();}
      if(this.flash>0&&!this.reduceMotion){c.fillStyle='rgba(255,125,159,'+(this.flash*.8)+')';c.fillRect(28,136,376,400);}
      c.restore();
      this.launcher(app);
      const aiming=app.pointer&&app.pointer.mode==='aim',cancel=aiming&&app.pointer.y>575;
      if(aiming)text(c,cancel?'松手取消 · 移回战场继续瞄准':'松手齐射  /  下滑到骰子区取消',216,568,11,cancel?P.pink:P.mint,600,'center');
      else text(c,s.count()?'按住战场瞄准  ·  松手齐射':'召唤骰子，然后按住战场瞄准',216,568,11,P.muted,550,'center');
      this.board(app);
      for(const q of this.conduits){const p=1-q.life/q.max,from=S.slotPosition(q.slot),x=M.lerp(from.x,216,p),y=M.lerp(from.y-25,530,p*p);c.save();c.globalAlpha=(1-p)*.8;line(c,from.x,from.y-25,x,y,q.color,1.4);const glow=this.glow(q.color);if(glow)c.drawImage(glow,x-14,y-14,28,28);circle(c,x,y,2.5,q.color);c.restore();}
    }
    enemy(e,time){
      if(e.dead||e.y+e.h/2<A.top-4||e.y-e.h/2>A.bottom)return;
      const c=this.c,iced=time<e.slowUntil,color=iced?'#87D6FF':e.kind==='volatile'?P.orange:e.kind==='boss'?P.pink:e.kind==='armored'?'#C6B4EE':'#E5969F';
      const x=e.x-e.w/2,y=e.y-e.h/2;
      box(c,x,y+4,e.w,e.h,7,'rgba(0,0,0,.35)');
      const g=c.createLinearGradient(x,y,x+e.w,y+e.h);g.addColorStop(0,color+'36');g.addColorStop(1,color+'10');box(c,x,y,e.w,e.h,7,g,color+'AC',1.3);
      line(c,x+8,y+3,x+e.w-8,y+3,color+'5A',1);
      if(e.kind==='armored')box(c,x+3,y+3,e.w-6,e.h-6,5,null,color+'45',1);
      if(e.kind==='boss'){track(c,'WARDEN',e.x,y+12,7,color,1.5);icon(c,'shield',x+12,e.y+5,10,color);text(c,compact(Math.max(0,e.hp)),e.x+5,e.y+6,23,P.text,750,'center');}
      else{if(e.kind==='volatile')icon(c,'blast',x+7,y+7,7,color);else if(iced)icon(c,'frost',x+7,y+7,7,color);text(c,compact(Math.max(0,e.hp)),e.x,e.y+1,e.hp>=1000?13:18,P.text,650,'center');}
      box(c,x+7,y+e.h-6,e.w-14,2,1,'#263345');box(c,x+7,y+e.h-6,(e.w-14)*M.clamp(e.hp/e.maxHp,0,1),2,1,color);
      if(e.flash>0){c.save();c.globalAlpha=e.flash*.35;box(c,x,y,e.w,e.h,7,'#FFFFFF');c.restore();}
      if(iced){c.save();c.globalAlpha=0.5;icon(c,'frost',x+e.w-5,y+3,10,'#C5EDFF');c.restore();}
    }
    launcher(app){
      const c=this.c,s=app.sim,a=app.pointer&&app.pointer.mode==='aim'?app.aimAngle:s.lastAim,ready=s.readyCount();
      circle(c,216,533,19,'#101D2B','#305C62',1.3);circle(c,216,533,15,null,ready?P.mint+'AA':'#526576',1.8);
      c.save();c.translate(216,533);c.rotate(a+Math.PI/2);box(c,-5,-22,10,22,4,'#45666B','#85C9B7',1);box(c,-3,-24,6,7,2,P.mint);c.restore();
      circle(c,216,533,8,'#1D4544','#8CCBBB',1);circle(c,216,533,3,P.mint);
      text(c,'就绪',51,543,9,P.muted,500);text(c,ready+' / '+s.count(),79,543,11,ready?P.mint:P.muted,650);
      text(c,'得分 '+compact(s.score),382,543,10,P.muted,500,'right');
    }
    aim(app){
      if(app.pointer.y>575)return;const c=this.c,points=app.sim.traceAim(app.aimAngle);c.save();
      for(let i=1;i<points.length;i++){
        c.globalAlpha=Math.max(.15,.72-i*.12);c.strokeStyle=P.mint;c.lineWidth=i===1?1.7:1.2;c.setLineDash([4,7]);c.lineDashOffset=-this.t*30;c.beginPath();c.moveTo(points[i-1].x,points[i-1].y);c.lineTo(points[i].x,points[i].y);c.stroke();c.setLineDash([]);
        if(points[i].enemy)circle(c,points[i].x,points[i].y,7,null,P.gold,1.5);else circle(c,points[i].x,points[i].y,3,P.mint);
      }
      const pointer=app.pointer;c.globalAlpha=.65;icon(c,'aim',M.clamp(pointer.x,39,393),M.clamp(pointer.y,149,506),23,P.mint);c.restore();
    }
    drawProjectiles(list){
      const c=this.c;c.save();c.lineCap='round';c.globalCompositeOperation='lighter';
      for(const p of list){
        const color=p.stats.color;if(p.trail.length>1){c.globalAlpha=p.child ? 0.32 : 0.50;c.beginPath();c.moveTo(p.trail[0].x,p.trail[0].y);for(let i=1;i<p.trail.length;i++)c.lineTo(p.trail[i].x,p.trail[i].y);c.lineTo(p.x,p.y);c.lineWidth=p.child?1.5:2.6;c.strokeStyle=color;c.stroke();}
        c.globalAlpha=p.child ? 0.65 : 1;const glow=this.glow(color);const size=p.surge?34:p.child?18:25;if(glow)c.drawImage(glow,p.x-size/2,p.y-size/2,size,size);
        circle(c,p.x,p.y,p.child?2:3.5,color);circle(c,p.x,p.y,p.child?1:1.8,'#FFFFFF');
        if(p.stats.effect==='bank'&&p.wallPower>1)circle(c,p.x,p.y,6,null,color,1);
      }c.restore();
    }
    drawFX(){
      const c=this.c;c.save();
      for(const ring of this.rings){const p=1-ring.life/ring.max;c.globalAlpha=(1-p)*.7;circle(c,ring.x,ring.y,Math.max(1,ring.radius*(.12+p*.88)),null,ring.color,Math.max(.5,(1-p)*2.5));}
      for(const a of this.arcs){c.globalAlpha=Math.min(1,a.life*10);c.beginPath();c.moveTo(a.points[0].x,a.points[0].y);for(let i=1;i<a.points.length;i++)c.lineTo(a.points[i].x,a.points[i].y);c.strokeStyle=a.color;c.lineWidth=3;c.stroke();c.strokeStyle='#F4EEFF';c.lineWidth=1;c.stroke();}
      for(const p of this.particles){c.globalAlpha=Math.min(1,p.life/p.max*1.4);c.save();c.translate(p.x,p.y);c.rotate(p.angle);c.fillStyle=p.color;if(p.shape===0)c.fillRect(-p.size/2,-p.size/2,p.size,p.size);else if(p.shape===1){c.beginPath();c.moveTo(0,-p.size);c.lineTo(p.size*.6,p.size*.5);c.lineTo(-p.size*.6,p.size*.5);c.fill();}else{c.fillRect(-p.size,-.6,p.size*2,1.2);}c.restore();}
      for(const f of this.floaters){const p=1-f.life/f.max;c.globalAlpha=Math.min(1,f.life*4);text(c,f.label,f.x,f.y-p*28,f.size,f.color,700,'center');}
      c.restore();
    }
    board(app){
      const c=this.c,s=app.sim;const dragging=app.pointer&&app.pointer.mode==='drag',from=dragging?app.pointer.slot:-1;
      text(c,'骰子阵地',30,590,13,P.text,650);text(c,s.count()+' / 8',107,590,11,P.muted,600);
      text(c,s.hasPair()?'有可合成的骰子':'同种类 + 同点数 才可合成',402,590,10,s.hasPair()?P.mint:P.muted,550,'right');
      for(let i=0;i<8;i++){
        const pos=S.slotPosition(i),d=s.board[i],match=dragging&&s.canMerge(from,i),fade=dragging&&from===i;
        c.save();if(fade)c.globalAlpha=.27;
        box(c,pos.x-41,pos.y-42,82,86,13,d?'#152235':'#101B2B',match?P.mint:'#2A3B50',match?1.8:1);
        if(d){
          if(match){c.save();c.globalAlpha=.10+Math.sin(this.t*8)*.04;box(c,pos.x-40,pos.y-41,80,84,12,P.mint);c.restore();}
          let scale=1;const pulse=this.pulses.find(p=>p.slot===i);if(pulse){const progress=1-pulse.life/pulse.max;scale=1+Math.sin(progress*Math.PI*2.3)*.10*(1-progress);}
          dieFace(c,d.type,d.pips,pos.x,pos.y-8,49*scale,0);
          text(c,S.TYPES[d.type].name+' · '+d.pips,pos.x,pos.y+29,10,S.TYPES[d.type].color,600,'center');
          const progress=1-M.clamp(d.cooldown/s.stats(d).reload,0,1);
          box(c,pos.x-27,pos.y+38,54,2,1,'#2B3D50');box(c,pos.x-27,pos.y+38,54*progress,2,1,S.TYPES[d.type].color);
          circle(c,pos.x+31,pos.y-32,2.2,progress>=.999?P.mint:'#586278');
          icon(c,S.TYPES[d.type].icon,pos.x-31,pos.y-32,8,S.TYPES[d.type].color);
        }else{
          c.save();c.setLineDash([2,4]);box(c,pos.x-24,pos.y-28,48,47,9,null,'#2C4155',1);c.restore();icon(c,'plus',pos.x,pos.y-6,19,'#3C566C');text(c,'空位',pos.x,pos.y+28,10,'#567087',500,'center');
        }c.restore();
      }
      for(const p of this.pulses){const pos=S.slotPosition(p.slot),progress=1-p.life/p.max;c.save();c.globalAlpha=Math.max(0,1-progress);circle(c,pos.x,pos.y-8,28+progress*32,null,p.color,2);if(p.merge)text(c,'合成！'+p.pips+' 点',pos.x,pos.y-51-progress*16,12,p.color,750,'center');c.restore();}
      text(c,'点按查看 · 拖动合成 · 不同类型不能合成',216,789,10,P.muted,500,'center');
      const affordable=s.energy>=R.summonCost,full=s.count()===8;
      this.button('summon',28,807,270,42,'','primary',{disabled:!affordable||full});
      icon(c,'plus',50,828,15,'#18433A');text(c,full?'阵地已满':'召唤骰子',72,828,15,'#123A32',700);icon(c,'energy',246,828,16,'#1B594A');text(c,R.summonCost,279,828,16,'#123A32',750,'right');
      this.button('help',310,807,94,42,'说明','ghost',{fontSize:12});
      if(dragging&&s.board[from]){
        const p=app.pointer,d=s.board[from];c.save();c.globalAlpha=.93;dieFace(c,d.type,d.pips,p.x,p.y-18,60,-.06);c.restore();
      }
    }
    dim(){const c=this.c;c.fillStyle='rgba(3,9,16,.82)';c.fillRect(0,0,W,H);this.buttons=[];}
    modal(x,y,w,h){box(this.c,x,y+7,w,h,24,'rgba(0,0,0,.25)');const g=this.c.createLinearGradient(x,y,x+w,y+h);g.addColorStop(0,'#1B2B3E');g.addColorStop(1,'#111D2E');box(this.c,x,y,w,h,24,g,'#344C60',1);}
    pause(app){
      const c=this.c;this.dim();this.modal(30,201,372,478);track(c,'TAKE A BREATH',216,238,9,P.mint,2);text(c,'战斗暂停',216,279,29,P.text,750,'center');text(c,'进度已自动保存',216,312,12,P.muted,500,'center');
      this.button('continue',57,345,318,51,'继续战斗','primary',{fontSize:16});
      this.button('sound',57,413,152,45,'音效  '+(app.settings.sound?'开':'关'),'ghost',{fontSize:12});this.button('music',223,413,152,45,'配乐  '+(app.settings.music?'开':'关'),'ghost',{fontSize:12});
      this.button('motion',57,474,318,43,'减弱闪光与震动  '+(app.settings.reduceMotion?'开':'关'),'ghost',{fontSize:12});
      this.button('home',57,546,318,46,'保存并返回主界面','ghost',{fontSize:13});text(c,'按 Esc 也可以继续',216,636,10,P.muted,500,'center');
    }
    upgrade(app){
      const c=this.c;this.dim();track(c,'CHOOSE YOUR ECHO',216,157,10,P.mint,2.8);text(c,'回响强化',216,202,34,P.text,750,'center');text(c,'选择一项，本局所有同类骰子持续受益。',216,238,12,P.muted,500,'center');
      app.sim.offers.forEach((id,i)=>{
        const u=S.UPG[id],y=278+i*127;this.button('upgrade:'+id,27,y,378,111,'','ghost',{radius:17});const g=c.createLinearGradient(28,y,135,y+110);g.addColorStop(0,u.color+'13');g.addColorStop(1,u.color+'00');box(c,28,y+1,375,109,16,g);
        box(c,45,y+29,48,48,13,u.color+'16',u.color+'4A',1);icon(c,u.icon,69,y+53,26,u.color);
        text(c,u.name,110,y+29,18,P.text,700);text(c,u.tag,110,y+54,10,u.color,600);wrap(c,u.description,110,y+78,268,11,P.muted,17,2);icon(c,'plus',377,y+30,14,u.color);
      });
      text(c,'战斗已暂停 · 选好再继续',216,698,11,P.muted,500,'center');
      const owned=Object.entries(app.sim.upgrades).filter(([,v])=>v>0);if(owned.length)text(c,'已获得 '+owned.reduce((n,[,v])=>n+v,0)+' 次强化',216,724,10,P.gold,600,'center');
    }
    gameOver(app){
      const c=this.c,s=app.sim;this.dim();this.modal(27,161,378,534);track(c,'THE CORRIDOR REMEMBERS',216,200,9,P.pink,1.9);text(c,'防线失守',216,244,33,P.text,750,'center');
      text(c,'第 '+s.wave+' 波',216,302,43,P.gold,750,'center');text(c,s.wave>=app.meta.bestWave?'这次回响，已记入你的最佳纪录。':'再调整一次火力，下一局走得更远。',216,350,11,P.muted,500,'center');
      const cols=[[compact(s.score),'得分'],[String(s.kills),'击破'],[String(s.bestCombo),'最高连锁']];cols.forEach((x,i)=>{const xx=92+i*124;text(c,x[0],xx,420,23,P.text,700,'center');text(c,x[1],xx,450,11,P.muted,500,'center');});
      this.button('restart',54,508,324,54,'再来一局','primary',{fontSize:17});this.button('home',54,580,324,45,'返回主界面','ghost',{fontSize:13});
    }
    dieInfo(app){
      const c=this.c,s=app.sim,d=s.board[app.selectedSlot];if(!d)return;const type=S.TYPES[d.type],stats=s.stats(d),level=C.levels[d.pips-1];this.dim();this.modal(28,139,376,604);
      this.button('closeDie',354,154,33,33,'','ghost',{icon:'close',iconOnly:true,iconSize:15});track(c,type.tag,216,177,10,type.color,2);
      dieFace(c,d.type,d.pips,216,255,92,-.06);text(c,type.name+'骰子 · '+d.pips+' 点',216,329,25,P.text,700,'center');
      const values=[[compact(stats.volley),'单次齐射伤害'],[stats.reload.toFixed(2)+'s','装填时间'],[String(d.pips),'每轮弹丸']];values.forEach((v,i)=>{const x=94+i*122;text(c,v[0],x,390,24,type.color,750,'center');text(c,v[1],x,420,10,P.muted,500,'center');});
      wrap(c,type.description,54,468,324,13,P.text,22,3);
      text(c,d.pips<6?'同种同点合成 → 卡组内随机 '+(d.pips+1)+' 点骰子':'已达六点上限，继续保留火力或回收。',216,553,11,P.muted,550,'center');
      this.button('closeDie',54,585,324,48,'返回战斗','primary',{fontSize:15});this.button('recycle:'+app.selectedSlot,54,651,324,43,'回收这颗骰子 · +'+level.recycle+' 能量','ghost',{fontSize:12,color:P.gold});
      text(c,'回收只移除骰子，不清除已发射弹丸。',216,720,10,P.muted,500,'center');
    }
    deck(app){
      const c=this.c;this.button('deckBack',26,30,40,38,'','ghost',{icon:'back',iconOnly:true});text(c,'配置卡组',216,48,25,P.text,750,'center');
      text(c,'携带 1–6 种骰子，召唤与合成均从中随机。',216,103,12,P.muted,500,'center');text(c,'类型越少越容易配对；类型越多，战斗效果越丰富。',216,127,10,P.muted,500,'center');
      Config.dice.forEach((type,i)=>{
        const x=26+(i%2)*194,y=162+Math.floor(i/2)*192,selected=app.editingDeck.includes(type.id);
        this.button('deck:'+type.id,x,y,182,177,'','ghost',{radius:17});box(c,x,y,182,177,17,selected?type.color+'0A':null,selected?type.color+'9A':'#2A3D50',1);
        if(selected){circle(c,x+160,y+20,9,type.color);icon(c,'check',x+160,y+20,12,'#16382F');}else circle(c,x+160,y+20,9,null,'#42596C',1);
        dieFace(c,type.id,1,x+38,y+47,46,-.04);text(c,type.name,x+77,y+37,18,P.text,700);text(c,type.tag,x+77,y+61,10,type.color,600);
        wrap(c,type.description,x+16,y+102,150,11,P.muted,18,4);
      });
      text(c,'当前每种出现概率：'+(app.editingDeck.length?(100/app.editingDeck.length).toFixed(1)+'%':'请选择至少一种'),216,761,11,P.muted,500,'center');
      this.button('deckSave',27,795,378,51,'保存卡组  '+app.editingDeck.length+' / 6','primary',{fontSize:15,disabled:app.editingDeck.length===0});
    }
    help(app){
      const c=this.c;this.dim();this.modal(24,99,384,671);this.button('closeHelp',355,116,33,33,'','ghost',{icon:'close',iconOnly:true,iconSize:14});text(c,'一分钟，学会回响',216,159,25,P.text,700,'center');
      const rows=[['aim','按住战场，移动瞄准','松手让所有已装填的骰子齐射。按住时不会自动开火；向下滑入骰子区可取消。'],['merge','同种类、同点数才可合成','把两颗相同骰子拖到一起，随机得到卡组内更高一点的骰子，并立刻打出强化齐射。'],['reload','数量与威力，需要取舍','保留更多骰子，火力更密；合成后单次齐射更强、装填稍慢，同时腾出一个格子。'],['energy','满格也不会卡死','阵地最多八格。点按骰子可看数值或回收。击破敌人与自然恢复都会补充召唤能量。']];
      rows.forEach((r,i)=>{const y=225+i*111;icon(c,r[0],55,y+2,24,i%2?P.gold:P.mint);text(c,r[1],82,y,15,P.text,650);wrap(c,r[2],82,y+29,293,12,P.muted,20,3);});
      this.button('closeHelp',51,698,330,48,'开始回响','primary',{fontSize:15});
    }
    confirmNew(app){
      const c=this.c;this.dim();this.modal(30,273,372,294);text(c,'开始新的回响？',216,324,25,P.text,700,'center');text(c,'当前保存的战斗将被替换，最佳纪录保留。',216,369,11,P.muted,500,'center');this.button('confirmStart',57,415,318,47,'开始新的一局','primary');this.button('cancelNew',57,481,318,42,'保留进度','ghost');
    }
  }
  return{Renderer,dieFace,icon,compact,rr,box,text,wrap};
});
