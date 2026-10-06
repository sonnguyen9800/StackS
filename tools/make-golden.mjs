// Generates golden replays with the JS reference sim. The C# tests must reproduce every
// expectedKey bit-for-bit. Run: node tools/make-golden.mjs
import { writeFileSync, mkdirSync } from 'node:fs';
import { defaultLevel, replay, stateKey } from '../editor/sim.js';

let seed = 12345;
const rnd = () => { seed = (Math.imul(seed, 1103515245) + 12345) >>> 0; return seed / 4294967296; };

function scripted(n){
  const inputs = []; let cur = 0, left = 0;
  for(let i = 0; i < n; i++){
    if(left <= 0){ cur = Math.floor(rnd()*16); left = 10 + Math.floor(rnd()*50); }
    let b = cur;
    if(rnd() < 0.08) b |= 16;
    inputs.push(b); left--;
  }
  return inputs;
}

const route = [];   // a hand-made route: right, forward, jumps onto the first platforms
const hold = (bits, n) => { for(let i = 0; i < n; i++) route.push(bits); };
hold(2, 60); hold(4, 50); hold(4|16, 20); hold(4, 30); hold(2|4|16, 25); hold(2|4, 40); hold(16, 15); hold(0, 60);

const cases = [
  {name:'route-default', inputs:route, tune:[]},
  {name:'random-2000', inputs:scripted(2000), tune:[]},
  {name:'random-live-tuning', inputs:scripted(1500), tune:[{tick:300, key:'jumpHeight', value:3.4}, {tick:700, key:'moveSpeed', value:9.5}, {tick:1100, key:'coyoteTicks', value:2}]},
];
mkdirSync('content/replays', {recursive:true});
for(const c of cases){
  const level = defaultLevel();
  const s = replay(level, c.inputs, c.tune);
  const file = {version:1, name:c.name, level, inputs:c.inputs, tune:c.tune, expectedKey:stateKey(s)};
  writeFileSync(`content/replays/${c.name}.replay.json`, JSON.stringify(file));
  console.log(c.name, 'ticks', c.inputs.length, 'coins', s.coins.length, 'deaths', s.deaths, 'done', s.done);
}
writeFileSync('content/levels/starter.level.json', JSON.stringify(defaultLevel(), null, 2));
