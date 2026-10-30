// FEATURES.customPlays: DRAW YOUR OWN on the real phone page, against a fake game (like playbook-shots.js).
// The caller opens the board from the PASS tab, drags a route from two receivers, taps one to erase it, changes the
// formation, draws again and LOCKS IN: one custom {base, routes, aid} message with play-frame points, thinned and
// kept on the field. A teammate who is not the caller gets no DRAW card. Nothing is off screen.
// Run: node tools/phone/customplay-test.js   (screenshots to .shots/customplay-<size>-<step>.png)
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const DIR = path.join(__dirname, '..', '..', 'Assets', 'StreamingAssets', 'phone');
const SHOTS = path.join(__dirname, '..', '..', '.shots');
const PLAYS = path.join(__dirname, '..', '..', 'Assets', '_Project', 'Config', 'plays');
const GAME = JSON.parse(fs.readFileSync(path.join(PLAYS, '..', 'game.json'), 'utf8'));
const SIZES = [[844, 390], [568, 320], [390, 844]];
const TEAMS = [{ name: 'ARROWS', short: 'ARR', color: '#d7263d', color2: '#ffb612', people: 1, players: 11 },
               { name: 'GRAINS', short: 'GRN', color: '#1f7a3a', color2: '#ffffff', people: 0, players: 11 }];
const OFF = [['QB', 15, 5, 0], ['RB', 21, 7, 0], ['WR', 11, 0, -20], ['WR', 17, 0, 20], ['WR', 19, 1, 13], ['TE', 87, 0, -4.5],
             ['C', 60, 0, 0], ['G', 64, 0, -1.5], ['G', 66, 0, 1.5], ['T', 72, 0, -3], ['T', 76, 0, 3]]
  .map(([pos, number, back, z]) => ({ pos, number, ahead: -back, z }));
const DEF = [['MLB', 54, 5, 0], ['CB', 24, 6, -19], ['CB', 22, 6, 19], ['S', 31, 12, -9], ['S', 33, 12, 9], ['LB', 50, 5, -4.5],
             ['LB', 52, 5, 4.5], ['DL', 90, 1, -1], ['DL', 92, 1, 1], ['DL', 94, 1, -3.5], ['DL', 96, 1, 3.5]]
  .map(([pos, number, back, z]) => ({ pos, number, ahead: back, z }));
const SETS = GAME.formation.sets || {};
function withSpots(p) {
  const set = SETS[p.formation];
  if (!set || set.side !== p.side || set.spots.length !== OFF.length) return p;
  const spots = set.spots.map(([back, z], i) => ({ pos: OFF[i].pos, number: OFF[i].number, ahead: -back, z }));
  return Object.assign({}, p, { spots, formationName: set.name });
}
const plays = fs.readdirSync(PLAYS).filter(f => f.endsWith('.json') && f !== 'boot.json')
  .map(f => JSON.parse(fs.readFileSync(path.join(PLAYS, f), 'utf8'))).filter(p => p.side === 'offense').map(withSpots);
const C = GAME.customPlays, RECV = GAME.play.receivers;
const MAXZ = GAME.field.widthYd / 2 - C.sidelineInYd;
const DRAW = { bases: ['slants', 'verts', 'screen'], receivers: RECV, maxPoints: C.maxPoints, maxAheadYd: C.maxAheadYd, maxBackYd: C.maxBackYd, maxZYd: MAXZ, name: C.name };
const huddle = o => Object.assign({ t: 'huddle', side: 'offense', secs: 15, caller: true, callerNumber: 15, locked: false, mine: '', plays, spots: OFF, opp: DEF, me: 0, thumbs: {}, draw: DRAW }, o);

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
      if (f.endsWith('.png')) return route.fulfill({ status: 200, contentType: 'image/png', body: fs.readFileSync(file) });
      if (f.endsWith('.ttf')) return route.fulfill({ status: 200, contentType: 'font/ttf', body: fs.readFileSync(file) });
      const type = f.endsWith('.js') ? 'text/javascript' : f.endsWith('.css') ? 'text/css' : 'text/html';
      route.fulfill({ status: 200, contentType: type + '; charset=utf-8', body: fs.readFileSync(file) });
    });
    await page.routeWebSocket('ws://pp.test/ws', ws => {
      sock = ws;
      ws.onMessage(m => {
        const msg = JSON.parse(m); got.push(msg);
        if (msg.t === 'join') ws.send(JSON.stringify({ t: 'joined', token: 'tk', name: msg.name, team: 0, teams: TEAMS }));
      });
    });
    const sendIn = o => sock.send(JSON.stringify(o));
    const shot = name => page.screenshot({ path: path.join(SHOTS, 'customplay-' + tag + '-' + name + '.png') });
    const offscreen = () => page.evaluate(() => {
      const out = [];
      document.querySelectorAll('button').forEach(e => {
        const r = e.getBoundingClientRect();
        if (r.width === 0 || r.height === 0 || e.closest('.hidden') || e.closest('#hPlays')) return;   // (the cards scroll: swipe for more)
        if (r.left < -1 || r.top < -1 || r.right > innerWidth + 1 || r.bottom > innerHeight + 1) out.push((e.textContent || e.className) + ' ' + Math.round(r.left) + ',' + Math.round(r.top));
        if (!e.getAttribute('aria-label')) out.push('no aria-label: ' + e.textContent);
      });
      return out;
    });
    const check = async name => { await page.waitForTimeout(80); await shot(name); const o = await offscreen(); ok(o.length === 0, tag + ' ' + name + ': ' + o.join(' | ')); };
    const visible = sel => page.locator(sel).isVisible();
    // a receiver's spot on the board, in page pixels
    const spotAt = (i, base) => page.evaluate(([i, base]) => {
      const svg = document.querySelector('#hDrawField svg');
      return null || (function () {
        const H = window.__ppTestSpots[base][i], c = svg.ppXY(H.ahead, H.z), pt = svg.createSVGPoint();
        pt.x = c[0]; pt.y = c[1];
        const q = pt.matrixTransform(svg.getScreenCTM());
        return [q.x, q.y];
      })();
    }, [i, base]);
    const drag = async (from, dx, dy, steps) => {
      await page.mouse.move(from[0], from[1]);
      await page.mouse.down();
      for (const [x, y] of steps.map(([a, b]) => [from[0] + a * dx, from[1] + b * dy])) await page.mouse.move(x, y, { steps: 6 });
      await page.mouse.up();
      await page.waitForTimeout(40);
    };

    await page.goto('http://pp.test/join/LEPT');
    await page.fill('#name', 'kenny');
    await page.click('#joinBtn');
    await page.waitForTimeout(150);
    await page.evaluate(p => { window.__ppTestSpots = {}; p.forEach(x => { window.__ppTestSpots[x.id] = x.spots; }); }, plays);

    // a teammate who is not the caller: no DRAW card
    sendIn({ t: 'ui', layout: 'huddle', aid: 'h1' });
    sendIn(huddle({ caller: false, draw: null }));
    await page.waitForTimeout(80);
    ok((await page.locator('#hPlays .drawcard').count()) === 0, tag + ' no DRAW card for a teammate');

    // the caller: the DRAW card is first on the PASS tab
    sendIn({ t: 'ui', layout: 'huddle', aid: 'h2' });
    sendIn(huddle({}));
    await check('playbook');
    ok((await page.locator('#hPlays .play').first().getAttribute('class')).includes('drawcard'), tag + ' DRAW YOUR OWN is the first card');
    await page.click('#hPlays .drawcard');
    await check('board');
    ok(await visible('#hDraw'), tag + ' the board opens');
    ok(await page.locator('#hDrawGo').isDisabled(), tag + ' nothing to lock in yet');

    // draw two routes (a wide receiver on a post, the slot on an out), then tap the second to erase it
    const box = await page.locator('#hDrawField').boundingBox();
    const ux = box.width / 12, uy = box.height / 10;
    await drag(await spotAt(2, 'slants'), ux, uy, [[0, -2], [0, -3.5], [1.5, -5]]);
    await drag(await spotAt(4, 'slants'), ux, uy, [[0, -1.5], [-1.5, -1.6]]);
    await check('drawn');
    ok(!(await page.locator('#hDrawGo').isDisabled()), tag + ' LOCK IN lights up after a route');
    ok((await page.locator('#hDrawLine').textContent()).startsWith('2 routes'), tag + ' two routes: ' + await page.locator('#hDrawLine').textContent());
    const s4 = await spotAt(4, 'slants');
    await page.mouse.click(s4[0], s4[1]);
    await page.waitForTimeout(40);
    ok((await page.locator('#hDrawLine').textContent()).startsWith('1 route '), tag + ' a tap erases his route: ' + await page.locator('#hDrawLine').textContent());

    // a drag from the open field draws nothing
    await drag([box.x + box.width / 2, box.y + 8], ux, uy, [[1, 1]]);
    ok((await page.locator('#hDrawLine').textContent()).startsWith('Start on'), tag + ' a drag off any receiver is a hint, not a route');

    // the next formation clears the board
    await page.click('#hDrawForm');
    await page.waitForTimeout(40);
    ok(await page.locator('#hDrawGo').isDisabled(), tag + ' a new formation starts clean');
    ok((await page.locator('#hDrawForm').textContent()).toUpperCase().includes(SETS.spread.name.toUpperCase()), tag + ' the next base is Four Verticals\' spread: ' + await page.locator('#hDrawForm').textContent());
    await drag(await spotAt(3, 'verts'), ux, uy, [[0, -1], [-0.5, -2], [-1, -2.2], [-1.3, -2.2], [-1, -3], [-1, -6]]);
    await check('verts');

    got.length = 0;
    await page.click('#hDrawGo');
    await page.waitForTimeout(60);
    const sent = got.find(m => m.t === 'custom');
    ok(!!sent, tag + ' LOCK IN sends custom: ' + JSON.stringify(got));
    if (sent) {
      ok(sent.base === 'verts' && sent.aid === 'h2', tag + ' base and aid: ' + JSON.stringify(sent));
      ok(sent.routes.length === 1 && sent.routes[0].spot === 3, tag + ' one route for spot 3: ' + JSON.stringify(sent.routes));
      const pts = sent.routes[0].pts;
      ok(pts.length >= 1 && pts.length <= C.maxPoints, tag + ' 1..' + C.maxPoints + ' points: ' + pts.length);
      ok(pts.every(([a, z]) => a >= -C.maxBackYd && a <= C.maxAheadYd && Math.abs(z) <= MAXZ + 0.05), tag + ' every point on the field: ' + JSON.stringify(pts));
      ok(pts[pts.length - 1][0] > 5, tag + ' the route goes downfield: ' + JSON.stringify(pts));
    }
    // the game says locked: the board closes
    sendIn({ t: 'thumbs', counts: {}, mine: '', locked: true });
    await page.waitForTimeout(60);
    ok(!(await visible('#hDraw')), tag + ' locked: the board closes');
    // a teammate joins mid-drawing: the huddle comes again, the drawing stays
    sendIn({ t: 'ui', layout: 'huddle', aid: 'h3' });
    sendIn(huddle({}));
    await page.click('#hPlays .drawcard');
    await page.waitForTimeout(40);
    await drag(await spotAt(2, 'slants'), ux, uy, [[0, -2], [1, -3]]);
    sendIn(huddle({}));
    await page.waitForTimeout(60);
    ok(await visible('#hDraw') && !(await page.locator('#hDrawGo').isDisabled()), tag + ' a repeat huddle keeps the board and the route');
    // after the play is called, the next down: a clean board
    sendIn({ t: 'job', side: 'offense', play: plays.find(p => p.id === 'slants'), spots: OFF, opp: DEF, me: 0 });
    sendIn({ t: 'ui', layout: 'huddle', aid: 'h4' });
    sendIn(huddle({}));
    await page.click('#hPlays .drawcard');
    await page.waitForTimeout(40);
    ok(await page.locator('#hDrawGo').isDisabled(), tag + ' the next down starts with a clean board');

    // the receiver's job card shows the drawn route (Messages.JobFor puts it in place of the base play's)
    const verts = plays.find(p => p.id === 'verts');
    const jobs = verts.jobs.slice(); jobs[3] = { kind: 'route', pts: sent ? sent.routes[0].pts : [[10, 10]], text: C.text };
    sendIn({ t: 'job', side: 'offense', play: Object.assign({}, verts, { jobs, name: C.name, custom: true }), spots: verts.spots, opp: DEF, me: 3 });
    sendIn({ t: 'ui', layout: 'wait', aid: 'r1' });
    await page.waitForTimeout(80);
    await shot('job');
    ok((await page.locator('#jobText').textContent()) === C.text, tag + ' the job line says the caller drew it');
    ok(errors.length === 0, tag + ' page errors: ' + errors.join('; '));
    await ctx.close();
  }
  await browser.close();
  if (fails.length) { console.log('FAIL\n- ' + fails.join('\n- ')); process.exit(1); }
  console.log('PASS: ' + SIZES.length + ' sizes, draw, erase, change formation, lock in with the aid, points on the field, nothing off screen');
})();
