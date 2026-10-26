// The real phone page against a fake game (Playwright routes HTTP and the WebSocket): join, pick, huddle, every
// play layout, at landscape and portrait phone sizes. Checks nothing is off screen and the messages are right.
// Run: node tools/phone/page-test.js   (screenshots to .shots/page-<size>-<screen>.png)
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const DIR = path.join(__dirname, '..', '..', 'Assets', 'StreamingAssets', 'phone');
const SHOTS = path.join(__dirname, '..', '..', '.shots');
const PLAYS = path.join(__dirname, '..', '..', 'Assets', '_Project', 'Config', 'plays');
const SIZES = [[844, 390], [740, 360], [667, 375], [568, 320], [390, 844]];
const TEAMS = [{ name: 'CHIEFS', short: 'KC', color: '#d7263d', color2: '#ffb612', people: 1, players: 11 },
               { name: 'ROUGHRIDERS', short: 'SSK', color: '#1f7a3a', color2: '#ffffff', people: 0, players: 11 }];
const OFF = [['QB', 15, 5, 0], ['RB', 21, 7, 0], ['WR', 11, 0, -20], ['WR', 17, 0, 20], ['WR', 19, 1, 13], ['TE', 87, 0, -4.5],
             ['C', 60, 0, 0], ['G', 64, 0, -1.5], ['G', 66, 0, 1.5], ['T', 72, 0, -3], ['T', 76, 0, 3]]
  .map(([pos, number, back, z]) => ({ pos, number, ahead: -back, z }));
const DEF = [['MLB', 54, 5, 0], ['CB', 24, 6, -19], ['CB', 22, 6, 19], ['S', 31, 12, -9], ['S', 33, 12, 9], ['LB', 50, 5, -4.5],
             ['LB', 52, 5, 4.5], ['DL', 90, 1, -1], ['DL', 92, 1, 1], ['DL', 94, 1, -3.5], ['DL', 96, 1, 3.5]]
  .map(([pos, number, back, z]) => ({ pos, number, ahead: back, z }));
const plays = fs.readdirSync(PLAYS).filter(f => f.endsWith('.json') && f !== 'boot.json')
  .map(f => JSON.parse(fs.readFileSync(path.join(PLAYS, f), 'utf8'))).filter(p => p.side === 'offense');
const RECEIVERS = [['△', 'WR #11'], ['○', 'WR #17'], ['□', 'WR #19'], ['◇', 'TE #87'], ['☆', 'RB #21']].map(([shape, label]) => ({ shape, label }));
const LAYOUTS = ['wait', 'qbpre', 'qb', 'carrier', 'route', 'block', 'rush', 'lb', 'cover', 'kick'];

(async () => {
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
      let f = u.pathname === '/' || u.pathname.startsWith('/join/') ? 'index.html' : u.pathname.slice(1);
      const file = path.join(DIR, f);
      if (!fs.existsSync(file)) return route.fulfill({ status: 404, body: 'nope' });
      const type = f.endsWith('.js') ? 'text/javascript' : f.endsWith('.css') ? 'text/css' : 'text/html';
      route.fulfill({ status: 200, contentType: type + '; charset=utf-8', body: fs.readFileSync(file) });
    });
    await page.routeWebSocket('ws://pp.test/ws', ws => {
      sock = ws;
      ws.onMessage(m => {
        const msg = JSON.parse(m); got.push(msg);
        if (msg.t === 'join') ws.send(JSON.stringify({ t: 'joined', token: 'tk', name: msg.name, team: -1, teams: TEAMS }));
        if (msg.t === 'join') ws.send(JSON.stringify({ t: 'ui', layout: 'pick', aid: 'p1' }));
        if (msg.t === 'pick') ws.send(JSON.stringify({ t: 'picked', team: 0, color: '#d7263d', color2: '#ffb612', name: 'KENNY', teamName: 'CHIEFS', pos: 'QB', number: 15 }));
      });
    });
    const sendIn = o => sock.send(JSON.stringify(o));
    const shot = name => page.screenshot({ path: path.join(SHOTS, 'page-' + tag + '-' + name + '.png') });
    // every visible interactive thing must be fully on screen
    const offscreen = () => page.evaluate(() => {
      const out = [];
      document.querySelectorAll('button, .stick, .tstick, input, .kickpad').forEach(e => {
        const r = e.getBoundingClientRect();
        if (r.width === 0 || r.height === 0 || getComputedStyle(e).visibility === 'hidden') return;
        if (e.closest('.hidden')) return;
        if (r.left < -1 || r.top < -1 || r.right > innerWidth + 1 || r.bottom > innerHeight + 1)
          out.push((e.id || e.className || e.tagName) + ' ' + Math.round(r.left) + ',' + Math.round(r.top) + ' ' + Math.round(r.width) + 'x' + Math.round(r.height));
      });
      if (document.documentElement.scrollWidth > innerWidth + 1) out.push('page scrolls sideways');
      return out;
    });
    const check = async name => { await page.waitForTimeout(80); await shot(name); const o = await offscreen(); ok(o.length === 0, tag + ' ' + name + ' off screen: ' + o.join(' | ')); };

    await page.goto('http://pp.test/join/LEPT');
    await check('join');
    await page.fill('#name', 'kenny');
    await page.click('#joinBtn');
    await page.waitForTimeout(150);
    ok(got.some(m => m.t === 'join' && m.code === 'LEPT' && m.name === 'KENNY'), tag + ' join message: ' + JSON.stringify(got));
    await check('pick');
    await page.locator('.team').first().click();
    sendIn({ t: 'status', phase: 'huddle', secs: 15, called: [false, false], score: [0, 0], down: '1ST & 10', tv: 'HUDDLE' });
    sendIn({ t: 'huddle', side: 'offense', secs: 15, caller: true, callerNumber: 15, locked: false, mine: '', plays, spots: OFF, opp: DEF, me: 0, thumbs: { slants: 2 } });
    sendIn({ t: 'ui', layout: 'huddle', aid: 'h1' });
    await check('huddle');
    // pick a play and lock it in
    const second = page.locator('#hPlays .play').nth(1);
    await second.click();
    await check('huddle-picked');
    got.length = 0;
    await page.click('#hGo');
    await page.waitForTimeout(80);
    ok(got.some(m => m.t === 'lock' && m.id === plays[1].id && m.aid === 'h1'), tag + ' lock message: ' + JSON.stringify(got));
    sendIn({ t: 'job', side: 'offense', play: plays[1], spots: OFF, opp: DEF, me: 0 });
    for (const L of LAYOUTS) {
      sendIn({ t: 'ui', layout: L, aid: L + '1', receivers: L === 'qb' ? RECEIVERS : undefined, throwStick: L === 'qb' ? true : undefined });
      sendIn({ t: 'status', phase: 'set', secs: 0, called: [true, true], score: [6, 0], down: '2ND & 6', tv: 'TACKLED! FIRST DOWN' });
      await check(L);
    }
    // the throw stick: a quick flick is a bullet, a long hold a lob, letting go in the middle cancels
    sendIn({ t: 'ui', layout: 'qb', aid: 'q9', receivers: RECEIVERS, throwStick: true });
    await page.waitForTimeout(50);
    const ts = await page.locator('.tstick').boundingBox();
    const cx = ts.x + ts.width / 2, cy = ts.y + ts.height / 2, up = cx + ts.width * 0.45;
    const throwWith = async (holdMs, toX) => {
      got.length = 0;
      await page.locator('.tstick').dispatchEvent('pointerdown', { pointerId: 7, clientX: toX, clientY: cy - 10 });
      if (holdMs) await page.waitForTimeout(holdMs);
      await page.locator('.tstick').dispatchEvent('pointerup', { pointerId: 7, clientX: toX, clientY: cy - 10 });
      await page.waitForTimeout(30);
      return got.filter(m => m.t === 'throw' || (m.t === 'aim' && m.cancel)).pop();
    };
    let m = await throwWith(0, up);
    ok(m && m.t === 'throw' && m.arc === 0 && m.x > 0.7 && m.aid === 'q9', tag + ' flick should be a bullet: ' + JSON.stringify(m));
    ok(got.some(g => g.t === 'aim' && !g.cancel), tag + ' aiming should stream aim messages');
    m = await throwWith(900, up);
    ok(m && m.t === 'throw' && m.arc === 1, tag + ' a long hold should be a full lob: ' + JSON.stringify(m));
    m = await throwWith(100, cx + 2);
    ok(m && m.t === 'aim' && m.cancel === 1, tag + ' letting go in the middle should cancel: ' + JSON.stringify(m));
    ok(errors.length === 0, tag + ' page errors: ' + errors.join('; '));
    await ctx.close();
  }
  await browser.close();
  if (fails.length) { console.log('FAIL\n- ' + fails.join('\n- ')); process.exit(1); }
  console.log('PASS: ' + SIZES.length + ' sizes, join, pick, huddle, lock in, ' + LAYOUTS.length + ' layouts, nothing off screen, throw stick flick/lob/cancel');
})();
