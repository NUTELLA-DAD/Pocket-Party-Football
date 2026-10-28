// Headless check of the phone layouts (Playwright + Chromium): every layout draws, presses send the right messages.
// Run: node tools/phone/layouts-test.js   (writes screenshots to .shots/phone-<layout>.png)
const path = require('path');
const { chromium } = require('playwright');

(async () => {
  const fails = [];
  const ok = (cond, what) => { if (!cond) fails.push(what); };
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 390, height: 844 }, hasTouch: true });
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto('file://' + path.resolve(__dirname, 'layouts-harness.html'));
  const names = await page.evaluate(() => PPLayouts.names);
  ok(names.length === 11, 'expected 11 layouts, got ' + names.length);
  for (const n of names) {
    await page.evaluate(n => go(n), n);
    const count = await page.$$eval('#btnBox > *', els => els.length);
    ok(count > 0, n + ': nothing drawn');
    await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-' + n + '.png') });
  }
  // QB: tap = bullet, hold = lob, both carry tgt and the aid
  await page.evaluate(() => go('qb'));
  await page.evaluate(() => { window.sent = []; });
  const rcv = page.locator('[data-tgt="△"]');
  await rcv.dispatchEvent('pointerdown', { pointerId: 1 });
  await rcv.dispatchEvent('pointerup', { pointerId: 1 });
  let sent = await page.evaluate(() => window.sent);
  ok(sent.length === 2 && sent[1].b === 'throw' && sent[1].tgt === '△' && sent[1].lob === false && sent[1].aid === 'a7', 'tap should be a bullet: ' + JSON.stringify(sent));
  await page.evaluate(() => { window.sent = []; });
  await rcv.dispatchEvent('pointerdown', { pointerId: 2 });
  await page.waitForTimeout(450);
  await rcv.dispatchEvent('pointerup', { pointerId: 2 });
  sent = await page.evaluate(() => window.sent);
  ok(sent.length === 2 && sent[1].lob === true, 'hold should be a lob: ' + JSON.stringify(sent));
  // a layout change mid-hold releases the held button
  await page.evaluate(() => { go('carrier'); window.sent = []; });
  await page.locator('[data-act="sprint"]').dispatchEvent('pointerdown', { pointerId: 3 });
  await page.evaluate(() => go('qb'));
  sent = await page.evaluate(() => window.sent);
  ok(sent.length === 2 && sent[0].d === 1 && sent[1].d === 0 && sent[1].b === 'sprint', 'layout change should release sprint: ' + JSON.stringify(sent));
  // kick: drag up from the ball and let go
  await page.evaluate(() => { go('kick'); window.sent = []; });
  const pad = await page.locator('.kickpad').boundingBox();
  const cx = pad.x + pad.width / 2, by = pad.y + pad.height - 24;
  await page.locator('.kickpad').dispatchEvent('pointerdown', { pointerId: 4, clientX: cx, clientY: by });
  await page.locator('.kickpad').dispatchEvent('pointerup', { pointerId: 4, clientX: cx + 10, clientY: pad.y + 40 });
  sent = await page.evaluate(() => window.sent);
  ok(sent.length === 1 && sent[0].t === 'kick' && sent[0].power > 0.8 && sent[0].aim > 0 && sent[0].aim < 0.5 && sent[0].aid === 'a7', 'kick message: ' + JSON.stringify(sent));
  ok(errors.length === 0, 'page errors: ' + errors.join('; '));
  await browser.close();
  if (fails.length) { console.log('FAIL\n- ' + fails.join('\n- ')); process.exit(1); }
  console.log('PASS: ' + names.length + ' layouts, throw tap/hold, release on layout change, kick');
})();
