// StackS dev server. No dependencies: needs only Node 18+.
//   node devserver/serve.mjs [port]
// Serves the editor at /, the C# WebAssembly runtime bundle at /runtime/,
// and reads/writes level and replay files under content/ (your source of truth in git).
import http from 'node:http';
import { promises as fs, existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const port = Number(process.argv[2] || process.env.PORT || 5173);
const MIME = {'.html':'text/html; charset=utf-8', '.js':'text/javascript', '.mjs':'text/javascript', '.json':'application/json',
  '.wasm':'application/wasm', '.css':'text/css', '.dat':'application/octet-stream', '.dll':'application/octet-stream',
  '.pdb':'application/octet-stream', '.blat':'application/octet-stream', '.webcil':'application/octet-stream', '.map':'application/json',
  '.png':'image/png', '.svg':'image/svg+xml', '.ico':'image/x-icon'};

// Where `dotnet publish src/StackS.Web` may have put the bundle (first one containing _framework/dotnet.js wins).
const BUNDLE_CANDIDATES = ['build/web/wwwroot', 'build/web',
  'src/StackS.Web/bin/Release/net8.0/publish/wwwroot', 'src/StackS.Web/bin/Release/net9.0/publish/wwwroot',
  'src/StackS.Web/bin/Debug/net8.0/browser-wasm/AppBundle', 'src/StackS.Web/bin/Release/net8.0/browser-wasm/AppBundle',
  'src/StackS.Web/bin/Debug/net9.0/wwwroot', 'src/StackS.Web/bin/Release/net9.0/wwwroot'];
const bundleDir = () => BUNDLE_CANDIDATES.map(d => path.join(root, d)).find(d => existsSync(path.join(d, '_framework', 'dotnet.js')));

function inside(base, rel){
  const p = path.resolve(base, '.' + path.sep + rel);
  return p.startsWith(base + path.sep) || p === base ? p : null;
}
async function sendFile(res, file){
  try{
    const data = await fs.readFile(file);
    res.writeHead(200, {'Content-Type': MIME[path.extname(file)] || 'application/octet-stream', 'Cache-Control':'no-store'});
    res.end(data);
  }catch{ res.writeHead(404, {'Content-Type':'text/plain'}); res.end('not found'); }
}
const SAVE = /^\/content\/(levels\/[a-z0-9][a-z0-9_-]{0,60}\.level\.json|replays\/[a-z0-9][a-z0-9_-]{0,60}\.replay\.json)$/i;

http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const p = decodeURIComponent(url.pathname);
  try{
    if(p === '/api/ping'){ res.writeHead(200, {'Content-Type':'application/json'}); res.end('{"ok":true}'); return; }
    if(req.method === 'PUT'){
      const m = SAVE.exec(p);
      if(!m){ res.writeHead(400); res.end('only content/levels/*.level.json and content/replays/*.replay.json can be saved'); return; }
      let body = ''; for await (const chunk of req) { body += chunk; if(body.length > 20e6) throw new Error('too large'); }
      JSON.parse(body);                                   // reject anything that is not JSON
      const file = path.join(root, 'content', m[1]);
      await fs.mkdir(path.dirname(file), {recursive:true});
      await fs.writeFile(file, body.endsWith('\n') ? body : body + '\n');
      res.writeHead(204); res.end(); return;
    }
    if(p.startsWith('/content/')){ const f = inside(path.join(root, 'content'), p.slice(9)); return f ? sendFile(res, f) : (res.writeHead(403), res.end()); }
    if(p.startsWith('/runtime/')){
      const dir = bundleDir();
      if(!dir){ res.writeHead(404, {'Content-Type':'text/plain'}); res.end('C# runtime not built'); return; }
      const f = inside(dir, p.slice(9)); return f ? sendFile(res, f) : (res.writeHead(403), res.end());
    }
    const f = inside(path.join(root, 'editor'), p === '/' ? 'index.html' : p.slice(1));
    return f ? sendFile(res, f) : (res.writeHead(403), res.end());
  }catch(e){ res.writeHead(500, {'Content-Type':'text/plain'}); res.end(String(e.message || e)); }
}).listen(port, () => {
  const dir = bundleDir();
  console.log(`StackS editor:  http://localhost:${port}`);
  console.log(dir ? `C# runtime:     ${path.relative(root, dir)}` : 'C# runtime:     not built yet (editor will use the JS runtime).\n                Build: dotnet publish src/StackS.Web -c Release -o build/web');
});
