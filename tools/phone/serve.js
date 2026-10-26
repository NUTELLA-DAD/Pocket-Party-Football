// A test server for the phone page: serves Assets/StreamingAssets/phone on http://127.0.0.1:8099, and with ?fake=1
// it swaps the game's WebSocket for a stand-in the test drives (window.__push(msg) plays a game message,
// window.__sent lists what the phone sent). With window.__fit() the page reports every control that is not fully
// on screen. Used by tools/phone/fit-check (in the built-in browser or Playwright) to check the controller at real
// phone sizes, sideways first. Run: node tools/phone/serve.js
const http = require('http'), fs = require('fs'), path = require('path');
const dir = path.resolve(__dirname, '../../Assets/StreamingAssets/phone');
const types = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml' };

const fake = `
(function () {
  window.__sent = [];
  function FakeWS() { var s = this; s.readyState = 0; window.__ws = s; setTimeout(function () { s.readyState = 1; if (s.onopen) s.onopen(); }, 0); }
  FakeWS.prototype.send = function (d) { window.__sent.push(JSON.parse(d)); };
  FakeWS.prototype.close = function () {};
  window.WebSocket = FakeWS;
  window.__push = function (m) { if (window.__ws && window.__ws.onmessage) window.__ws.onmessage({ data: JSON.stringify(m) }); };
  // every visible control (buttons, inputs, the stick) must lie fully inside the viewport
  window.__fit = function () {
    var bad = [], W = innerWidth, H = innerHeight;
    document.querySelectorAll('button, input, .stick, .kickpad').forEach(function (e) {
      if (!e.offsetParent && getComputedStyle(e).position !== 'fixed') return;   // hidden
      var r = e.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) return;
      // inside a row that swipes sideways (the huddle's play cards): fine if the row itself is on screen
      for (var p = e.parentElement; p; p = p.parentElement) {
        if (p.classList && p.classList.contains('plays')) { var pr = p.getBoundingClientRect(); if (pr.left >= -0.5 && pr.right <= W + 0.5 && pr.top >= -0.5 && pr.bottom <= H + 0.5) return; }
      }
      if (r.left < -0.5 || r.top < -0.5 || r.right > W + 0.5 || r.bottom > H + 0.5)
        bad.push((e.id || e.getAttribute('data-act') || e.className || e.tagName) + ' ' + Math.round(r.left) + ',' + Math.round(r.top) + ' ' + Math.round(r.width) + 'x' + Math.round(r.height));
      // and not clipped by a scrolling parent on the play screen
    });
    return { size: W + 'x' + H, bad: bad };
  };
})();`;

http.createServer((req, res) => {
  const url = new URL(req.url, 'http://x');
  let p = url.pathname;
  if (p === '/' || p.startsWith('/join/')) p = '/index.html';
  if (p === '/__fitcheck.js') { res.writeHead(200, { 'Content-Type': 'application/javascript', 'Cache-Control': 'no-store' }); res.end(fs.readFileSync(path.join(__dirname, 'fit-check.js'))); return; }
  const file = path.join(dir, path.basename(p));
  if (!file.startsWith(dir) || !fs.existsSync(file)) { res.writeHead(404); res.end(); return; }
  let body = fs.readFileSync(file);
  if (p === '/index.html' && url.searchParams.get('fake') === '1')
    body = Buffer.from(body.toString().replace('<script src="/layouts.js">', '<script>' + fake + '</script>\n<script src="/__fitcheck.js"></script>\n<script src="/layouts.js">'));
  res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  res.end(body);
}).listen(8099, '127.0.0.1', () => console.log('phone test server on http://127.0.0.1:8099/join/TEST?fake=1'));
