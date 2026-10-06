import { TYPES, TUNE_META, HY, DT, defaultLevel } from './sim.js';
import { createJsRuntime } from './runtime-js.js';
import { createCsRuntime } from './runtime-cs.js';

// ================= RUNTIME selection =================
// Default: the C# runtime compiled to WebAssembly (served at ./runtime/ by the dev server and on GitHub Pages).
// ?runtime=js forces the JavaScript reference runtime.
const wantJs = new URLSearchParams(location.search).get('runtime') === 'js';
let Runtime = null, runtimeNote = '';
if(!wantJs){
  try{ Runtime = await createCsRuntime(new URL('./runtime/', location.href).href); }
  catch(e){ runtimeNote = 'C# runtime not found (' + e.message + '). Build it with: dotnet publish src/StackS.Web -c Release -o build/web'; }
}
if(!Runtime) Runtime = createJsRuntime();
const badge = document.getElementById('rtBadge');
badge.textContent = Runtime.kind === 'csharp' ? 'Runtime: C# (.NET WebAssembly)' : 'Runtime: JavaScript reference';
badge.classList.toggle('cs', Runtime.kind === 'csharp');

// ================= RUNTIME: PS1 renderer =================
const Render = (() => {
  const VS = `uniform vec2 uRes;uniform float uJ;uniform float uA;uniform vec3 uL;uniform float uFN;uniform float uFF;uniform float uRep;
varying vec2 vUv;varying float vW;varying vec3 vLight;varying float vFog;
void main(){vec4 mv=modelViewMatrix*vec4(position,1.0);vec4 clip=projectionMatrix*mv;
if(uJ>0.5){vec2 g=uRes*0.5;clip.xy=floor(clip.xy/clip.w*g+0.5)/g*clip.w;}
gl_Position=clip;vec3 n=normalize(normalMatrix*normal);vec3 l=normalize((viewMatrix*vec4(uL,0.0)).xyz);
float d=max(dot(n,l),0.0);vLight=mix(vec3(0.42,0.44,0.55),vec3(1.05),d);
float w=mix(1.0,clip.w,uA);vUv=uv*uRep*w;vW=w;vFog=clamp((-mv.z-uFN)/(uFF-uFN),0.0,1.0);}`;
  const FS = `uniform sampler2D uTex;uniform vec3 uCol;uniform float uD;uniform vec3 uFogC;
varying vec2 vUv;varying float vW;varying vec3 vLight;varying float vFog;
float b2(vec2 a){a=floor(a);return fract(a.x*0.5+a.y*a.y*0.75);}
float b4(vec2 a){return b2(0.5*a)*0.25+b2(a);}
void main(){vec2 uv=vUv/vW;vec3 base=uCol*texture2D(uTex,uv).rgb;
vec3 c=base*vLight;c=mix(c,uFogC,vFog);
if(uD>0.5){c=floor(c*15.0+b4(gl_FragCoord.xy))/15.0;}
gl_FragColor=vec4(c,1.0);}`;
  const cv = document.getElementById('cv');
  const R = new THREE.WebGLRenderer({canvas:cv, antialias:false});
  const scene = new THREE.Scene();
  const cam = new THREE.PerspectiveCamera(60, 16/9, 0.1, 200);
  const U = {uJ:{value:1}, uA:{value:1}, uD:{value:1}, uRes:{value:new THREE.Vector2(427,240)}, uFogC:{value:new THREE.Color(0x2a2340)}, uFN:{value:12}, uFF:{value:40}, uL:{value:new THREE.Vector3(0.5,1,0.3).normalize()}};
  function mkTex(){
    const n = 16, d = new Uint8Array(n*n*4);
    let seed = 7; const rnd = () => { seed = (seed*16807) % 2147483647; return seed/2147483647; };
    for(let y=0;y<n;y++) for(let x=0;x<n;x++){ const i=(y*n+x)*4, c=(((x>>2)+(y>>2))%2 ? 232 : 182) + Math.floor(rnd()*22-11); d[i]=d[i+1]=d[i+2]=Math.max(0,Math.min(255,c)); d[i+3]=255; }
    const t = new THREE.DataTexture(d, n, n, THREE.RGBAFormat);
    t.magFilter = t.minFilter = THREE.NearestFilter; t.wrapS = t.wrapT = THREE.RepeatWrapping; t.needsUpdate = true; return t;
  }
  const TX = mkTex(), mats = new Map();
  function mat(color, rep){
    const k = color+'_'+rep; if(mats.has(k)) return mats.get(k);
    const m = new THREE.ShaderMaterial({uniforms:{uJ:U.uJ,uA:U.uA,uD:U.uD,uRes:U.uRes,uFogC:U.uFogC,uFN:U.uFN,uFF:U.uFF,uL:U.uL,uTex:{value:TX},uCol:{value:new THREE.Color(color)},uRep:{value:rep}},vertexShader:VS,fragmentShader:FS});
    mats.set(k, m); return m;
  }
  const partsGroup = new THREE.Group(); scene.add(partsGroup);
  const grid = new THREE.GridHelper(40, 80, 0x8a8794, 0x55525e); grid.position.y = 0.002; scene.add(grid);
  const sel = new THREE.LineSegments(new THREE.EdgesGeometry(new THREE.BoxGeometry(1,1,1)), new THREE.LineBasicMaterial({color:0xffffff}));
  sel.visible = false; scene.add(sel);
  // segmented PS1-style hero: rigid parts, no skinning
  const hero = new THREE.Group(); hero.visible = false; scene.add(hero);
  const hb = (x,y,z,w,h,d,c,parent) => { const m = new THREE.Mesh(new THREE.BoxGeometry(w,h,d), mat(c,1)); m.position.set(x,y,z); (parent||hero).add(m); return m; };
  const legL = new THREE.Group(), legR = new THREE.Group(); legL.position.set(-0.11,0.36,0); legR.position.set(0.11,0.36,0); hero.add(legL); hero.add(legR);
  hb(0,-0.18,0,0.17,0.36,0.17,0x2f4f9a,legL); hb(0,-0.18,0,0.17,0.36,0.17,0x2f4f9a,legR);
  hb(0,0.56,0,0.46,0.42,0.28,0xd9483b); hb(0,0.92,0,0.3,0.3,0.3,0xf1c9a0);
  hb(-0.3,0.56,0,0.13,0.38,0.13,0xd9483b); hb(0.3,0.56,0,0.13,0.38,0.13,0xd9483b);

  const orbit = {theta:0.75, phi:0.95, dist:20, target:new THREE.Vector3(0,1,0)};
  let ps1 = true, lastDocRef = null, docDirty = true, playCamInit = false, facing = 0;
  const meshById = new Map();

  function size(){
    const w = cv.parentElement.clientWidth || 640, asp = 16/9;
    const h = ps1 ? 240 : Math.round(w/asp*Math.min(window.devicePixelRatio||1, 2)), rw = Math.round(h*asp);
    R.setPixelRatio(1); R.setSize(rw, h, false); U.uRes.value.set(rw, h); cam.aspect = asp; cam.updateProjectionMatrix();
  }
  function setPS1(on){
    ps1 = on; U.uJ.value = U.uA.value = U.uD.value = on ? 1 : 0;
    U.uFogC.value.set(on ? 0x2a2340 : 0xbfe0f5); R.setClearColor(U.uFogC.value);
    U.uFN.value = on ? 12 : 40; U.uFF.value = on ? 40 : 120; size();
  }
  function rebuild(doc){
    for(const m of partsGroup.children) m.geometry.dispose();
    partsGroup.clear(); meshById.clear();
    for(const p of doc.parts){
      // subdivide big faces so affine warping stays subtle
      const g = new THREE.BoxGeometry(p.w, p.h, p.d, Math.max(1,Math.ceil(p.w/2)), Math.max(1,Math.ceil(p.h/2)), Math.max(1,Math.ceil(p.d/2)));
      const rep = Math.max(1, Math.round(Math.max(p.w, p.d)/2));
      const m = new THREE.Mesh(g, mat(TYPES[p.type].c, rep));
      m.position.set(p.x, p.y, p.z); m.userData.id = p.id; m.userData.type = p.type;
      partsGroup.add(m); meshById.set(p.id, m);
    }
  }
  function frame(now, selectedId){
    const v = Runtime.view();
    if(docDirty || v.doc !== lastDocRef){ rebuild(v.doc); lastDocRef = v.doc; docDirty = false; }
    const t = now/1000;
    for(const m of partsGroup.children){
      if(m.userData.type === 'coin'){ m.rotation.y = t*2; m.visible = !(v.sim && v.sim.coins.includes(m.userData.id)); }
    }
    if(v.mode === 'play' && v.sim){
      const s = v.sim;
      grid.visible = false; sel.visible = false; hero.visible = true;
      hero.position.set(s.x, s.y - HY, s.z);
      const sp = Math.hypot(s.vx, s.vz);
      if(sp > 0.3) facing = Math.atan2(s.vx, s.vz);
      hero.rotation.y = facing;
      const swing = sp > 0.3 && s.grounded ? Math.sin(t*14)*0.6 : 0;
      legL.rotation.x = swing; legR.rotation.x = -swing;
      const want = new THREE.Vector3(s.x, s.y + 3.2, s.z + 6.5);
      if(!playCamInit){ cam.position.copy(want); playCamInit = true; } else cam.position.lerp(want, 0.12);
      cam.lookAt(s.x, s.y + 0.4, s.z);
    } else {
      playCamInit = false; hero.visible = false; grid.visible = true;
      const o = orbit;
      cam.position.set(o.target.x + o.dist*Math.sin(o.phi)*Math.sin(o.theta), o.target.y + o.dist*Math.cos(o.phi), o.target.z + o.dist*Math.sin(o.phi)*Math.cos(o.theta));
      cam.lookAt(o.target);
      const p = selectedId && v.doc.parts.find(q => q.id === selectedId);
      sel.visible = !!p;
      if(p){ sel.position.set(p.x, p.y, p.z); sel.scale.set(p.w+0.04, p.h+0.04, p.d+0.04); }
    }
    R.render(scene, cam);
  }
  const raycaster = new THREE.Raycaster();
  function pick(clientX, clientY){
    const r = cv.getBoundingClientRect();
    raycaster.setFromCamera({x:((clientX-r.left)/r.width)*2-1, y:-((clientY-r.top)/r.height)*2+1}, cam);
    const hits = raycaster.intersectObjects(partsGroup.children, false);
    if(hits.length){ const h = hits[0]; return {id:h.object.userData.id, point:h.point.clone(), normal:h.face.normal.clone()}; }
    const pt = new THREE.Vector3();
    return raycaster.ray.intersectPlane(new THREE.Plane(new THREE.Vector3(0,1,0), 0), pt) ? {id:null, point:pt, normal:new THREE.Vector3(0,1,0)} : null;
  }
  function pan(dx, dy){
    const o = orbit, k = o.dist/600;
    const right = new THREE.Vector3(Math.cos(o.theta), 0, -Math.sin(o.theta)), fwd = new THREE.Vector3(Math.sin(o.theta), 0, Math.cos(o.theta));
    o.target.addScaledVector(right, -dx*k).addScaledVector(fwd, -dy*k);
  }
  new ResizeObserver(size).observe(cv.parentElement);
  setPS1(true);
  return {frame, pick, pan, orbit, setPS1, markDirty: () => { docDirty = true; }};
})();

// ================= TOOLS: editor (talks to the runtime only through Runtime.exec) =================
const $ = id => document.getElementById(id);
const logEl = $('log');
function log(msg, cls){ const d = document.createElement('div'); if(cls) d.className = cls; d.textContent = msg; logEl.appendChild(d); while(logEl.childNodes.length > 200) logEl.removeChild(logEl.firstChild); logEl.scrollTop = logEl.scrollHeight; }
function cmd(name, args, quiet){
  const r = Runtime.exec(name, args);
  if(!quiet) log('> ' + name + (args && Object.keys(args).length ? ' ' + JSON.stringify(args) : ''), 'dim');
  if(!r.ok) log('  ' + r.err, 'err');
  return r;
}
const snap = (v, g) => Math.round(v/g)*g;
let tool = 'select', selected = null;
const docNow = () => Runtime.view().doc;
const isPlaying = () => Runtime.view().mode === 'play';

// --- tool palette ---
const toolsEl = $('tools');
function renderTools(){
  toolsEl.innerHTML = '';
  const add = (key, label, color) => {
    const b = document.createElement('button');
    b.className = tool === key ? 'on' : '';
    b.innerHTML = (color != null ? '<span class="sw" style="background:#' + color.toString(16).padStart(6,'0') + '"></span>' : '') + label;
    b.onclick = () => { tool = key; renderTools(); };
    toolsEl.appendChild(b);
  };
  add('select', 'Select');
  for(const k in TYPES) add(k, TYPES[k].label, TYPES[k].c);
}

// --- tuning panel ---
const tuneEl = $('tuning'), tuneInputs = {};
for(const k in TUNE_META){
  const m = TUNE_META[k], row = document.createElement('label');
  row.className = 'row';
  row.innerHTML = '<span>' + m.label + '</span><input type="range" min="' + m.min + '" max="' + m.max + '" step="' + m.step + '"><output></output>';
  const inp = row.querySelector('input'), out = row.querySelector('output');
  inp.addEventListener('input', () => { const r = cmd('tune.set', {key:k, value:+inp.value}, true); if(r.ok) out.textContent = r.res; });
  inp.addEventListener('change', () => log('> tune.set ' + JSON.stringify({key:k, value:+inp.value}), 'dim'));
  tuneInputs[k] = {inp, out};
  tuneEl.appendChild(row);
}
function renderTuning(){ const t = docNow().tuning; for(const k in tuneInputs){ tuneInputs[k].inp.value = t[k]; tuneInputs[k].out.textContent = t[k]; } }

// --- inspector ---
const inspEl = $('inspector');
function renderInspector(){
  if(inspEl.contains(document.activeElement) && document.activeElement.tagName === 'INPUT') return;
  const p = selected && docNow().parts.find(q => q.id === selected);
  if(!p){ selected = null; inspEl.innerHTML = '<p class="empty">Nothing selected. Click a part in the scene, or choose a part type under Build and click to place it.</p>'; return; }
  let html = '<div class="field">Part ' + p.id + '<select data-f="type">';
  for(const k in TYPES) html += '<option value="' + k + '"' + (k === p.type ? ' selected' : '') + '>' + TYPES[k].label + '</option>';
  html += '</select></div><div class="fields">';
  for(const f of ['x','y','z','w','h','d']) html += '<label class="field">' + ({x:'X',y:'Y',z:'Z',w:'Width',h:'Height',d:'Depth'})[f] + '<input type="number" step="0.25" data-f="' + f + '" value="' + p[f] + '"></label>';
  html += '</div><div class="btnrow"><button id="dupBtn">Duplicate</button><button id="delBtn">Delete</button></div>';
  inspEl.innerHTML = html;
  inspEl.querySelectorAll('[data-f]').forEach(el => el.addEventListener('change', () => {
    cmd('part.set', {id:p.id, field:el.dataset.f, value: el.dataset.f === 'type' ? el.value : +el.value});
    el.blur(); renderInspector();
  }));
  $('dupBtn').onclick = duplicate;
  $('delBtn').onclick = removeSelected;
}
function duplicate(){
  if(!selected) return;
  const p = docNow().parts.find(q => q.id === selected); if(!p) return;
  const r = cmd('part.add', {type:p.type, x:p.x + Math.max(0.5, p.w), y:p.y, z:p.z, w:p.w, h:p.h, d:p.d});
  if(r.ok){ selected = r.res; renderInspector(); }
}
function removeSelected(){ if(!selected) return; const r = cmd('part.remove', {id:selected}); if(r.ok){ selected = null; renderInspector(); } }

// --- viewport input (edit mode) ---
const cv = $('cv');
let drag = null;
cv.addEventListener('contextmenu', e => e.preventDefault());
cv.addEventListener('pointerdown', e => {
  if(isPlaying()) return;
  cv.setPointerCapture(e.pointerId);
  drag = {x:e.clientX, y:e.clientY, moved:false, pan:e.shiftKey || e.button === 2};
});
cv.addEventListener('pointermove', e => {
  if(!drag) return;
  const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
  if(!drag.moved && Math.abs(dx) + Math.abs(dy) < 5) return;
  drag.moved = true; drag.x = e.clientX; drag.y = e.clientY;
  const o = Render.orbit;
  if(drag.pan) Render.pan(dx, dy);
  else { o.theta -= dx*0.008; o.phi = Math.min(1.45, Math.max(0.15, o.phi - dy*0.008)); }
});
cv.addEventListener('pointerup', e => {
  if(!drag) return;
  const wasClick = !drag.moved; drag = null;
  if(wasClick && !isPlaying()) clickScene(e.clientX, e.clientY);
});
cv.addEventListener('wheel', e => { if(isPlaying()) return; e.preventDefault(); const o = Render.orbit; o.dist = Math.min(70, Math.max(4, o.dist*(e.deltaY > 0 ? 1.1 : 0.9))); }, {passive:false});

function clickScene(x, y){
  const hit = Render.pick(x, y);
  if(tool === 'select'){ selected = hit && hit.id; renderInspector(); return; }
  if(!hit) return;
  const [w,h,d] = TYPES[tool].size, n = hit.normal, pt = hit.point;
  let px = pt.x + n.x*w/2, py = pt.y + n.y*h/2, pz = pt.z + n.z*d/2;
  if(tool === 'coin') py += 0.4;
  px = snap(px, 0.5); pz = snap(pz, 0.5); py = snap(py, 0.05);
  const r = cmd('part.add', {type:tool, x:px, y:py, z:pz});
  if(r.ok){ selected = r.res; renderInspector(); }
}

// --- keyboard ---
const keys = new Set(), touchBits = {v:0};
const typing = t => t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT');
document.addEventListener('keydown', e => {
  if(typing(e.target)) return;
  if(e.code === 'Tab'){ e.preventDefault(); togglePlay(); return; }
  if(isPlaying()){
    if(['Space','ArrowUp','ArrowDown','ArrowLeft','ArrowRight'].includes(e.code)) e.preventDefault();
    keys.add(e.code); return;
  }
  const mod = e.ctrlKey || e.metaKey;
  if(mod && e.code === 'KeyZ'){ e.preventDefault(); cmd(e.shiftKey ? 'redo' : 'undo'); return; }
  if(mod && e.code === 'KeyY'){ e.preventDefault(); cmd('redo'); return; }
  if(mod && e.code === 'KeyD'){ e.preventDefault(); duplicate(); return; }
  if(e.code === 'Escape'){ tool = 'select'; selected = null; renderTools(); renderInspector(); return; }
  if(!selected) return;
  const mv = {ArrowLeft:[-0.5,0,0], ArrowRight:[0.5,0,0], ArrowUp:[0,0,-0.5], ArrowDown:[0,0,0.5], KeyQ:[0,-0.25,0], KeyE:[0,0.25,0]}[e.code];
  if(mv){ e.preventDefault(); cmd('part.move', {id:selected, dx:mv[0], dy:mv[1], dz:mv[2]}, true); renderInspector(); return; }
  if(e.code === 'Delete' || e.code === 'Backspace'){ e.preventDefault(); removeSelected(); }
});
document.addEventListener('keyup', e => keys.delete(e.code));
window.addEventListener('blur', () => { keys.clear(); touchBits.v = 0; });
document.querySelectorAll('.touch [data-bit]').forEach(b => {
  const bit = +b.dataset.bit;
  b.addEventListener('pointerdown', e => { e.preventDefault(); b.setPointerCapture(e.pointerId); touchBits.v |= bit; });
  const up = () => { touchBits.v &= ~bit; };
  b.addEventListener('pointerup', up); b.addEventListener('pointercancel', up);
});
function inputBits(){
  let b = touchBits.v;
  if(keys.has('ArrowLeft') || keys.has('KeyA')) b |= 1;
  if(keys.has('ArrowRight') || keys.has('KeyD')) b |= 2;
  if(keys.has('ArrowUp') || keys.has('KeyW')) b |= 4;
  if(keys.has('ArrowDown') || keys.has('KeyS')) b |= 8;
  if(keys.has('Space') || keys.has('KeyK')) b |= 16;
  return b;
}

// --- play / stop ---
function togglePlay(){ cmd(isPlaying() ? 'stop' : 'play'); }
$('playBtn').onclick = togglePlay;
$('undoBtn').onclick = () => { cmd('undo'); renderInspector(); };
$('redoBtn').onclick = () => { cmd('redo'); renderInspector(); };
$('ps1').onchange = e => Render.setPS1(e.target.checked);
$('resetBtn').onclick = () => { if(!isPlaying() || cmd('stop').ok){ cmd('doc.load', {doc:defaultLevel()}); selected = null; renderInspector(); } };

// --- replay verification ---
$('verifyBtn').onclick = () => {
  const r = cmd('replay.verify');
  const out = $('verifyOut');
  if(!r.ok){ out.className = 'err'; out.textContent = r.err; return; }
  const v = r.res;
  out.className = v.match ? 'ok' : 'err';
  out.textContent = (v.match ? 'Match. ' : 'Mismatch. ') + '[' + (v.runtime || 'js') + '] ' + v.ticks + ' ticks re-simulated in ' + v.ms + ' ms. Live end state ' + v.live + ', replay end state ' + v.replay + '.';
  log('  ' + (v.match ? 'replay matches (' + v.live + ')' : 'replay MISMATCH ' + v.live + ' vs ' + v.replay), v.match ? 'ok' : 'err');
};

// --- export / import ---
const io = $('io');
$('ioBtn').onclick = () => { $('ioText').value = JSON.stringify(docNow(), null, 2); io.showModal(); };
$('ioClose').onclick = () => io.close();
$('ioCopy').onclick = async () => { const t = $('ioText'); try{ await navigator.clipboard.writeText(t.value); log('  copied level JSON', 'ok'); }catch(e){ t.select(); log('  select all and copy manually', 'dim'); } };
$('ioLoad').onclick = () => {
  let parsed; try{ parsed = JSON.parse($('ioText').value); }catch(e){ log('  that text is not valid JSON', 'err'); return; }
  if(isPlaying()) cmd('stop');
  const r = cmd('doc.load', {doc:parsed});
  if(r.ok){ selected = null; renderInspector(); io.close(); }
};

// --- console: raw access to the same command bus ---
const HELP = 'commands: add <type> <x> <y> <z> | set <id> <field> <value> | move <id> <dx> <dy> <dz> | del <id> | tune <key> <value> | inspect <id> | list | play | stop | undo | redo | verify\ntypes: ' + Object.keys(TYPES).join(', ') + '\ntunables: ' + Object.keys(TUNE_META).join(', ');
$('console').addEventListener('keydown', e => {
  if(e.key !== 'Enter') return;
  const line = e.target.value.trim(); e.target.value = '';
  if(!line) return;
  log(line);
  const [c, ...a] = line.split(/\s+/);
  let r;
  switch(c){
    case 'help': log(HELP, 'dim'); return;
    case 'add': r = Runtime.exec('part.add', {type:a[0], x:+a[1], y:+a[2], z:+a[3]}); if(r.ok){ selected = r.res; } break;
    case 'set': r = Runtime.exec('part.set', {id:a[0], field:a[1], value:a[1] === 'type' ? a[2] : +a[2]}); break;
    case 'move': r = Runtime.exec('part.move', {id:a[0], dx:+a[1], dy:+a[2], dz:+a[3]}); break;
    case 'del': r = Runtime.exec('part.remove', {id:a[0]}); break;
    case 'tune': r = Runtime.exec('tune.set', {key:a[0], value:+a[1]}); break;
    case 'inspect': r = Runtime.exec('part.get', {id:a[0]}); break;
    case 'list': r = Runtime.exec('doc.get'); if(r.ok) r.res = r.res.parts.map(p => p.id + ' ' + p.type + ' (' + p.x + ', ' + p.y + ', ' + p.z + ')').join('\n'); break;
    case 'verify': r = Runtime.exec('replay.verify'); break;
    case 'play': case 'stop': case 'undo': case 'redo': r = Runtime.exec(c); break;
    default: log('  unknown command, type help', 'err'); return;
  }
  if(r.ok) log('  ' + (typeof r.res === 'object' ? JSON.stringify(r.res) : r.res), 'ok'); else log('  ' + r.err, 'err');
  renderInspector();
});


// --- dev server: load/save the level on disk (content/levels/current.level.json) and golden replays ---
const DevServer = (() => {
  let online = false, timer = null;
  async function start(){
    try{ online = (await fetch('/api/ping', {cache:'no-store'})).ok; }catch(e){ online = false; }
    if(!online){ log('No dev server: changes are not saved to disk. Run: node devserver/serve.mjs', 'dim'); return; }
    try{
      const r = await fetch('/content/levels/current.level.json', {cache:'no-store'});
      if(r.ok){ const res = cmd('doc.load', {doc: await r.json()}, true); if(res.ok) log('  loaded content/levels/current.level.json', 'ok'); }
    }catch(e){ log('  could not load current.level.json: ' + e.message, 'err'); }
    Runtime.subscribe(ev => { if(ev.type === 'doc') schedule(); });
  }
  function schedule(){ if(!online) return; clearTimeout(timer); timer = setTimeout(save, 600); }
  async function save(){
    try{
      const r = await fetch('/content/levels/current.level.json', {method:'PUT', headers:{'Content-Type':'application/json'}, body:JSON.stringify(docNow(), null, 2)});
      if(!r.ok) log('  autosave failed: ' + r.status, 'err');
    }catch(e){ log('  autosave failed: ' + e.message, 'err'); }
  }
  async function saveGolden(name, json){
    if(!online){ log('  start the dev server to save golden replays', 'err'); return; }
    const r = await fetch('/content/replays/' + name + '.replay.json', {method:'PUT', headers:{'Content-Type':'application/json'}, body:json});
    log(r.ok ? '  saved content/replays/' + name + '.replay.json' : '  save failed: ' + r.status, r.ok ? 'ok' : 'err');
  }
  return {start, saveGolden};
})();

$('goldenBtn').onclick = async () => {
  const name = (prompt('Name for this golden replay (letters, digits, dashes):', 'run-' + Date.now().toString(36)) || '').trim();
  if(!/^[a-z0-9][a-z0-9_-]{0,60}$/i.test(name)){ if(name) log('  invalid name', 'err'); return; }
  const r = cmd('replay.export', {name});
  if(r.ok) await DevServer.saveGolden(name, typeof r.res === 'string' ? r.res : JSON.stringify(r.res));
};

// --- runtime events ---
Runtime.subscribe(ev => {
  if(ev.type === 'doc'){ Render.markDirty(); renderInspector(); renderTuning(); }
  if(ev.type === 'mode'){
    const p = isPlaying();
    document.body.classList.toggle('playing', p);
    $('playBtn').textContent = p ? 'Stop (Tab)' : 'Play (Tab)';
    keys.clear(); touchBits.v = 0; acc = 0;
    Render.markDirty();
    if(p) cv.focus && document.activeElement && document.activeElement.blur();
  }
});

// --- main loop: fixed 60 Hz simulation, render every frame ---
const hud = $('hud'), banner = $('banner');
let acc = 0, last = performance.now();
function loop(now){
  const dt = Math.min(0.1, (now - last)/1000); last = now;
  if(isPlaying()){
    acc += dt; let n = 0;
    while(acc >= DT && n < 5){ Runtime.exec('tick', {input:inputBits()}); acc -= DT; n++; }
    if(n === 5) acc = 0;
  }
  Render.frame(now, selected);
  const v = Runtime.view();
  if(v.mode === 'play' && v.sim){
    const total = v.doc.parts.filter(p => p.type === 'coin').length;
    hud.textContent = 'Coins ' + v.sim.coins.length + '/' + total + '   Deaths ' + v.sim.deaths + '   ' + (v.sim.tick/60).toFixed(1) + ' s';
    banner.style.display = v.sim.done ? 'flex' : 'none';
  } else {
    hud.textContent = 'Edit · ' + (tool === 'select' ? 'Select' : 'Place ' + TYPES[tool].label);
    banner.style.display = 'none';
  }
  requestAnimationFrame(loop);
}

renderTools(); renderTuning(); renderInspector();
log('StackS editor ready (' + Runtime.kind + ' runtime). Type help for console commands.', 'dim');
if(runtimeNote) log(runtimeNote, 'err');
await DevServer.start();
requestAnimationFrame(loop);
