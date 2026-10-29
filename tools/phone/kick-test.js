// Headless check of the kick meter (phone/kick.js, WS-01) with Playwright + Chromium: it shows only for the 'kick'
// layout, a swipe up sends one { t:'kick', power, aim, aid }, a tiny swipe sends nothing, and it goes away after.
// Then the field goal's two taps (kickSweep, kickSync) and the punt's and field goal's arc (kickArc).
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

    // ---- kickArc: steer the arrow, HIKE, tap the power, tap the pitch ----
    const shot = name => page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-kick-' + tag + '-' + name + '.png') });
    await page.evaluate(() => { meter(false); ui('walk'); window.sent = []; ui('kick'); arcMeter('PUNT', 'aim', { aim: 0 }); });
    await page.waitForTimeout(200);
    s = await state();
    ok(s.shown && s.arc && s.stage === 'aim', tag + ': a punt lines up with the arc ' + JSON.stringify(s));
    ok(await page.isVisible('.karcfield') && !(await page.isVisible('.kgauges')), tag + ': the little field and its arrow, no meters yet');
    ok(!(await page.isVisible('.kfield')) && !(await page.isVisible('.ktap')), tag + ': none of the old swipe or sweep');
    ok((await page.textContent('.karcbtn')).trim() === 'HIKE', tag + ': the big button says HIKE');
    ok((await page.getAttribute('.ksteer.r', 'aria-label')).length > 0 && (await page.getAttribute('.karcbtn', 'aria-label')).length > 0, tag + ': the buttons name their actions');
    ok((await page.getAttribute('.kplate', 'src')).endsWith('punt-title.png'), tag + ': the PUNT plate');
    await shot('arc-punt-aim');
    // hold the right arrow for half a second: the arc swings right, and the game hears about it
    await page.locator('.ksteer.r').dispatchEvent('pointerdown', { pointerId: 11 });
    await page.waitForTimeout(500);
    await page.locator('.ksteer.r').dispatchEvent('pointerup', { pointerId: 11 });
    s = await state();
    ok(s.aim > 0.3 && s.aim <= 1, tag + ': holding the right arrow steers right ' + s.aim);
    sent = await page.evaluate(() => window.sent);
    const arcAid = await page.evaluate(() => aid);
    ok(sent.length >= 2 && sent.every(m => m.t === 'kicksteer' && m.aid === arcAid), tag + ': steering sends kicksteer with the aid ' + JSON.stringify(sent));
    ok(sent.length > 0 && sent[sent.length - 1].aim === s.aim, tag + ': the last word is where it stopped');
    ok(sent.length <= 12, tag + ': not a flood of messages (' + sent.length + ')');
    const arcLean = await page.evaluate(() => document.querySelector('.karrow').style.transform);
    ok(/rotate\([0-9.]+deg\)/.test(arcLean), tag + ': the arrow leans right ' + arcLean);
    await shot('arc-punt-steered');
    // a drag on the field steers too: left by a quarter of its width = half the way left
    const fb = await page.locator('.karcfield').boundingBox();
    const was = s.aim;
    await page.mouse.move(fb.x + fb.width / 2, fb.y + 30); await page.mouse.down();
    await page.mouse.move(fb.x + fb.width / 4, fb.y + 30, { steps: 5 }); await page.mouse.up();
    s = await state();
    ok(Math.abs(s.aim - Math.max(-1, was - 0.5)) < 0.06, tag + ': a drag steers ' + was + ' -> ' + s.aim);
    // the game echoing an old aim does not yank it back
    await page.evaluate(() => arcMeter('PUNT', 'aim', { aim: -0.9 }));
    ok(Math.abs((await state()).aim - s.aim) < 1e-6, tag + ': the game\'s echo does not undo the steering');
    // HIKE
    await page.evaluate(() => { window.sent = []; });
    await page.locator('.karcbtn').dispatchEvent('pointerdown', { pointerId: 12 });
    sent = await page.evaluate(() => window.sent);
    ok(sent.length === 1 && sent[0].t === 'kickaim' && sent[0].aim === s.aim && sent[0].aid === arcAid, tag + ': HIKE sends the aim ' + JSON.stringify(sent));
    s = await state();
    ok(s.stage === 'power' && await page.isVisible('.kgauges') && !(await page.isVisible('.karcfield')), tag + ': then the power meter ' + JSON.stringify(s));
    ok((await page.textContent('.karcbtn')).includes('POWER'), tag + ': TAP FOR POWER');
    // the game's power clock: 0.6 s in (twice: it holds still), the meter is 3/4 full
    await page.evaluate(() => { arcMeter('PUNT', 'power', { t: 0.6, aim: 0.2 }); arcMeter('PUNT', 'power', { t: 0.6, aim: 0.2 }); });
    await page.waitForTimeout(150);
    const clip = await page.evaluate(() => document.querySelector('.kfillart').style.clipPath);
    ok(/^inset\(25%/.test(clip), tag + ': the colour bar fills three quarters ' + clip);
    await shot('arc-punt-power');
    await page.locator('.kgauges').dispatchEvent('pointerdown', { pointerId: 13 });
    sent = await page.evaluate(() => window.sent);
    ok(sent.length === 2 && sent[1].t === 'kickpower' && sent[1].power === 0.75 && sent[1].st === 0.6 && sent[1].aid === arcAid, tag + ': a tap stops the power ' + JSON.stringify(sent[1]));
    s = await state();
    ok(s.stage === 'pitch' && s.power === 0.75, tag + ': then the pitch ' + JSON.stringify(s));
    ok((await page.textContent('.karcbtn')).includes('PITCH'), tag + ': TAP FOR PITCH');
    ok(await page.isVisible('.kwedge'), tag + ': a punt shades the good high hang');
    await page.evaluate(() => { arcMeter('PUNT', 'pitch', { t: 0.325, power: 0.75, aim: 0.2 }); arcMeter('PUNT', 'pitch', { t: 0.325, power: 0.75, aim: 0.2 }); });
    await page.waitForTimeout(150);
    const needle = await page.evaluate(() => document.querySelector('.kneedle').style.transform);
    ok(/rotate\(-45deg\)/.test(needle), tag + ': a quarter of the pitch sweep in, the needle is at 45 degrees ' + needle);
    await shot('arc-punt-pitch');
    await page.locator('.karcbtn').dispatchEvent('pointerdown', { pointerId: 14 });
    sent = await page.evaluate(() => window.sent);
    ok(sent.length === 3 && sent[2].t === 'kick' && sent[2].pitch === 0.5 && sent[2].st === 0.325 && sent[2].aid === arcAid, tag + ': the second tap kicks with the pitch ' + JSON.stringify(sent[2]));
    s = await state();
    ok(s.done && !(await page.isVisible('.karcbtn')), tag + ': kicked, no more button');
    await page.locator('.kgauges').dispatchEvent('pointerdown', { pointerId: 15 });
    ok((await page.evaluate(() => window.sent.length)) === 3, tag + ': one kick');
    await shot('arc-punt-kicked');

    // a field goal: the FIELD GOAL art, the brackets on the power that clears the bar, the green climbs that clear it
    await page.evaluate(() => { meter(false); ui('walk'); window.sent = []; ui('kick'); arcMeter('FIELD GOAL', 'aim'); });
    await page.waitForTimeout(100);
    ok((await page.getAttribute('.kplate', 'src')).endsWith('fg-title.png'), tag + ': the FIELD GOAL plate');
    ok(await page.isVisible('.karcfield .kposts'), tag + ': posts on the little field');
    ok((await page.textContent('.karcfield .kwind')).includes('9 mph'), tag + ': and the wind');
    await shot('arc-fg-aim');
    // too slow: the game snapped it, then (say a pad) set the power: the phone follows
    await page.evaluate(() => arcMeter('FIELD GOAL', 'power', { aim: -0.3 }));
    s = await state();
    ok(s.stage === 'power' && Math.abs(s.aim + 0.3) < 1e-6, tag + ': the game snapped it: the power runs ' + JSON.stringify(s));
    ok(await page.isVisible('.kmark'), tag + ': the brackets mark the power that clears the bar');
    await page.waitForTimeout(400);
    await shot('arc-fg-power');
    await page.evaluate(() => arcMeter('FIELD GOAL', 'pitch', { power: 0.8, clear: [22, 51], aim: -0.3, t: 0.2 }));
    s = await state();
    ok(s.stage === 'pitch' && s.power === 0.8, tag + ': straight to the pitch with the game\'s power ' + JSON.stringify(s));
    ok(await page.isVisible('.kwedge'), tag + ': the climbs that clear the bar are shaded');
    await page.waitForTimeout(300);
    await shot('arc-fg-pitch');
    // and a kickoff after it is the swipe again
    await page.evaluate(() => { meter(false); ui('walk'); ui('kick'); meter(true, 'KICKOFF', 0); });
    s = await state();
    ok(!s.arc && await page.isVisible('.kfield') && !(await page.isVisible('.karc')), tag + ': a kickoff is the swipe again ' + JSON.stringify(s));

    ok(errors.length === 0, tag + ': page errors ' + errors.join('; '));
    await page.close();
  }
  await browser.close();
  if (fails.length) { console.log('KICK METER FAILED (' + fails.length + ')'); fails.forEach(f => console.log('  ' + f)); process.exit(1); }
  console.log('kick meter ok');
})();
