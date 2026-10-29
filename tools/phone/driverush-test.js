// Headless check of the defender's BULL RUSH stick (phone/driveblock.js, the game's FEATURES.driveRush), Playwright + Chromium:
// the 'rush' layout with driveStick draws small TACKLE / SWIM / SWAT gems and a BULL RUSH stick; touching it presses
// BULL RUSH, sliding streams drive messages with the aid, letting go zeroes the drive and releases BULL RUSH; a layout
// change mid-hold lets go too; without driveStick the four old gems are back.
// Run: node tools/phone/driverush-test.js   (writes .shots/phone-rush-drive-portrait.png and -landscape.png)
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

  // without the switch: the four gems
  await page.evaluate(() => go('rush'));
  ok(await page.$('.gem[data-act="bull"]') !== null, 'no driveStick: the BULL RUSH gem should show');
  ok(await page.$('.dstick') === null, 'no driveStick: no drive stick');

  await page.evaluate(() => go('rush', { driveStick: true }));
  ok(await page.$('.dstick[data-act="bull"]') !== null, 'driveStick: the BULL RUSH stick should show');
  for (const act of ['tackle', 'swim', 'swat']) ok(await page.$('.dcol [data-act="' + act + '"]') !== null, 'driveStick: ' + act + ' should still show');
  ok(await page.$('[data-act="sprint"]') === null, 'the rusher has no SPRINT gem (he never had one)');
  ok(/shove/i.test(await page.textContent('#line')), 'driveStick: the line should explain the shove');
  for (const sel of ['.dstick', '.dcol [data-act="tackle"]', '.dcol [data-act="swim"]', '.dcol [data-act="swat"]']) ok(((await page.getAttribute(sel, 'aria-label')) || '').length > 5, sel + ' needs an aria-label naming its action');
  await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-rush-drive-portrait.png') });

  // touch, slide right, let go
  const box = await page.locator('.dstick').boundingBox();
  const cx = box.x + box.width / 2, cy = box.y + box.height / 2;
  await page.evaluate(() => { window.sent = []; });
  const ds = page.locator('.dstick');
  await ds.dispatchEvent('pointerdown', { pointerId: 5, clientX: cx, clientY: cy });
  await ds.dispatchEvent('pointermove', { pointerId: 5, clientX: cx + box.width * 0.45, clientY: cy });
  await page.waitForTimeout(160);
  let sent = await page.evaluate(() => window.sent);
  ok(sent.length > 0 && sent[0].t === 'btn' && sent[0].b === 'bull' && sent[0].d === 1 && sent[0].aid === 'a7', 'touch should press BULL RUSH: ' + JSON.stringify(sent[0]));
  const drives = sent.filter(m => m.t === 'drive');
  ok(drives.length >= 2, 'drive should stream while held: ' + drives.length);
  const last = drives[drives.length - 1] || {};
  ok(last.x > 0.8 && Math.abs(last.y) < 0.05 && last.aid === 'a7', 'slid right should drive right: ' + JSON.stringify(last));
  ok(await page.$('.dstick.driving') !== null, 'sliding out of the middle should show DRIVE');
  await ds.dispatchEvent('pointerup', { pointerId: 5, clientX: cx + box.width * 0.45, clientY: cy });
  sent = await page.evaluate(() => window.sent);
  const tail = sent.slice(-2);
  ok(tail[0].t === 'drive' && tail[0].x === 0 && tail[0].y === 0, 'let go should zero the drive: ' + JSON.stringify(tail[0]));
  ok(tail[1].t === 'btn' && tail[1].b === 'bull' && tail[1].d === 0, 'let go should release BULL RUSH: ' + JSON.stringify(tail[1]));
  const n = sent.length;
  await page.waitForTimeout(150);
  ok((await page.evaluate(() => window.sent.length)) === n, 'nothing should stream after letting go');

  // the small gems still press and release
  await page.evaluate(() => { window.sent = []; });
  const sw = page.locator('.dcol [data-act="swim"]');
  await sw.dispatchEvent('pointerdown', { pointerId: 8 });
  await sw.dispatchEvent('pointerup', { pointerId: 8 });
  sent = await page.evaluate(() => window.sent);
  ok(sent.length === 2 && sent[0].b === 'swim' && sent[0].d === 1 && sent[1].b === 'swim' && sent[1].d === 0 && sent[0].aid === 'a7', 'SWIM should press and release: ' + JSON.stringify(sent));

  // a layout change mid-hold lets go of BULL RUSH
  await page.evaluate(() => { window.sent = []; });
  await ds.dispatchEvent('pointerdown', { pointerId: 6, clientX: cx, clientY: cy - box.height * 0.4 });
  await page.evaluate(() => go('wait'));
  sent = await page.evaluate(() => window.sent);
  ok(sent.some(m => m.t === 'btn' && m.b === 'bull' && m.d === 0), 'a layout change should release BULL RUSH: ' + JSON.stringify(sent));
  ok(sent.filter(m => m.t === 'btn' && m.b === 'bull' && m.d === 0).length === 1, 'BULL RUSH should be released once: ' + JSON.stringify(sent));

  await page.setViewportSize({ width: 844, height: 390 });
  await page.evaluate(() => go('rush', { driveStick: true }));
  await page.screenshot({ path: path.resolve(__dirname, '../../.shots/phone-rush-drive-landscape.png') });

  await browser.close();
  if (errors.length) fails.push('page errors: ' + errors.join('; '));
  if (fails.length) { console.log('RUSH STICK FAILED (' + fails.length + ')'); fails.forEach(f => console.log('  ' + f)); process.exit(1); }
  console.log('rush stick ok');
})();
