// Headless check of the lobby screen (WS-07, Playwright + Chromium): it shows, the spots send the right messages,
// a swap question answers, KICK OFF sends, a non-lobby screen while the game waits asks for the lobby back.
// Run: node tools/phone/lobby-test.js   (writes .shots/phone-lobby-portrait.png, -landscape.png, -ask.png)
const path = require('path');
const { chromium } = require('playwright');

(async () => {
  const fails = [];
  const ok = (cond, what) => { if (!cond) fails.push(what); };
  const shots = path.resolve(__dirname, '../../.shots');
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 390, height: 844 }, hasTouch: true });
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto('file://' + path.resolve(__dirname, 'lobby-harness.html'));
  await page.waitForTimeout(50);

  ok(await page.isVisible('#lobbyView'), 'the lobby screen should show on ui lobby');
  ok(!(await page.isVisible('#pickView')), 'the other screens hide');
  ok((await page.$$('#lOff .lspot')).length === 11 && (await page.$$('#lDef .lspot')).length === 11, '11 spots a side');
  const you = await page.textContent('#lYou');
  ok(you.includes('WR #17') && you.includes('CB #24'), 'your two spots: ' + you);
  const unlabelled = await page.$$eval('#lobbyView button', bs => bs.filter(b => !b.getAttribute('aria-label')).length);
  ok(unlabelled === 0, 'every button names its action');
  await page.screenshot({ path: path.join(shots, 'phone-lobby-portrait.png'), fullPage: true });

  // a bot's spot: take it; your own spot: nothing; a person's spot: a swap ask
  await page.evaluate(() => { window.sent = []; });
  await page.click('#lOff .lspot:nth-child(2)');
  await page.click('#lOff .lspot:nth-child(4)');
  await page.click('#lDef .lspot:nth-child(1)');
  let sent = await page.evaluate(() => window.sent);
  ok(sent.length === 2, 'two presses send, your own spot does not: ' + JSON.stringify(sent));
  ok(sent[0] && sent[0].t === 'spot' && sent[0].side === 'o' && sent[0].spot === 1 && sent[0].aid === 'lobby3', 'offense spot press: ' + JSON.stringify(sent[0]));
  ok(sent[1] && sent[1].side === 'd' && sent[1].spot === 0, 'defense spot press: ' + JSON.stringify(sent[1]));

  // someone asks to swap: the question shows; SWAP answers yes
  await page.evaluate(() => lineup({ ask: { from: 'RAY', side: 'o', give: 'WR #17', get: 'QB #9' }, line: 'RAY asked you' }));
  ok(await page.isVisible('#lAsk'), 'the swap question shows');
  ok((await page.textContent('#lAskText')).includes('RAY wants your WR #17'), 'the question names who and what');
  await page.screenshot({ path: path.join(shots, 'phone-lobby-ask.png'), fullPage: true });
  await page.evaluate(() => { window.sent = []; });
  await page.click('#lYes');
  await page.click('#lKick');
  sent = await page.evaluate(() => window.sent);
  ok(sent[0] && sent[0].t === 'swap' && sent[0].yes === true && sent[0].aid === 'lobby3', 'swap yes: ' + JSON.stringify(sent[0]));
  ok(sent[1] && sent[1].t === 'kickoff' && sent[1].aid === 'lobby3', 'kickoff: ' + JSON.stringify(sent[1]));

  // while the game waits, another screen asks for the lobby back (but a team pick does not)
  await page.evaluate(() => { window.sent = []; Lobby.onMessage({ t: 'ui', layout: 'pick', aid: 'pick9' }); });
  ok(!(await page.isVisible('#lobbyView')), 'another screen hides the lobby');
  await page.evaluate(() => Lobby.onMessage({ t: 'ui', layout: 'huddle', aid: 'huddle10' }));
  sent = await page.evaluate(() => window.sent);
  ok(sent.length === 1 && sent[0].t === 'lobbyme', 'asks for the lobby back once: ' + JSON.stringify(sent));

  // after kickoff: no asking back, KICK OFF greyed
  await page.evaluate(() => { window.sent = []; lineup({ held: false }); Lobby.onMessage({ t: 'ui', layout: 'lobby', aid: 'lobby4' }); });
  await page.waitForTimeout(20);
  ok(await page.isDisabled('#lKick'), 'KICK OFF is greyed after kickoff');
  await page.evaluate(() => Lobby.onMessage({ t: 'ui', layout: 'huddle', aid: 'huddle11' }));
  sent = await page.evaluate(() => window.sent);
  ok(sent.length === 0, 'no lobbyme after kickoff: ' + JSON.stringify(sent));

  // landscape phone
  await page.evaluate(() => { lineup({}); Lobby.onMessage({ t: 'ui', layout: 'lobby', aid: 'lobby5' }); });
  await page.setViewportSize({ width: 844, height: 390 });
  await page.waitForTimeout(20);
  await page.screenshot({ path: path.join(shots, 'phone-lobby-landscape.png'), fullPage: true });
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  ok(!overflow, 'no sideways scroll in landscape');

  ok(errors.length === 0, 'page errors: ' + errors.join('; '));
  await browser.close();
  if (fails.length) { console.log('LOBBY TEST FAILED (' + fails.length + ')'); fails.forEach(f => console.log('  ' + f)); process.exit(1); }
  console.log('lobby test ok');
})();
