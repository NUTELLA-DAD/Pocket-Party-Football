// Steps the phone page (served by tools/phone/serve.js with ?fake=1) through every screen and returns, per screen,
// the controls that are not fully on screen. Run in the page: await window.__fitCheck()  -> { size, screens: {name: [bad...]} }
// Every list must be empty at every phone size, sideways first (844x390, 740x360, 667x375, 568x320) and upright.
window.__fitCheck = async function (only) {
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const $ = id => document.getElementById(id);
  const out = { size: innerWidth + 'x' + innerHeight, screens: {} };
  const fit = name => { out.screens[name] = window.__fit().bad; };
  const teams = [
    { name: 'CHIEFS', short: 'KC', color: '#d7263d', color2: '#ffb612', people: 1, players: 11 },
    { name: 'ROUGHRIDERS', short: 'SSK', color: '#1f8a4c', color2: '#f6f1de', people: 0, players: 11 },
  ];
  const spots = [], opp = [];
  for (let i = 0; i < 11; i++) { spots.push({ pos: i === 0 ? 'QB' : 'OL', number: 60 + i, ahead: -1 - (i === 0 ? 4 : 0), z: (i - 5) * 3 }); opp.push({ pos: 'DL', number: 90 + i, ahead: 2 + i, z: (i - 5) * 3 }); }
  const plays = ['dive', 'slants', 'verts', 'boot', 'screen', 'sneak', 'toss'].map((id, k) => ({
    id, name: id.toUpperCase(), side: 'offense', desc: 'A play', jobs: spots.map((s, i) => ({ kind: 'route', text: 'Run your route', pts: [[2 + k, s.z], [10 + k, s.z + 3]], radius: 0, target: -1 })),
  }));

  fit('join');
  $('name').value = 'KENNY';
  $('code').value = 'TEST';
  $('joinBtn').click();
  await wait(30);
  window.__push({ t: 'joined', token: 'x', slot: 0, name: 'KENNY', code: 'TEST', team: -1, teams });
  window.__push({ t: 'ui', layout: 'pick', aid: 'p1' });
  await wait(30); fit('pick');
  window.__push({ t: 'picked', team: 0, number: 9, pos: 'QB', name: 'KENNY', teamName: 'CHIEFS', color: '#d7263d', color2: '#ffb612' });
  window.__push({ t: 'status', phase: 'huddle', secs: 15, called: [false, false], score: [14, 21], down: '2ND & 10', tv: 'HUDDLE 0:15     CHIEFS ...     ROUGHRIDERS ...' });
  window.__push({ t: 'huddle', side: 'offense', secs: 15, caller: true, callerNumber: 9, locked: false, mine: '', plays, spots, opp, me: 0, thumbs: { slants: 2 } });
  window.__push({ t: 'ui', layout: 'huddle', aid: 'h1' });
  await wait(30); fit('huddle');
  window.__push({ t: 'job', side: 'offense', play: plays[1], spots, opp, me: 0 });
  const receivers = ['△', '○', '□', '◇', '☆'].map((s, i) => ({ shape: s, label: 'WR #' + (11 + i) }));
  for (const l of ['walk', 'qbpre', 'qb', 'carrier', 'route', 'block', 'rush', 'lb', 'cover', 'wait', 'kick']) {
    if (only && only !== l) continue;
    window.__push({ t: 'ui', layout: l, aid: l + '1', receivers });
    window.__push({ t: 'me', st: 70, tired: false, oob: false });
    await wait(30); fit(l);
  }
  out.visible = ['joinView', 'pickView', 'huddleView', 'playView'].filter(v => !$(v).classList.contains('hidden'));
  if (!window.__ws) out.error = 'the fake join never connected';
  return out;
};
