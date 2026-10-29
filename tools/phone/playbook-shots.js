// The playbook pictures: every play in the huddle preview (offense and defense) and the in-play job card,
// on the real phone page against a fake game. Checks there are no page errors and each picture fills its box.
// Run: node tools/phone/playbook-shots.js   (screenshots to .shots/playbook-<size>-<play>.png)
const fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const DIR = path.join(__dirname, '..', '..', 'Assets', 'StreamingAssets', 'phone');
const SHOTS = path.join(__dirname, '..', '..', '.shots');
const PLAYS = path.join(__dirname, '..', '..', 'Assets', '_Project', 'Config', 'plays');
const SIZES = [[844, 390], [568, 320], [390, 844]];
const TEAMS = [{ name: 'ARROWS', short: 'ARR', color: '#d7263d', color2: '#ffb612', people: 1, players: 11 },
               { name: 'GRAINS', short: 'GRN', color: '#1f7a3a', color2: '#ffffff', people: 0, players: 11 }];
const OFF = [['QB', 15, 5, 0], ['RB', 21, 7, 0], ['WR', 11, 0, -20], ['WR', 17, 0, 20], ['WR', 19, 1, 13], ['TE', 87, 0, -4.5],
             ['C', 60, 0, 0], ['G', 64, 0, -1.5], ['G', 66, 0, 1.5], ['T', 72, 0, -3], ['T', 76, 0, 3]]
  .map(([pos, number, back, z]) => ({ pos, number, ahead: -back, z }));
const DEF = [['MLB', 54, 5, 0], ['CB', 24, 6, -19], ['CB', 22, 6, 19], ['S', 31, 12, -9], ['S', 33, 12, 9], ['LB', 50, 5, -4.5],
             ['LB', 52, 5, 4.5], ['DL', 90, 1, -1], ['DL', 92, 1, 1], ['DL', 94, 1, -3.5], ['DL', 96, 1, 3.5]]
  .map(([pos, number, back, z]) => ({ pos, number, ahead: back, z }));
const all = fs.readdirSync(PLAYS).filter(f => f.endsWith('.json') && f !== 'boot.json')
  .map(f => JSON.parse(fs.readFileSync(path.join(PLAYS, f), 'utf8')));
const ME = { offense: 2, defense: 1 };   // a wide receiver; a cornerback

(async () => {
  const fails = [];
  const ok = (c, what) => { if (!c) fails.push(what); };
  fs.mkdirSync(SHOTS, { recursive: true });
  const browser = await chromium.launch();
  for (const [w, h] of SIZES) {
    const tag = w + 'x' + h;
    for (const side of ['offense', 'defense']) {
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
        if (f.endsWith('.png')) return route.fulfill({ status: 200, contentType: 'image/png', body: fs.readFileSync(file) });
        if (f.endsWith('.ttf')) return route.fulfill({ status: 200, contentType: 'font/ttf', body: fs.readFileSync(file) });
        const type = f.endsWith('.js') ? 'text/javascript' : f.endsWith('.css') ? 'text/css' : 'text/html';
        route.fulfill({ status: 200, contentType: type + '; charset=utf-8', body: fs.readFileSync(file) });
      });
      await page.routeWebSocket('ws://pp.test/ws', ws => {
        sock = ws;
        ws.onMessage(m => {
          const msg = JSON.parse(m);
          if (msg.t === 'join') ws.send(JSON.stringify({ t: 'joined', token: 'tk', name: msg.name, team: 0, teams: TEAMS }));
        });
      });
      await page.goto('http://pp.test/join/LEPT');
      await page.fill('#name', 'kenny');
      await page.click('#joinBtn');
      await page.waitForTimeout(150);
      const send = o => sock.send(JSON.stringify(o));
      const plays = all.filter(p => p.side === side);
      const spots = side === 'offense' ? OFF : DEF, opp = side === 'offense' ? DEF : OFF, me = ME[side];
      send({ t: 'ui', layout: 'huddle', aid: 'h1' });
      send({ t: 'huddle', side, secs: 15, caller: true, callerNumber: 15, locked: false, mine: '', plays, spots, opp, me, thumbs: {} });
      await page.waitForTimeout(80);
      // every tab, every card: the picture fills the card's grass box
      const tabs = Math.max(1, (await page.$$('#hTabs .htab')).length);
      let seen = 0;
      for (let t = 0; t < tabs; t++) {
        if (await page.isVisible('#hTabs')) await page.locator('#hTabs .htab').nth(t).click();
        await page.waitForTimeout(60);
        const cards = page.locator('#hPlays .play');
        const n = await cards.count();
        for (let i = 0; i < n; i++) {
          const id = await cards.nth(i).getAttribute('data-id');
          await cards.nth(i).click();
          await page.waitForTimeout(40);
          const fit = await cards.nth(i).evaluate(c => {
            const f = c.querySelector('.pf'), s = f && f.querySelector('svg');
            if (!s) return 'no picture';
            const a = f.getBoundingClientRect(), b = s.getBoundingClientRect();
            return Math.abs(a.width - b.width) <= 2 && Math.abs(a.height - b.height) <= 2 ? '' : 'box ' + Math.round(a.width) + 'x' + Math.round(a.height) + ' picture ' + Math.round(b.width) + 'x' + Math.round(b.height);
          });
          ok(!fit, tag + ' ' + id + ' card picture does not fill its box: ' + fit);
          await cards.nth(i).screenshot({ path: path.join(SHOTS, 'playbook-' + tag + '-' + id + '.png') });
          seen++;
        }
      }
      ok(seen === plays.length, tag + ' ' + side + ': ' + seen + ' of ' + plays.length + ' plays on the tabs');
      await page.screenshot({ path: path.join(SHOTS, 'playbook-' + tag + '-huddle-' + side + '.png') });
      send({ t: 'job', side, play: plays[0], spots, opp, me });
      send({ t: 'ui', layout: 'wait', aid: 'r1' });   // the job card shows before the snap
      await page.waitForTimeout(80);
      await page.screenshot({ path: path.join(SHOTS, 'playbook-' + tag + '-job-' + side + '.png') });
      ok(errors.length === 0, tag + ' ' + side + ' page errors: ' + errors.join('; '));
      await ctx.close();
    }
  }
  await browser.close();
  if (fails.length) { console.log('FAIL\n- ' + fails.join('\n- ')); process.exit(1); }
  console.log('PASS: ' + all.length + ' plays at ' + SIZES.length + ' sizes, every card picture fills its box, no page errors');
})();
