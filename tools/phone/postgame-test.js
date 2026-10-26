// WS-12: the phone's final card against a fake game (Playwright routes HTTP and the WebSocket), at landscape and
// portrait phone sizes. Checks the card shows on `postgame`, everything on it is on screen, names stay text, HIDE
// hides it, the same card sent again stays hidden, new numbers bring it back, and a new huddle clears it.
// Run: node tools/phone/postgame-test.js   (screenshots to .shots/postgame-<size>.png)
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const DIR = path.join(__dirname, '..', '..', 'Assets', 'StreamingAssets', 'phone');
const SHOTS = path.join(__dirname, '..', '..', '.shots');
const SIZES = [[844, 390], [740, 360], [667, 375], [568, 320], [390, 844], [360, 640]];
const TEAMS = [{ name: 'CHIEFS', short: 'KC', color: '#d7263d', color2: '#ffb612', people: 1, players: 11 },
               { name: 'ROUGHRIDERS', short: 'SSK', color: '#1f7a3a', color2: '#ffffff', people: 0, players: 11 }];
const CARD = {
  t: 'postgame', winner: 0,
  teams: [{ name: 'CHIEFS', short: 'KC', color: '#d7263d', score: 28, yds: 412, tds: 4, tackles: 21, picks: 2 },
          { name: 'ROUGHRIDERS', short: 'SSK', color: '#1f7a3a', score: 21, yds: 355, tds: 3, tackles: 27, picks: 1 }],
  mvp: 'Player of the game: <B>WWWWWWWWWWWW</B> (#87 WR) · 2 TD · 131 YDS · 7 REC',
  mine: { team: 0, number: 15, pos: 'QB', yds: -4, rushYd: -4, recYd: 0, catches: 0, tackles: 0, sacks: 0, picks: 0, tds: 0, points: 0,
          line: '-4 YDS · 14/22 PASSING, 288 YDS' },
};

(async () => {
  fs.mkdirSync(SHOTS, { recursive: true });
  const fails = [];
  const ok = (c, what) => { if (!c) fails.push(what); };
  const browser = await chromium.launch();
  for (const [w, h] of SIZES) {
    const tag = w + 'x' + h;
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, hasTouch: true, isMobile: true });
    const page = await ctx.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    let sock = null;
    await page.route('http://pp.test/**', route => {
      const u = new URL(route.request().url());
      const f = u.pathname === '/' || u.pathname.startsWith('/join/') ? 'index.html' : u.pathname.slice(1);
      const file = path.join(DIR, f);
      if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: 'nope' });
      const type = f.endsWith('.js') ? 'text/javascript' : f.endsWith('.css') ? 'text/css' : 'text/html';
      route.fulfill({ status: 200, contentType: type + '; charset=utf-8', body: fs.readFileSync(file) });
    });
    await page.routeWebSocket('ws://pp.test/ws', ws => {
      sock = ws;
      ws.onMessage(m => {
        const msg = JSON.parse(m);
        if (msg.t === 'join') ws.send(JSON.stringify({ t: 'joined', token: 'tk', name: msg.name, team: 0, teams: TEAMS }));
        if (msg.t === 'join') ws.send(JSON.stringify({ t: 'picked', team: 0, color: '#d7263d', color2: '#ffb612', name: 'KENNY', teamName: 'CHIEFS', pos: 'QB', number: 15 }));
        if (msg.t === 'join') ws.send(JSON.stringify({ t: 'ui', layout: 'wait', aid: 'w1' }));
      });
    });
    const sendIn = o => sock.send(JSON.stringify(o));
    const shown = () => page.evaluate(() => { const r = document.getElementById('postgame'); return !!r && !r.classList.contains('hidden'); });
    // everything on the card is fully on screen
    const offscreen = () => page.evaluate(() => {
      const out = [];
      document.querySelectorAll('#postgame *').forEach(e => {
        const r = e.getBoundingClientRect();
        if (r.width === 0 || r.height === 0) return;
        if (r.left < -1 || r.top < -1 || r.right > innerWidth + 1 || r.bottom > innerHeight + 1)
          out.push((e.className || e.tagName) + ' ' + Math.round(r.left) + ',' + Math.round(r.top) + ' ' + Math.round(r.width) + 'x' + Math.round(r.height));
      });
      if (document.documentElement.scrollWidth > innerWidth + 1) out.push('page scrolls sideways');
      return out;
    });

    await page.goto('http://pp.test/join/LEPT');
    await page.fill('#name', 'kenny');
    await page.click('#joinBtn');
    await page.waitForTimeout(150);
    sendIn(CARD);
    await page.waitForTimeout(80);
    ok(await shown(), tag + ' the card shows on postgame');
    await page.screenshot({ path: path.join(SHOTS, 'postgame-' + tag + '.png') });
    const o = await offscreen();
    ok(o.length === 0, tag + ' off screen: ' + o.join(' | '));
    ok(await page.evaluate(() => !document.querySelector('#postgame b')), tag + ' a name is text, not HTML');
    const text = await page.evaluate(() => document.getElementById('postgame').textContent);
    ok(text.includes('CHIEFS WIN!') && text.includes('28') && text.includes('21') && text.includes('14/22 PASSING'), tag + ' card text: ' + text);
    ok(await page.evaluate(() => { const b = document.querySelector('#postgame button'); return b && /hide/i.test(b.getAttribute('aria-label') || ''); }), tag + ' HIDE names its action');
    await page.click('#postgame button');
    await page.waitForTimeout(50);
    ok(!(await shown()), tag + ' HIDE hides it');
    sendIn(CARD);
    await page.waitForTimeout(50);
    ok(!(await shown()), tag + ' the same card again stays hidden');
    sendIn(Object.assign({}, CARD, { winner: -1, teams: [Object.assign({}, CARD.teams[0], { score: 21 }), CARD.teams[1]] }));
    await page.waitForTimeout(50);
    ok(await shown(), tag + ' new numbers bring it back');
    ok((await page.evaluate(() => document.getElementById('postgame').textContent)).includes("IT'S A TIE!"), tag + ' a tie');
    sendIn({ t: 'huddle', side: 'offense', secs: 15, caller: false, callerNumber: 15, locked: false, mine: '', plays: [], spots: [], opp: [], me: 0, thumbs: {} });
    await page.waitForTimeout(50);
    ok(!(await shown()), tag + ' a new huddle clears it');
    ok(errors.length === 0, tag + ' page errors: ' + errors.join('; '));
    await ctx.close();
  }
  await browser.close();
  if (fails.length) { console.log('FAIL\n- ' + fails.join('\n- ')); process.exit(1); }
  console.log('PASS: ' + SIZES.length + ' sizes, the final card shows, fits, hides, comes back with new numbers, clears on a new huddle');
})();
