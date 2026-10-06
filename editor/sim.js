// JavaScript reference simulation. It must stay operation-for-operation identical to
// src/StackS.Kernel/Sim.cs; the golden replays in content/replays check this bit-for-bit.
// Used by the editor's JS fallback runtime and by tools/make-golden.mjs.

export const TYPES = {
  solid:      {c:0xb07a48, s:true,  size:[2,1,2],       label:'Solid block'},
  spawn:      {c:0x4a7bd0, s:false, size:[1,0.2,1],     label:'Spawn point'},
  checkpoint: {c:0x3bb6c4, s:false, size:[1,0.2,1],     label:'Checkpoint'},
  coin:       {c:0xf2c230, s:false, size:[0.5,0.5,0.5], label:'Coin'},
  goal:       {c:0x5fd068, s:false, size:[1,2,1],       label:'Goal'},
  kill:       {c:0xd9483b, s:false, size:[2,0.4,2],     label:'Kill zone'}
};
export const DT = 1/60, HX = 0.3, HY = 0.45, HZ = 0.3, EPS = 1e-4, DIAG = 0.7071067811865476;
export const TUNE_META = {
  moveSpeed:   {min:2,   max:12,  step:0.5,  label:'Move speed'},
  jumpHeight:  {min:0.5, max:5,   step:0.1,  label:'Jump height'},
  timeToApex:  {min:0.2, max:0.8, step:0.02, label:'Time to apex'},
  coyoteTicks: {min:0,   max:15,  step:1,    label:'Coyote ticks'}
};
export const DEFAULT_TUNING = {moveSpeed:6, jumpHeight:2.2, timeToApex:0.38, coyoteTicks:6};

export function defaultLevel(){
  const d = {version:1, nextId:1, tuning:{...DEFAULT_TUNING}, parts:[]};
  const P = (type,x,y,z,w,h,dd) => { const s = TYPES[type].size; d.parts.push({id:'p'+d.nextId++, type, x, y, z, w:w??s[0], h:h??s[1], d:dd??s[2]}); };
  P('solid', 0,-0.5,0, 16,1,16);
  P('spawn', -6,0.1,6);
  P('coin', -4,0.75,6);
  P('solid', -3,0.5,2);   P('coin', -3,1.6,2);
  P('solid', 0,1.5,-1);   P('checkpoint', 0,2.1,-1); P('coin', 0,2.9,-1);
  P('kill', 3.5,0.2,2.5, 3,0.4,3);
  P('solid', 3,2.5,-4);   P('coin', 3,3.6,-4);
  P('solid', 6,3.5,-6.5); P('coin', 4.5,4.6,-5.25);
  P('goal', 6,5,-6.5);
  return d;
}

export function validateDoc(raw){
  if(!raw || typeof raw !== 'object' || !Array.isArray(raw.parts)) throw new Error('not a level file');
  let v = raw.version ?? 0;
  if(v === 0 && raw.tune){ raw = {...raw, tuning: raw.tune}; v = 1; }   // v0 -> v1 migration
  if(v === 0) v = 1;
  if(v !== 1) throw new Error('unsupported level version ' + v);
  const d = {version:1, nextId:1, tuning:{...DEFAULT_TUNING}, parts:[]};
  for(const k in TUNE_META){ const n = Number(raw.tuning && raw.tuning[k]); if(isFinite(n)) d.tuning[k] = Math.min(TUNE_META[k].max, Math.max(TUNE_META[k].min, n)); }
  let maxId = 0;
  for(const p of raw.parts){
    if(!TYPES[p.type]) throw new Error('unknown part type ' + p.type);
    const q = {id:String(p.id), type:p.type};
    for(const f of ['x','y','z','w','h','d']){ const n = Number(p[f]); if(!isFinite(n)) throw new Error('bad ' + f + ' on ' + p.id); q[f] = n; }
    const n = parseInt(q.id.slice(1), 10); if(isFinite(n) && n > maxId) maxId = n;
    d.parts.push(q);
  }
  d.nextId = maxId + 1;
  return d;
}

function overlap(s,p){ return Math.abs(s.x-p.x) < HX+p.w/2 && Math.abs(s.y-p.y) < HY+p.h/2 && Math.abs(s.z-p.z) < HZ+p.d/2; }

export function makeSim(doc){
  const sp = doc.parts.find(p => p.type === 'spawn');
  const r = sp ? {x:sp.x, y:sp.y+sp.h/2+HY+0.01, z:sp.z} : {x:0, y:2, z:0};
  return {x:r.x, y:r.y, z:r.z, vx:0, vy:0, vz:0, grounded:false, coyote:0, buf:0, jumping:false, cut:false, prevJ:false,
          coins:[], respawn:r, deaths:0, tick:0, done:false, tuning:{...doc.tuning}, parts:doc.parts.map(p => ({...p}))};
}

function moveAxis(s, ax, d){
  if(d === 0) return 0;
  s[ax] += d;
  const H = ax === 'x' ? HX : ax === 'y' ? HY : HZ, sz = ax === 'x' ? 'w' : ax === 'y' ? 'h' : 'd';
  let hit = 0;
  for(const p of s.parts){
    if(!TYPES[p.type].s || !overlap(s,p)) continue;
    if(d > 0){ s[ax] = p[ax] - p[sz]/2 - H - EPS; hit = 1; } else { s[ax] = p[ax] + p[sz]/2 + H + EPS; hit = -1; }
  }
  return hit;
}

function die(s){ s.deaths++; s.x = s.respawn.x; s.y = s.respawn.y; s.z = s.respawn.z; s.vx = s.vy = s.vz = 0; s.jumping = false; }

// input bits: 1 left, 2 right, 4 forward, 8 back, 16 jump
export function step(s, inp){
  if(s.done){ s.tick++; return; }
  const t = s.tuning, g = 2*t.jumpHeight/(t.timeToApex*t.timeToApex), v0 = 2*t.jumpHeight/t.timeToApex;
  let dx = ((inp&2)?1:0) - ((inp&1)?1:0), dz = ((inp&8)?1:0) - ((inp&4)?1:0);
  if(dx && dz){ dx *= DIAG; dz *= DIAG; }
  s.vx += (dx*t.moveSpeed - s.vx)*0.25;
  s.vz += (dz*t.moveSpeed - s.vz)*0.25;
  const J = (inp & 16) !== 0;
  if(J && !s.prevJ) s.buf = 6; else if(s.buf > 0) s.buf--;
  if(s.grounded) s.coyote = t.coyoteTicks + 1; else if(s.coyote > 0) s.coyote--;
  if(s.buf > 0 && s.coyote > 0){ s.vy = v0; s.buf = 0; s.coyote = 0; s.grounded = false; s.jumping = true; s.cut = false; }
  if(!J && s.jumping && !s.cut && s.vy > 0){ s.vy *= 0.5; s.cut = true; }
  s.vy -= g*DT; if(s.vy < -30) s.vy = -30;
  if(moveAxis(s,'x',s.vx*DT)) s.vx = 0;
  if(moveAxis(s,'z',s.vz*DT)) s.vz = 0;
  const hy = moveAxis(s,'y',s.vy*DT);
  if(hy) s.vy = 0;
  s.grounded = hy === -1;
  if(s.grounded) s.jumping = false;
  for(const p of s.parts){
    if(TYPES[p.type].s || !overlap(s,p)) continue;
    if(p.type === 'coin'){ if(!s.coins.includes(p.id)) s.coins.push(p.id); }
    else if(p.type === 'checkpoint'){ s.respawn = {x:p.x, y:p.y+p.h/2+HY+0.01, z:p.z}; }
    else if(p.type === 'goal'){ s.done = true; }
    else if(p.type === 'kill'){ die(s); break; }
  }
  if(s.y < -15) die(s);
  s.prevJ = J;
  s.tick++;
}

const dv = new DataView(new ArrayBuffer(8));
function bits(x){ dv.setFloat64(0, x); return dv.getBigUint64(0).toString(16).padStart(16, '0'); }
export function stateKey(s){
  return [s.x,s.y,s.z,s.vx,s.vy,s.vz].map(bits).join(',') + '|' + s.coins.join(',') + '|' + s.deaths + '|' + s.tick + '|' + (s.done ? 1 : 0);
}
export function fnv(str){ let h = 0x811c9dc5; for(let i=0;i<str.length;i++){ h ^= str.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; } return h.toString(16).padStart(8,'0'); }

export function replay(start, inputs, tune){
  const s = makeSim(start); let k = 0;
  for(let i = 0; i < inputs.length; i++){
    while(k < tune.length && tune[k].tick <= i){ s.tuning[tune[k].key] = tune[k].value; k++; }
    step(s, inputs[i]);
  }
  return s;
}
