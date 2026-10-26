// Headless check of the kick meter (phone/kick.js, WS-01) with Playwright + Chromium: it shows only for the 'kick'
// layout, a swipe up sends one { t:'kick', power, aim, aid }, a tiny swipe sends nothing, and it goes away after.
// Run: node tools/phone/kick-test.js   (screenshots to .shots/phone-kick-<size>.png)
const path = require('path');
const { chromium } = require('playwright');

(async () => {
  const fails = [];
  const ok = (cond, what) => { if (!cond) fails.push(what); };
  const browser = await chromium.launch();
  for (const [w, h] of [[390, 844], [844, 390]]) {
    const tag = w + 'x' + h;
    const page = await browser.newPage({ viewport: { width: w, height: h } });
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto('file://' + path.resolve(__dirname, 'kick-harness.html'));
    const state = () => page.evaluate(() => PPKick.state());

    ok(!(await state()).shown, tag + ': hidden before any kick');
    await page.evaluate(() => { ui('qb'); meter(false); });
    ok(!(await state()).shown, tag + ': hidden for a QB who is not kicking');

    await page.evaluate(() => { ui('kick'); meter(true, 'FIELD GOAL', 42); });
    let s = await state();
    ok(s.shown && s.kind === 'FIELD GOAL', tag + ': shown for the kicker ' + JSON.stringify(s));
    ok(await page.isVisible('.kposts'), tag + ': posts on a field goal');
    ok((await page.textContent('.ksub')).includes('42'), tag + ': says how far');
    await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-kick-' + tag + '.png') });

    const box = await page.locator('.kfield').boundingBox();
    const x0 = box.x + box.width / 2, y0 = box.y + box.height - 20;
    // a tiny swipe: nothing goes out
    await page.mouse.move(x0, y0); await page.mouse.down(); await page.mouse.move(x0, y0 - 4); await page.mouse.up();
    ok((await page.evaluate(() => window.sent.length)) === 0, tag + ': a tiny swipe does not kick');
    // a real one: up 60% of the field, a little right
    await page.mouse.move(x0, y0); await page.mouse.down();
    await page.mouse.move(x0 + box.width * 0.1, y0 - box.height * 0.3, { steps: 4 });
    await page.mouse.move(x0 + box.width * 0.1, y0 - box.height * 0.6, { steps: 4 });
    await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-kick-' + tag + '-swipe.png') });
    await page.mouse.up();
    let sent = await page.evaluate(() => window.sent);
    const aid = await page.evaluate(() => aid);
    ok(sent.length === 1, tag + ': one kick message, got ' + JSON.stringify(sent));
    const k = sent[0] || {};
    ok(k.t === 'kick' && k.aid === aid, tag + ': kick message with the aid ' + JSON.stringify(k));
    ok(k.power > 0.7 && k.power < 0.9, tag + ': power from the swipe length (0.6 / 0.75 = 0.8) ' + k.power);
    ok(k.aim > 0.1 && k.aim < 0.3, tag + ': aim to the right ' + k.aim);
    // a second swipe is ignored
    await page.mouse.move(x0, y0); await page.mouse.down(); await page.mouse.move(x0, y0 - box.height * 0.6, { steps: 4 }); await page.mouse.up();
    ok((await page.evaluate(() => window.sent.length)) === 1, tag + ': only one kick per meter');

    // the kick is away: the game sends kickmeter off and a new layout
    await page.evaluate(() => { meter(false); ui('walk'); });
    ok(!(await state()).shown, tag + ': gone after the kick');
    // a kickoff next time: fresh meter, no posts
    await page.evaluate(() => { ui('kick'); meter(true, 'KICKOFF', 0); });
    s = await state();
    ok(s.shown && !s.done && s.kind === 'KICKOFF', tag + ': a new kick gets a fresh meter ' + JSON.stringify(s));
    ok(!(await page.isVisible('.kposts')) && (await page.isVisible('.kdown')), tag + ': kickoff shows DOWNFIELD, not posts');
    ok(errors.length === 0, tag + ': page errors ' + errors.join('; '));
    await page.close();
  }
  await browser.close();
  if (fails.length) { console.log('KICK METER FAILED (' + fails.length + ')'); fails.forEach(f => console.log('  ' + f)); process.exit(1); }
  console.log('kick meter ok');
})();
