(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.DiceMath = factory();
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const lerp = (a, b, t) => a + (b - a) * t;
  const dist2 = (a, b) => (a.x - b.x) ** 2 + (a.y - b.y) ** 2;
  function unit(x, y) { const l = Math.hypot(x, y); return l > 1e-9 ? {x:x/l, y:y/l} : {x:0, y:-1}; }
  class RNG {
    constructor(seed) { this.state = (seed >>> 0) || 0x91a45be3; }
    next() { let x = this.state; x ^= x << 13; x ^= x >>> 17; x ^= x << 5; this.state = x >>> 0; return this.state / 4294967296; }
    int(n) { return Math.floor(this.next() * n); }
    pick(list) { return list[this.int(list.length)]; }
    shuffle(list) { const a = list.slice(); for (let i=a.length-1;i>0;i--) { const j=this.int(i+1); [a[i],a[j]]=[a[j],a[i]]; } return a; }
  }
  /** Swept point / expanded AABB. t is normalized over the provided displacement. */
  function sweepAABB(x, y, dx, dy, b) {
    if (x > b.left && x < b.right && y > b.top && y < b.bottom) {
      const edges = [{d:x-b.left,nx:-1,ny:0},{d:b.right-x,nx:1,ny:0},{d:y-b.top,nx:0,ny:-1},{d:b.bottom-y,nx:0,ny:1}];
      edges.sort((a,c)=>a.d-c.d); const q=edges[0];
      return {t:0,nx:q.nx,ny:q.ny,penetration:q.d};
    }
    let nearX=-Infinity, farX=Infinity, nearY=-Infinity, farY=Infinity;
    if (Math.abs(dx)<1e-10) { if (x<b.left || x>b.right) return null; }
    else { nearX=(b.left-x)/dx; farX=(b.right-x)/dx; if(nearX>farX)[nearX,farX]=[farX,nearX]; }
    if (Math.abs(dy)<1e-10) { if(y<b.top || y>b.bottom) return null; }
    else { nearY=(b.top-y)/dy; farY=(b.bottom-y)/dy; if(nearY>farY)[nearY,farY]=[farY,nearY]; }
    const t=Math.max(nearX,nearY), far=Math.min(farX,farY);
    if(t<0 || t>1 || t>far || far<0) return null;
    if(nearX>nearY) return {t,nx:dx>0?-1:1,ny:0,penetration:0};
    return {t,nx:0,ny:dy>0?-1:1,penetration:0};
  }
  class SpatialGrid {
    constructor(size=58) { this.size=size; this.bins=new Map(); }
    rebuild(enemies) {
      this.bins.clear();
      for(const e of enemies) if(!e.dead) {
        const x0=Math.floor((e.x-e.w/2)/this.size),x1=Math.floor((e.x+e.w/2)/this.size);
        const y0=Math.floor((e.y-e.h/2)/this.size),y1=Math.floor((e.y+e.h/2)/this.size);
        for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++) {const k=x+','+y;if(!this.bins.has(k))this.bins.set(k,[]);this.bins.get(k).push(e);}
      }
    }
    query(x0,y0,x1,y1) {
      const result=[], seen=new Set();
      for(let y=Math.floor(y0/this.size);y<=Math.floor(y1/this.size);y++)for(let x=Math.floor(x0/this.size);x<=Math.floor(x1/this.size);x++) {
        const bin=this.bins.get(x+','+y); if(!bin)continue;
        for(const e of bin)if(!e.dead&&!seen.has(e.id)){seen.add(e.id);result.push(e);}
      }
      return result;
    }
  }
  return {clamp,lerp,dist2,unit,RNG,sweepAABB,SpatialGrid};
});
