(function(root,factory){if(typeof module==='object'&&module.exports)module.exports=factory();else root.DiceAudio=factory();})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  const NOTES=[261.63,329.63,392,523.25,659.25,783.99,1046.5];
  /** Original synthesized sound design. No downloads, samples, fonts, or external assets. */
  function synthesize(kind,variant=0,sampleRate=22050){
    const duration={tap:0.085,launch:0.12,hit:0.07,kill:0.19,explode:0.28,merge:0.58,summon:0.30,upgrade:0.65,breach:0.40,clear:0.62,music:0.90,error:0.15}[kind]||0.12;
    const out=new Float32Array(Math.ceil(sampleRate*duration));let seed=194783+variant*197;let phase=0;
    const noise=()=>{seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return(seed>>>0)/2147483648-1;};
    for(let i=0;i<out.length;i++){
      const t=i/sampleRate,progress=t/duration;let v=0;
      const n=noise(),fade=Math.pow(1-progress,2),attack=Math.min(1,t*250);
      if(kind==='hit')v=(Math.sin(t*Math.PI*2*NOTES[variant%NOTES.length]*2)*0.48+n*0.08)*Math.exp(-t*60);
      else if(kind==='launch'){phase+=2*Math.PI*(850-520*progress)/sampleRate;v=(Math.sin(phase)*0.28+n*0.065)*fade;}
      else if(kind==='kill'){v=(Math.sin(t*2*Math.PI*NOTES[variant%NOTES.length])+0.22*Math.sin(t*2*Math.PI*NOTES[variant%NOTES.length]*2))*Math.exp(-t*19)*0.48+n*0.12*Math.exp(-t*40);}
      else if(kind==='explode'){phase+=2*Math.PI*(115-75*progress)/sampleRate;v=(Math.sin(phase)*0.52+n*0.30)*Math.exp(-t*13);}
      else if(kind==='merge'||kind==='summon'||kind==='upgrade'||kind==='clear'){
        const sequence=kind==='merge'?[0,2,3,5,6]:kind==='summon'?[0,2,4]:[0,1,2,4,6];
        const step=duration*0.68/sequence.length;
        for(let k=0;k<sequence.length;k++){const nt=t-k*step;if(nt>=0)v+=Math.sin(nt*2*Math.PI*NOTES[sequence[k]])*Math.exp(-nt*13)*0.26;}
      }else if(kind==='breach'){phase+=2*Math.PI*(155-85*progress)/sampleRate;v=(Math.sin(phase)+n*0.2)*fade*0.52;}
      else if(kind==='music'){const f=NOTES[variant%NOTES.length]/2;v=(Math.sin(t*2*Math.PI*f)+0.15*Math.sin(t*2*Math.PI*f*2))*Math.exp(-t*5)*0.22;}
      else if(kind==='error')v=Math.sin(t*2*Math.PI*(progress<0.45?240:180))*fade*0.30;
      else v=Math.sin(t*2*Math.PI*(900-300*progress))*fade*0.28;
      out[i]=Math.max(-0.96,Math.min(0.96,v*attack));
    }
    return{data:out,sampleRate};
  }
  class Sound{
    constructor(platform={}){this.platform=platform;this.enabled=true;this.music=false;this.context=null;this.master=null;this.cache=new Map();this.last={};this.voices=0;this.musicClock=0;this.musicStep=0;this.unlocked=false;}
    unlock(){
      if(!this.context){try{
        const factory=this.platform.createAudioContext;
        this.context=factory?factory():null;
        if(this.context&&this.context.createGain){this.master=this.context.createGain();this.master.gain.value=0.46;this.master.connect(this.context.destination);}
      }catch(_){this.context=null;}}
      this.unlocked=true;
      if(this.context&&this.context.resume)try{const r=this.context.resume();if(r&&r.catch)r.catch(()=>{});}catch(_){}
    }
    setEnabled(value){this.enabled=!!value;if(this.master)this.master.gain.value=this.enabled?0.46:0;}
    play(kind,variant=0){
      if(!this.enabled||!this.unlocked)return;
      const now=this.platform.now?this.platform.now():Date.now()/1000;
      const gap=kind==='hit'?0.04:kind==='launch'?0.055:kind==='kill'?0.045:kind==='explode'?0.10:0;
      if(gap&&(now-(this.last[kind]||-100))<gap)return;this.last[kind]=now;
      if(!this.context||!this.master){if(this.platform.playFallback)try{this.platform.playFallback(kind);}catch(_){}return;}
      if(this.voices>22)return;
      try{
        const key=kind+':'+(variant%7);let buffer=this.cache.get(key);
        if(!buffer){const s=synthesize(kind,variant%7);buffer=this.context.createBuffer(1,s.data.length,s.sampleRate);buffer.getChannelData(0).set(s.data);this.cache.set(key,buffer);}
        const src=this.context.createBufferSource();src.buffer=buffer;const gain=this.context.createGain();gain.gain.value=kind==='music'?0.28:1;src.connect(gain);gain.connect(this.master);this.voices++;
        src.onended=()=>{this.voices=Math.max(0,this.voices-1);try{src.disconnect();gain.disconnect();}catch(_){}};src.start(0);
      }catch(_){if(this.platform.playFallback)try{this.platform.playFallback(kind);}catch(__){}}
    }
    tick(dt,active){if(!active||!this.music||!this.enabled||!this.unlocked)return;this.musicClock+=dt;if(this.musicClock>=0.36){this.musicClock-=0.36;const sequence=[0,2,1,4,0,3,2,5,1,4,2,6,0,2,3,4];this.play('music',sequence[this.musicStep++%sequence.length]);}}
    suspend(){if(this.context&&this.context.suspend)try{const r=this.context.suspend();if(r&&r.catch)r.catch(()=>{});}catch(_){}if(this.platform.stopFallback)this.platform.stopFallback();}
    destroy(){if(this.context&&this.context.close)try{this.context.close();}catch(_){}this.cache.clear();}
  }
  return{Sound,synthesize};
});
