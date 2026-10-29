// Headless check of the lineman's drive stick (phone/driveblock.js, the game's FEATURES.driveBlock), Playwright + Chromium:
// the 'block' layout with driveStick draws SPRINT and a BLOCK stick; touching it holds BLOCK, sliding streams drive
// messages with the aid, letting go zeroes the drive and releases BLOCK; a layout change mid-hold lets go too; without
// driveStick the old BLOCK gem is back.
// Run: node tools/phone/driveblock-test.js   (writes .shots/phone-block-drive-portrait.png and -landscape.png)
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

  // without the switch: the plain BLOCK gem
  await page.evaluate(() => go('block'));
  ok(await page.$('.gem[data-act="block"]') !== null, 'no driveStick: the BLOCK gem should show');
  ok(await page.$('.dstick') === null, 'no driveStick: no drive stick');

  await page.evaluate(() => go('block', { driveStick: true }));
  ok(await page.$('.dstick[data-act="block"]') !== null, 'driveStick: the BLOCK stick should show');
  ok(await page.$('.dsprint[data-act="sprint"]') !== null, 'driveStick: SPRINT should still show');
  ok(/shove/i.test(await page.textContent('#line')), 'driveStick: the line should explain the shove');
  for (const sel of ['.dstick', '.dsprint']) ok(((await page.getAttribute(sel, 'aria-label')) || '').length > 5, sel + ' needs an aria-label naming its action');
  await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-block-drive-portrait.png') });

  // touch, slide right, let go
  const box = await page.locator('.dstick').boundingBox();
  const cx = box.x + box.width / 2, cy = box.y + box.height / 2;
  await page.evaluate(() => { window.sent = []; });
  const ds = page.locator('.dstick');
  await ds.dispatchEvent('pointerdown', { pointerId: 5, clientX: cx, clientY: cy });
  await ds.dispatchEvent('pointermove', { pointerId: 5, clientX: cx + box.width * 0.45, clientY: cy });
  await page.waitForTimeout(160);
  let sent = await page.evaluate(() => window.sent);
  ok(sent.length > 0 && sent[0].t === 'btn' && sent[0].b === 'block' && sent[0].d === 1 && sent[0].aid === 'a7', 'touch should hold BLOCK: ' + JSON.stringify(sent[0]));
  const drives = sent.filter(m => m.t === 'drive');
  ok(drives.length >= 2, 'drive should stream while held: ' + drives.length);
  const last = drives[drives.length - 1] || {};
  ok(last.x > 0.8 && Math.abs(last.y) < 0.05 && last.aid === 'a7', 'slid right should drive right: ' + JSON.stringify(last));
  ok(await page.$('.dstick.driving') !== null, 'sliding out of the middle should show DRIVE');
  await ds.dispatchEvent('pointerup', { pointerId: 5, clientX: cx + box.width * 0.45, clientY: cy });
  sent = await page.evaluate(() => window.sent);
  const tail = sent.slice(-2);
  ok(tail[0].t === 'drive' && tail[0].x === 0 && tail[0].y === 0, 'let go should zero the drive: ' + JSON.stringify(tail[0]));
  ok(tail[1].t === 'btn' && tail[1].b === 'block' && tail[1].d === 0, 'let go should release BLOCK: ' + JSON.stringify(tail[1]));
  const n = sent.length;
  await page.waitForTimeout(150);
  ok((await page.evaluate(() => window.sent.length)) === n, 'nothing should stream after letting go');

  // a layout change mid-hold lets go of BLOCK
  await page.evaluate(() => { window.sent = []; });
  await ds.dispatchEvent('pointerdown', { pointerId: 6, clientX: cx, clientY: cy - box.height * 0.4 });
  await page.evaluate(() => go('wait'));
  sent = await page.evaluate(() => window.sent);
  ok(sent.some(m => m.t === 'btn' && m.b === 'block' && m.d === 0), 'a layout change should release BLOCK: ' + JSON.stringify(sent));
  ok(sent.filter(m => m.t === 'btn' && m.b === 'block' && m.d === 0).length === 1, 'BLOCK should be released once: ' + JSON.stringify(sent));

  await page.setViewportSize({ width: 844, height: 390 });
  await page.evaluate(() => go('block', { driveStick: true }));
  await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-block-drive-landscape.png') });

  await browser.close();
  if (errors.length) fails.push('page errors: ' + errors.join('; '));
  if (fails.length) { console.log('DRIVE STICK FAILED (' + fails.length + ')'); fails.forEach(f => console.log('  ' + f)); process.exit(1); }
  console.log('drive stick ok');
})();
