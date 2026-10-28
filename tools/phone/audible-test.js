// WS-13: the audible and hot route buttons on the real phone page, against a fake game (like page-test.js).
// The caller sees AUDIBLE before the snap, opens the playbook and changes the play; a receiver taps a HOT route and
// taps it again to go back; everything carries the aid and goes away at the snap. Nothing is off screen.
// Run: node tools/phone/audible-test.js   (screenshots to .shots/audible-<size>-<screen>.png)
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const DIR = path.join(__dirname, '..', '..', 'Assets', 'StreamingAssets', 'phone');
const SHOTS = path.join(__dirname, '..', '..', '.shots');
const SIZES = [[844, 390], [568, 320], [390, 844]];
const TEAMS = [{ name: 'ARROWS', short: 'ARR', color: '#d7263d', color2: '#ffb612', people: 1, players: 11 },
               { name: 'GRAINS', short: 'GRN', color: '#1f7a3a', color2: '#ffffff', people: 0, players: 11 }];
const PLAYS = [{ id: 'verts', name: 'Four Verticals', desc: 'Everyone goes deep' }, { id: 'dive', name: 'HB Dive', desc: 'Straight ahead' },
               { id: 'screen', name: 'RB Screen', desc: 'Dump it off' }, { id: 'toss', name: 'Toss', desc: 'Outside run' }];
const HOT = [{ id: 'hot_slant', name: 'Hot slant', text: 'Hot route: two steps, then slant inside fast' }, { id: 'hot_go', name: 'Hot go', text: 'Hot route: run straight deep' }];
const offer = o => Object.assign({ t: 'audible', open: true, side: 'offense', caller: false, left: 0, plays: [], hot: [], mine: '', mineText: '', routes: [] }, o);

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
    const got = [];
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
        const msg = JSON.parse(m); got.push(msg);
        if (msg.t === 'join') ws.send(JSON.stringify({ t: 'joined', token: 'tk', name: msg.name, team: 0, teams: TEAMS }));
        if (msg.t === 'join') ws.send(JSON.stringify({ t: 'picked', team: 0, color: '#d7263d', color2: '#ffb612', name: 'KENNY', teamName: 'ARROWS', pos: 'QB', number: 15 }));
      });
    });
    const sendIn = o => sock.send(JSON.stringify(o));
    const shot = name => page.screenshot({ path: path.join(SHOTS, 'audible-' + tag + '-' + name + '.png') });
    const offscreen = () => page.evaluate(() => {
      const out = [];
      document.querySelectorAll('button').forEach(e => {
        const r = e.getBoundingClientRect();
        if (r.width === 0 || r.height === 0 || e.closest('.hidden')) return;
        if (r.left < -1 || r.top < -1 || r.right > innerWidth + 1 || r.bottom > innerHeight + 1) out.push((e.textContent || e.className) + ' ' + Math.round(r.left) + ',' + Math.round(r.top));
        if (!e.getAttribute('aria-label')) out.push('no aria-label: ' + e.textContent);
      });
      return out;
    });
    const check = async name => { await page.waitForTimeout(80); await shot(name); const o = await offscreen(); ok(o.length === 0, tag + ' ' + name + ': ' + o.join(' | ')); };
    const visible = sel => page.locator(sel).isVisible();

    await page.goto('http://pp.test/join/LEPT');
    await page.fill('#name', 'kenny');
    await page.click('#joinBtn');
    await page.waitForTimeout(150);

    // the QB at the line: AUDIBLE in the name plate
    sendIn({ t: 'ui', layout: 'qbpre', aid: 'qbpre7' });
    sendIn(offer({ caller: true, left: 1, plays: PLAYS }));
    await check('caller');
    ok(await visible('#audBar .aud'), tag + ' AUDIBLE button should show for the caller');
    await page.click('#audBar .aud');
    await check('sheet');
    ok(await visible('#audSheet'), tag + ' the playbook sheet should open');
    ok((await page.locator('#audSheet .play').count()) === PLAYS.length, tag + ' every play in the sheet');
    got.length = 0;
    await page.locator('#audSheet .play').nth(1).click();
    await page.waitForTimeout(50);
    ok(got.some(m => m.t === 'audible' && m.id === 'dive' && m.aid === 'qbpre7'), tag + ' audible message with the aid: ' + JSON.stringify(got));
    ok(!(await visible('#audSheet')), tag + ' the sheet closes after the pick');
    sendIn(offer({ caller: false, left: 0 }));
    await page.waitForTimeout(50);
    ok(!(await visible('#audBar')), tag + ' used up: no AUDIBLE button');
    // the QB hears which receiver went hot
    sendIn(offer({ routes: [{ spot: 1, number: 21, pos: 'RB', id: 'hot_go', name: 'Hot go' }] }));
    await page.waitForTimeout(50);
    ok((await page.locator('#audBar').textContent()).includes('RB #21 Hot go'), tag + ' the QB sees the hot route');

    // a receiver: two HOT buttons
    sendIn({ t: 'ui', layout: 'route', aid: 'route8' });
    sendIn(offer({ hot: HOT }));
    await check('receiver');
    ok((await page.locator('#audBar .hot').count()) === 2, tag + ' two hot route buttons');
    got.length = 0;
    await page.locator('#audBar .hot').nth(1).click();
    await page.waitForTimeout(50);
    ok(got.some(m => m.t === 'hot' && m.id === 'hot_go' && m.aid === 'route8'), tag + ' hot message with the aid: ' + JSON.stringify(got));
    sendIn(offer({ hot: HOT, mine: 'hot_go', mineText: 'Hot route: run straight deep' }));
    await check('receiver-hot');
    ok((await page.locator('#audBar .hot').nth(1).getAttribute('aria-pressed')) === 'true', tag + ' the chosen route is lit');
    got.length = 0;
    await page.locator('#audBar .hot').nth(1).click();
    await page.waitForTimeout(50);
    ok(got.some(m => m.t === 'hot' && m.id === ''), tag + ' tapping the lit route goes back to the called route');

    // the snap: it all goes away
    sendIn(offer({ open: false }));
    await page.waitForTimeout(50);
    ok(!(await visible('#audBar')), tag + ' nothing after the snap');
    ok(errors.length === 0, tag + ' page errors: ' + errors.join('; '));
    await ctx.close();
  }
  await browser.close();
  if (fails.length) { console.log('FAIL\n- ' + fails.join('\n- ')); process.exit(1); }
  console.log('PASS: ' + SIZES.length + ' sizes, audible sheet, hot routes on and off, aid on every press, nothing off screen');
})();
