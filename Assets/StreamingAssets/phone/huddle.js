// Pocket Party Football: the secret huddle and the job diagram (M1-b).
// Only this team's phones ever get the plays; the TV and the other team see check marks.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var SVG = 'http' + '://www.w3.org/2000/svg';   // the SVG namespace name (not a download)
  var H = { plays: [], spots: [], opp: [], me: 0, caller: false, locked: false, mine: '', pick: '', thumbs: {}, side: '', ends: 0 };
  var clockTimer = null;

  // ---- the mini field: [ahead, z] yards from the ball -> SVG. The offense goes right, the far sideline is up. ----
  var W = 300, HT = 160, LOS = 120, PX = 6, PZ = 2.8;
  function sx(a) { return LOS + a * PX; }
  function sy(z) { return HT / 2 - z * PZ; }
  function el(name, attrs, parent) {
    var e = document.createElementNS(SVG, name);
    for (var k in attrs) e.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(e);
    return e;
  }

  function diagram(play, spots, opp, me) {
    var svg = el('svg', { viewBox: '0 0 ' + W + ' ' + HT, class: 'mini', role: 'img', 'aria-label': 'Your job on the field' });
    el('rect', { width: W, height: HT, fill: '#1f6b45' }, svg);
    for (var x = 0; x < W; x += 30) el('line', { x1: x, y1: 0, x2: x, y2: HT, stroke: '#fff', 'stroke-opacity': '.15' }, svg);
    el('line', { x1: LOS, y1: 0, x2: LOS, y2: HT, stroke: '#5fb4ff', 'stroke-width': 2 }, svg);
    el('line', { x1: sx(10), y1: 0, x2: sx(10), y2: HT, stroke: '#ffcf4a', 'stroke-width': 2, 'stroke-opacity': '.8' }, svg);
    var defs = el('defs', {}, svg);
    var m = el('marker', { id: 'ah', viewBox: '0 0 10 10', refX: 8, refY: 5, markerWidth: 5, markerHeight: 5, orient: 'auto' }, defs);
    el('path', { d: 'M0 0L10 5L0 10z', fill: 'var(--team-2)' }, m);
    opp.forEach(function (s) { el('circle', { cx: sx(s.ahead), cy: sy(s.z), r: 3, fill: '#0d2219', opacity: '.7' }, svg); });
    spots.forEach(function (s, i) { if (i !== me) el('circle', { cx: sx(s.ahead), cy: sy(s.z), r: 4, fill: '#a8c4b4', opacity: '.75' }, svg); });
    var home = spots[me] || { ahead: 0, z: 0 };
    var hx = sx(home.ahead), hy = sy(home.z);
    var job = play && play.jobs ? play.jobs[me] : null;
    var stroke = { stroke: 'var(--team-2)', 'stroke-width': 3.5, fill: 'none', 'stroke-linejoin': 'round' };
    if (job) {
      var pts = job.pts || [];
      if (job.kind === 'route' && pts.length) {
        var line = hx + ',' + hy + ' ' + pts.map(function (p) { return sx(p[0]) + ',' + sy(p[1]); }).join(' ');
        el('polyline', Object.assign({ points: line, 'marker-end': 'url(#ah)' }, stroke), svg);
      } else if (job.kind === 'block' && pts.length) {
        var bx = sx(pts[0][0]), by = sy(pts[0][1]);
        el('line', Object.assign({ x1: hx, y1: hy, x2: bx, y2: by }, stroke), svg);
        el('line', Object.assign({ x1: bx, y1: by - 7, x2: bx, y2: by + 7 }, stroke, { 'stroke-width': 4 }), svg);
      } else if (job.kind === 'zone' && pts.length) {
        var zx = sx(pts[0][0]), zy = sy(pts[0][1]);
        el('ellipse', { cx: zx, cy: zy, rx: job.radius * PX, ry: job.radius * PZ, fill: 'var(--team-2)', 'fill-opacity': '.25', stroke: 'var(--team-2)', 'stroke-width': 2, 'stroke-dasharray': '5 4' }, svg);
        el('line', Object.assign({ x1: hx, y1: hy, x2: zx, y2: zy, 'marker-end': 'url(#ah)' }, stroke, { 'stroke-width': 2.5 }), svg);
      } else if (job.kind === 'man' && opp[job.target]) {
        var t = opp[job.target];
        el('line', Object.assign({ x1: hx, y1: hy, x2: sx(t.ahead), y2: sy(t.z), 'stroke-dasharray': '6 4' }, stroke, { 'stroke-width': 3 }), svg);
        el('circle', { cx: sx(t.ahead), cy: sy(t.z), r: 6, fill: 'none', stroke: 'var(--team-2)', 'stroke-width': 2 }, svg);
      } else if (job.kind === 'rush' && opp[0]) {
        el('polyline', Object.assign({ points: hx + ',' + hy + ' ' + sx(opp[0].ahead) + ',' + sy(opp[0].z), 'marker-end': 'url(#ah)' }, stroke), svg);
      }
    }
    el('circle', { cx: hx, cy: hy, r: 7, fill: 'var(--team)', stroke: '#fff', 'stroke-width': 2.5 }, svg);
    return svg;
  }

  // ---- the huddle screen ----
  function onHuddle(msg) {
    H.plays = msg.plays || []; H.spots = msg.spots || []; H.opp = msg.opp || []; H.me = msg.me;
    H.caller = !!msg.caller; H.locked = !!msg.locked; H.mine = msg.mine || ''; H.thumbs = msg.thumbs || {};
    H.side = msg.side; H.pick = H.mine || H.pick;
    if (!H.plays.some(function (p) { return p.id === H.pick; })) H.pick = '';
    H.ends = Date.now() + msg.secs * 1000;
    var me = H.spots[H.me] || {};
    $('hTitle').textContent = (H.side === 'offense' ? 'OFFENSE' : 'DEFENSE') + ' HUDDLE';
    $('hSub').textContent = 'You are ' + (me.pos || '') + ' #' + (me.number || '') + ' · only your team sees this';
    $('hGo').textContent = H.caller ? 'LOCK IN' : 'SUGGEST';
    $('hGo').setAttribute('aria-label', H.caller ? 'Lock in the chosen play' : 'Suggest the chosen play to your caller');
    drawPlays();
    tick();
    clearInterval(clockTimer);
    clockTimer = setInterval(tick, 250);
  }

  function tick() {
    var s = Math.max(0, Math.ceil((H.ends - Date.now()) / 1000));
    $('hClock').textContent = '0:' + (s < 10 ? '0' : '') + s;
    if (s === 0) clearInterval(clockTimer);
  }

  function drawPlays() {
    var box = $('hPlays');
    box.innerHTML = '';
    H.plays.forEach(function (p) {
      var b = document.createElement('button');
      b.className = 'play';
      b.setAttribute('aria-pressed', String(p.id === H.pick));
      b.setAttribute('aria-label', 'Choose ' + p.name);
      b.disabled = H.locked;
      var n = H.thumbs[p.id] || 0;
      if (n > 0) { var th = document.createElement('span'); th.className = 'thumbs'; th.textContent = '👍 ' + n; b.appendChild(th); }
      var t = document.createElement('b'); t.textContent = p.name; b.appendChild(t);
      var d = document.createElement('small'); d.textContent = p.desc || ''; b.appendChild(d);
      b.appendChild(diagram(p, H.spots, H.opp, H.me));
      b.addEventListener('click', function () { if (!H.locked) { H.pick = p.id; drawPlays(); } });
      box.appendChild(b);
    });
    var go = $('hGo');
    go.disabled = H.locked || !H.pick || (!H.caller && H.pick === H.mine);
    $('hLine').textContent = H.locked ? 'Locked in. Waiting for the other team...'
      : H.caller ? 'You call the play. Tap one, then LOCK IN. Thumbs are your teammates’ suggestions.'
      : H.mine ? 'You suggested it. Your caller decides.'
      : 'Tap a play and SUGGEST it to your caller.';
  }

  $('hGo').addEventListener('click', function () {
    if (!H.pick || H.locked) return;
    window.PP.send({ t: H.caller ? 'lock' : 'suggest', id: H.pick, aid: window.PP.aid() });
  });

  function onThumbs(msg) {
    H.thumbs = msg.counts || {}; H.mine = msg.mine || ''; H.locked = !!msg.locked;
    drawPlays();
  }

  // ---- your job, shown above the stick once the plays are called ----
  function onJob(msg) {
    var me = (msg.spots || [])[msg.me] || {};
    var job = msg.play && msg.play.jobs ? msg.play.jobs[msg.me] : null;
    $('job').classList.remove('hidden');
    $('jobPlay').textContent = msg.play.name + ' · you are ' + (me.pos || '') + ' #' + (me.number || '');
    $('jobText').textContent = job ? job.text : '';
    var f = $('jobField');
    f.innerHTML = '';
    f.appendChild(diagram(msg.play, msg.spots || [], msg.opp || [], msg.me));
  }

  function onStatus(msg) {
    if (msg.phase === 'huddle') { H.ends = Date.now() + msg.secs * 1000; tick(); }
  }

  window.Huddle = {
    onMessage: function (msg) {
      if (msg.t === 'huddle') onHuddle(msg);
      else if (msg.t === 'thumbs') onThumbs(msg);
      else if (msg.t === 'job') onJob(msg);
      else if (msg.t === 'status') onStatus(msg);
    }
  };
})();
