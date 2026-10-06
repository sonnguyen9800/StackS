// Verifies the JS reference sim against every golden replay (the C# side is checked by tests/StackS.Tests).
// Run: node tools/check-js-goldens.mjs
import { readdirSync, readFileSync } from 'node:fs';
import { replay, stateKey, validateDoc } from '../editor/sim.js';
let bad = 0, n = 0;
for(const f of readdirSync('content/replays').filter(f => f.endsWith('.replay.json')).sort()){
  const r = JSON.parse(readFileSync('content/replays/' + f, 'utf8'));
  const key = stateKey(replay(validateDoc(r.level), r.inputs, r.tune || []));
  const ok = key === r.expectedKey; n++; if(!ok) bad++;
  console.log((ok ? 'match    ' : 'MISMATCH ') + f);
}
console.log(bad ? `${bad} of ${n} differ` : `all ${n} replays match (JS)`);
process.exit(bad ? 1 : 0);
