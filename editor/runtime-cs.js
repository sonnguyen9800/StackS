// Adapter for the C# runtime compiled to WebAssembly (src/StackS.Web).
// Same interface as runtime-js.js: exec(name, args) -> {ok, res|err}, subscribe(fn), view() -> {mode, doc, sim}.
export async function createCsRuntime(base){
  const mod = await import(base + '_framework/dotnet.js');
  const { getAssemblyExports, getConfig } = await mod.dotnet.withDiagnosticTracing(false).create();
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  const B = exports.StackS.Web.Bridge;
  const subs = new Set();
  const emit = ev => subs.forEach(f => f(ev));
  let doc = null, ver = -1, mode = 'edit', sim = null;

  function refreshDoc(){ const r = JSON.parse(B.Exec('doc.get', '{}')); doc = r.res; ver = r.docVersion; }
  refreshDoc();

  function exec(name, args){
    if(name === 'tick'){ B.Tick((args && args.input) | 0); return {ok:true}; }
    const r = JSON.parse(B.Exec(name, JSON.stringify(args || {})));
    if(r.docVersion !== ver){ refreshDoc(); emit({type:'doc'}); }
    if(r.mode !== mode){ mode = r.mode; if(mode !== 'play') sim = null; emit({type:'mode'}); }
    return r.ok ? {ok:true, res:r.res} : {ok:false, err:r.err};
  }
  function view(){
    if(mode === 'play') sim = JSON.parse(B.View()).sim;
    return {mode, doc, sim};
  }
  return {kind:'csharp', exec, subscribe: f => subs.add(f), view};
}
