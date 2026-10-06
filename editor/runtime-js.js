// JavaScript reference runtime: same command bus contract as the C# runtime (src/StackS.Kernel/Runtime.cs).
// Used as a fallback when the C# WebAssembly bundle is not built, and to cross-check behaviour.
import { TYPES, TUNE_META, makeSim, step, stateKey, fnv, replay, validateDoc, defaultLevel } from './sim.js';

export function createJsRuntime(){
  let doc = defaultLevel();
  let undo = [], redo = [], mode = 'edit', sim = null, run = null, lastRun = null;
  const subs = new Set();
  const emit = ev => subs.forEach(f => f(ev));
  const find = id => doc.parts.find(p => p.id === id);
  function change(u, r, mergeKey){
    const last = undo.at(-1);
    if(mergeKey && last && last.k === mergeKey){ last.r = r; }
    else { undo.push({u, r, k:mergeKey}); if(undo.length > 300) undo.shift(); }
    redo = []; emit({type:'doc'});
  }
  const editOnly = () => { if(mode !== 'edit') throw new Error('stop play first'); };
  const commands = {
    'part.add': a => {
      editOnly();
      const T = TYPES[a.type]; if(!T) throw new Error('unknown type ' + a.type);
      const p = {id:'p'+doc.nextId++, type:a.type, x:+a.x||0, y:+a.y||0, z:+a.z||0, w:+a.w||T.size[0], h:+a.h||T.size[1], d:+a.d||T.size[2]};
      const target = doc;
      const ins = () => { target.parts.push(p); }, rem = () => { target.parts = target.parts.filter(q => q !== p); };
      ins(); change(rem, ins); return p.id;
    },
    'part.set': a => {
      editOnly();
      const p = find(a.id); if(!p) throw new Error('no part ' + a.id);
      const f = a.field; if(!['type','x','y','z','w','h','d'].includes(f)) throw new Error('unknown field ' + f);
      let v = a.value;
      if(f === 'type'){ if(!TYPES[v]) throw new Error('unknown type ' + v); }
      else { v = Number(v); if(!isFinite(v)) throw new Error(f + ' must be a number'); if((f==='w'||f==='h'||f==='d') && v < 0.1) v = 0.1; }
      const prev = p[f]; if(prev === v) return p.id;
      const set = x => () => { p[f] = x; };
      set(v)(); change(set(prev), set(v), 'set:' + p.id + ':' + f); return p.id;
    },
    'part.move': a => {
      editOnly();
      const p = find(a.id); if(!p) throw new Error('no part ' + a.id);
      const prev = {x:p.x, y:p.y, z:p.z}, next = {x:p.x+(+a.dx||0), y:p.y+(+a.dy||0), z:p.z+(+a.dz||0)};
      const set = o => () => { p.x = o.x; p.y = o.y; p.z = o.z; };
      set(next)(); change(set(prev), set(next), 'move:' + p.id); return p.id;
    },
    'part.remove': a => {
      editOnly();
      const i = doc.parts.findIndex(p => p.id === a.id); if(i < 0) throw new Error('no part ' + a.id);
      const p = doc.parts[i], target = doc;
      const rem = () => { target.parts = target.parts.filter(q => q !== p); }, ins = () => { target.parts.splice(Math.min(i, target.parts.length), 0, p); };
      rem(); change(ins, rem); return a.id;
    },
    'tune.set': a => {
      const m = TUNE_META[a.key]; if(!m) throw new Error('unknown tunable ' + a.key);
      let v = Number(a.value); if(!isFinite(v)) throw new Error('value must be a number');
      v = Math.min(m.max, Math.max(m.min, v));
      const prev = doc.tuning[a.key]; if(prev === v) return v;
      const target = doc;
      const set = x => () => { target.tuning[a.key] = x; if(sim) sim.tuning[a.key] = x; };
      set(v)();
      if(run) run.tune.push({tick:sim.tick, key:a.key, value:v});
      change(set(prev), set(v), 'tune:' + a.key); return v;
    },
    'doc.load': a => {
      editOnly();
      const d = validateDoc(a.doc), prev = doc;
      const set = x => () => { doc = x; };
      set(d)(); change(set(prev), set(d)); return d.parts.length + ' parts';
    },
    'doc.get': () => doc,
    'part.get': a => { const p = find(a.id); if(!p) throw new Error('no part ' + a.id); return p; },
    'undo': () => { editOnly(); const e = undo.pop(); if(!e) return 'nothing to undo'; e.u(); redo.push(e); emit({type:'doc'}); return 'undone'; },
    'redo': () => { editOnly(); const e = redo.pop(); if(!e) return 'nothing to redo'; e.r(); undo.push(e); emit({type:'doc'}); return 'redone'; },
    'play': () => {
      if(mode === 'play') return 'already playing';
      const start = JSON.parse(JSON.stringify(doc));
      mode = 'play'; sim = makeSim(start); run = {start, inputs:[], tune:[]};
      emit({type:'mode'}); return 'playing';
    },
    'stop': () => {
      if(mode !== 'play') return 'not playing';
      run.endKey = stateKey(sim); lastRun = run;
      mode = 'edit'; sim = null; run = null;
      emit({type:'mode'}); return 'stopped after ' + lastRun.inputs.length + ' ticks';
    },
    'tick': a => { if(mode !== 'play') return; const i = a.input|0; run.inputs.push(i); step(sim, i); },
    'replay.verify': () => {
      if(!lastRun) throw new Error('play the level and stop first');
      const t0 = performance.now();
      const key = stateKey(replay(lastRun.start, lastRun.inputs, lastRun.tune));
      return {ticks:lastRun.inputs.length, live:fnv(lastRun.endKey), replay:fnv(key), match:key === lastRun.endKey, ms:Math.round((performance.now()-t0)*10)/10, runtime:'js'};
    },
    'replay.export': a => {
      if(!lastRun) throw new Error('play the level and stop first');
      return JSON.stringify({version:1, name:a.name||'', level:lastRun.start, inputs:lastRun.inputs, tune:lastRun.tune, expectedKey:lastRun.endKey});
    }
  };
  function exec(name, args){
    try{
      const f = commands[name]; if(!f) throw new Error('unknown command ' + name);
      const res = f(name === 'tick' ? (args || {}) : JSON.parse(JSON.stringify(args || {})));
      return {ok:true, res:(res && typeof res === 'object') ? JSON.parse(JSON.stringify(res)) : res};
    }catch(e){ return {ok:false, err:e.message}; }
  }
  return {kind:'js', exec, subscribe: f => subs.add(f), view: () => ({mode, doc, sim})};
}
