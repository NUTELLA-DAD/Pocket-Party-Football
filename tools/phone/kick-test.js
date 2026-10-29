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

    // a field goal with two taps (kickSweep): tap = the aim (and the snap), tap again = the power
    await page.evaluate(() => { meter(false); ui('walk'); window.sent = []; ui('kick'); sweepMeter('aim'); });
    s = await state();
    ok(s.shown && s.sweep && s.stage === 'aim', tag + ': the aim sweeps ' + JSON.stringify(s));
    ok(await page.isVisible('.ktap'), tag + ': a big TAP button');
    ok((await page.getAttribute('.ktap', 'aria-label')).length > 0, tag + ': the tap button names its action');
    ok((await page.textContent('.kwind')).includes('11 mph'), tag + ': the wind is shown');
    await page.waitForTimeout(300);
    await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-kick-' + tag + '-aim.png') });
    await page.locator('.ktap').dispatchEvent('pointerdown', { pointerId: 3 });
    sent = await page.evaluate(() => window.sent);
    const aimAid = await page.evaluate(() => aid);
    ok(sent.length === 1 && sent[0].t === 'kickaim' && sent[0].aid === aimAid, tag + ': the first tap sends the aim ' + JSON.stringify(sent));
    ok(sent[0] && sent[0].aim > 0.2 && sent[0].aim <= 1, tag + ': 0.3 s in, the arrow had swung right ' + (sent[0] && sent[0].aim));
    s = await state();
    ok(s.stage === 'power', tag + ': then the power sweeps');
    // the game answers with its own stage: nothing restarts
    await page.evaluate(a => sweepMeter('power', a), sent[0] ? sent[0].aim : 0);
    await page.waitForTimeout(500);
    await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-kick-' + tag + '-power.png') });
    await page.locator('.kfield').dispatchEvent('pointerdown', { pointerId: 4 });
    sent = await page.evaluate(() => window.sent);
    ok(sent.length === 2 && sent[1].t === 'kick' && sent[1].aid === aimAid, tag + ': the second tap kicks ' + JSON.stringify(sent));
    ok(sent[1] && sent[1].power > 0.45 && sent[1].power <= 0.8, tag + ': half a second up the sweep ' + (sent[1] && sent[1].power));
    ok(sent[1] && sent[1].aim === sent[0].aim, tag + ': with the aim from the first tap');
    await page.locator('.kfield').dispatchEvent('pointerdown', { pointerId: 5 });
    ok((await page.evaluate(() => window.sent.length)) === 2, tag + ': one kick');
    // the run-up: the game still says on (no sweep): the phone stays on "kicked"
    await page.evaluate(() => meter(true, 'FIELD GOAL', 42));
    s = await state();
    ok(s.done && s.shown, tag + ': still showing the kick while he runs up');
    await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-kick-' + tag + '-kicked.png') });
    // too slow: the game snapped it and says power first
    await page.evaluate(() => { meter(false); ui('walk'); window.sent = []; ui('kick'); sweepMeter('aim'); sweepMeter('power', -0.4); });
    s = await state();
    ok(s.stage === 'power' && !s.done, tag + ': the game snapped it: straight to the power ' + JSON.stringify(s));
    await page.locator('.ktap').dispatchEvent('pointerdown', { pointerId: 6 });
    sent = await page.evaluate(() => window.sent);
    ok(sent.length === 1 && sent[0].t === 'kick' && sent[0].aim === -0.4, tag + ': kicks with the game\'s aim ' + JSON.stringify(sent));
    // kickSync: the arrow runs on the game's clock. 0.55 s in, the game's arrow is all the way right (aim 1)
    await page.evaluate(() => { meter(false); ui('walk'); window.sent = []; ui('kick'); syncMeter('aim', 0.55); syncMeter('aim', 0.55); });
    await page.waitForTimeout(300);
    // (the same t twice: the game's clock stands still, so the arrow holds at full right)
    const lean = await page.evaluate(() => document.querySelector('.kaimline').style.transform);
    ok(/rotate\(15deg\)/.test(lean), tag + ': the arrow leans as far as the kick really goes (15 deg), on the game\'s clock: ' + lean);
    const postsW = await page.evaluate(() => parseFloat(document.querySelector('.kposts').style.width));
    const fieldH = (await page.locator('.kfield').boundingBox()).height;
    const want = 2 * Math.max(10, Math.max(40, fieldH - 8 - 60) * 0.69 * 3.08 / 42);
    ok(Math.abs(postsW - want) < 6, tag + ': the posts drawn to scale with the arrow ' + postsW + ' vs ' + want);
    await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-kick-' + tag + '-synced.png') });
    await page.locator('.ktap').dispatchEvent('pointerdown', { pointerId: 7 });
    sent = await page.evaluate(() => window.sent);
    ok(sent.length === 1 && sent[0].t === 'kickaim' && sent[0].st === 0.55 && sent[0].aim === 1, tag + ': the tap says which second it showed ' + JSON.stringify(sent));
    // a late word about the aim (sent before the game had the tap) changes nothing; the game's power clock is taken
    await page.evaluate(() => syncMeter('aim', 0.8));
    ok((await state()).stage === 'power', tag + ': a late aim sync does not undo the tap');
    await page.evaluate(() => { syncMeter('power', 0.4, 1); syncMeter('power', 0.4, 1); });
    await page.locator('.ktap').dispatchEvent('pointerdown', { pointerId: 8 });
    sent = await page.evaluate(() => window.sent);
    ok(sent.length === 2 && sent[1].t === 'kick' && sent[1].st === 0.4 && sent[1].power === 0.5 && sent[1].aim === 1, tag + ': the kick on the game\'s power clock ' + JSON.stringify(sent));
    // and a kickoff after it is the swipe again
    await page.evaluate(() => { meter(false); ui('walk'); ui('kick'); meter(true, 'KICKOFF', 0); });
    s = await state();
    ok(!s.sweep && !(await page.isVisible('.ktap')), tag + ': a kickoff is a swipe again ' + JSON.stringify(s));

    ok(errors.length === 0, tag + ': page errors ' + errors.join('; '));
    await page.close();
  }
  await browser.close();
  if (fails.length) { console.log('KICK METER FAILED (' + fails.length + ')'); fails.forEach(f => console.log('  ' + f)); process.exit(1); }
  console.log('kick meter ok');
})();
