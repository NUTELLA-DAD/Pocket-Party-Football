// Pocket Party Football: the secret huddle and the job diagram (M1-b).
// Only this team's phones ever get the plays; the TV and the other team see check marks.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var SVG = 'http' + '://www.w3.org/2000/svg';   // the SVG namespace name (not a download)
  var H = { plays: [], spots: [], opp: [], me: 0, caller: false, locked: false, mine: '', pick: '', thumbs: {}, side: '', ends: 0 };
  var clockTimer = null;

  // ---- the playbook picture: a coach's chalkboard on grass. Offense = cream O's, defense = red X's, jobs = yellow arrows. ----
  // Play frame: [ahead, z] yards from the ball, ahead = the offense's direction, +z = the far sideline on the TV.
  // Drawn like a real playbook: forward is UP, the TV's far sideline is on the left (the TV picture turned a quarter turn).
  // Not to scale, on purpose: the line is spread out so eleven circles fit side by side, and deep routes are squeezed.
  var S = 10, SIDE = 26.7;                    // SVG units per yard; the sideline, yards from the middle
  var C = { grass: '#2a8a3e', grassOut: '#1f6e31', tuft: '#46a852', chalk: '#efe8c8', line: '#f6f1de',
            o: '#f7efd2', ink: '#14203a', x: '#ee4a3c', xInk: '#6e1414', route: '#ffd21f' };
  function wz(z) { var a = Math.abs(z), d = a <= 5 ? a * 2.4 : 12 + (a - 5) * 0.68; return z < 0 ? d : -d; }   // across (yards)
  function wa(a) { var d = Math.abs(a), v = d <= 2 ? d * 3.5 : d <= 12 ? 7 + (d - 2) * 1.5 : 22 + (d - 12) * 0.55; return a < 0 ? -v : v; }   // ahead (yards)
  function el(name, attrs, parent) {
    var e = document.createElementNS(SVG, name);
    for (var k in attrs) e.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(e);
    return e;
  }
  function f1(n) { return Math.round(n * 10) / 10; }

  // side: 'offense' or 'defense' (whose phones these are). aspect: width / height of the box to fill, or 0 to fit tight.
  // whole: also draw every teammate's job, so the caller sees the shape of the whole play.
  function diagram(play, spots, opp, me, whole, side, aspect) {
    var mineOff = side ? side === 'offense' : !spots.length || spots.reduce(function (t, s) { return t + s.ahead; }, 0) <= 0;
    var jobs = play && play.jobs ? play.jobs : [];
    // everything in drawing yards: u across (left = +z), v ahead (up = +ahead)
    function P(a, z) { return [wz(z), wa(a)]; }
    var pts = [];
    spots.concat(opp).forEach(function (s) { pts.push(P(s.ahead, s.z)); });
    jobs.forEach(function (j, i) {
      if (!whole && i !== me) return;
      (j.pts || []).forEach(function (q) { var p = P(q[0], q[1]), r = j.kind === 'zone' ? (j.radius || 0) : 0; pts.push([p[0] - r, p[1] - r], [p[0] + r, p[1] + r]); });
    });
    var u0 = -12, u1 = 12, v0 = -6, v1 = 10;
    pts.forEach(function (p) { u0 = Math.min(u0, p[0]); u1 = Math.max(u1, p[0]); v0 = Math.min(v0, p[1]); v1 = Math.max(v1, p[1]); });
    u0 -= 3; u1 += 3; v0 -= 3; v1 += 4;
    if (aspect > 0) {   // grow the window (never shrink it) so the picture fills its box
      var wd = u1 - u0, ht = v1 - v0;
      if (wd / ht < aspect) { var gu = (ht * aspect - wd) / 2; u0 -= gu; u1 += gu; }
      else { var gv = (wd / aspect - ht) / 2; v0 -= gv; v1 += gv; }
    }
    var W = (u1 - u0) * S, H = (v1 - v0) * S;
    function X(u) { return f1((u - u0) * S); }
    function Y(v) { return f1((v1 - v) * S); }
    function xy(a, z) { var p = P(a, z); return [X(p[0]), Y(p[1])]; }

    var svg = el('svg', { viewBox: '0 0 ' + f1(W) + ' ' + f1(H), class: 'mini', role: 'img',
      'aria-label': (whole ? 'The play' : 'Your job') + ': forward is up, your spot says YOU' });
    // grass, out of bounds past the sidelines, then the chalk
    el('rect', { width: f1(W), height: f1(H), fill: C.grassOut }, svg);
    var xl = X(wz(SIDE)), xr = X(wz(-SIDE));
    el('rect', { x: Math.max(0, xl), y: 0, width: f1(Math.min(W, xr) - Math.max(0, xl)), height: f1(H), fill: C.grass }, svg);
    var seed = 7;
    function rnd() { seed = (seed * 16807) % 2147483647; return seed / 2147483647; }   // fixed seed: the same tufts every time
    for (var n = Math.round(W * H / 9000); n > 0; n--) {
      var tx = rnd() * W, ty = rnd() * H, k = S * (0.5 + rnd() * 0.4);
      el('path', { d: 'M' + f1(tx - k) + ' ' + f1(ty - k) + 'L' + f1(tx) + ' ' + f1(ty) + 'L' + f1(tx + k * 0.3) + ' ' + f1(ty - k * 1.3) + 'M' + f1(tx) + ' ' + f1(ty) + 'L' + f1(tx + k) + ' ' + f1(ty - k * 0.8),
        stroke: C.tuft, 'stroke-width': f1(S * 0.22), fill: 'none', 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, svg);
    }
    if (xl > 0) el('line', { x1: xl, y1: 0, x2: xl, y2: f1(H), stroke: C.chalk, 'stroke-width': f1(S * 0.5) }, svg);
    if (xr < W) el('line', { x1: xr, y1: 0, x2: xr, y2: f1(H), stroke: C.chalk, 'stroke-width': f1(S * 0.5) }, svg);
    var a0 = Math.ceil(-40 / 5) * 5;
    for (var ya = a0; ya <= 60; ya += 5) {
      if (ya === 0) continue;
      var yy = Y(wa(ya));
      if (yy < 0 || yy > H) continue;
      el('line', { x1: Math.max(0, xl), y1: yy, x2: Math.min(W, xr), y2: yy, stroke: C.chalk, 'stroke-width': f1(S * 0.25), 'stroke-opacity': '.85' }, svg);
      [xl + S * 0.25, xr - S * 0.25].forEach(function (hx) {
        if (hx > 0 && hx < W) el('rect', { x: f1(hx - S * 0.35), y: f1(yy - S * 0.7), width: f1(S * 0.7), height: f1(S * 0.7), fill: C.chalk }, svg);
      });
    }
    el('line', { x1: Math.max(0, xl), y1: Y(2), x2: Math.min(W, xr), y2: Y(2), stroke: C.line, 'stroke-width': f1(S * 0.35), 'stroke-dasharray': f1(S * 1.4) + ' ' + f1(S * 0.9) }, svg);

    var offense = mineOff ? spots : opp, defense = mineOff ? opp : spots;
    var R = S * 1.45;   // a player's mark, radius
    function at(i) { var s = spots[i]; return s ? xy(s.ahead, s.z) : null; }

    // a thick yellow arrow with a dark outline; corners rounded like a marker pen
    function arrow(from, to, wide, dash, head) {
      var ps = [from].concat(to), w = S * (wide ? 0.85 : 0.6), hl = S * (wide ? 2.4 : 1.9);
      var e = ps[ps.length - 1], b = ps[ps.length - 2], dx = e[0] - b[0], dy = e[1] - b[1], len = Math.sqrt(dx * dx + dy * dy) || 1;
      var ux = dx / len, uy = dy / len;
      var tip = e, end = head === false ? e : [e[0] - ux * hl * 0.8, e[1] - uy * hl * 0.8];
      var d = 'M' + ps[0][0] + ' ' + ps[0][1];
      for (var i = 1; i < ps.length - 1; i++) {
        var p = ps[i], pa = ps[i - 1], pb = ps[i + 1];
        var la = Math.hypot(p[0] - pa[0], p[1] - pa[1]) || 1, lb = Math.hypot(pb[0] - p[0], pb[1] - p[1]) || 1, r = Math.min(S * 2, la / 2, lb / 2);
        d += 'L' + f1(p[0] - (p[0] - pa[0]) / la * r) + ' ' + f1(p[1] - (p[1] - pa[1]) / la * r) + 'Q' + p[0] + ' ' + p[1] + ' ' + f1(p[0] + (pb[0] - p[0]) / lb * r) + ' ' + f1(p[1] + (pb[1] - p[1]) / lb * r);
      }
      d += 'L' + f1(end[0]) + ' ' + f1(end[1]);
      var g = el('g', {}, svg), base = { d: d, fill: 'none', 'stroke-linejoin': 'round', 'stroke-linecap': 'round' };
      el('path', Object.assign({}, base, { stroke: C.ink, 'stroke-width': f1(w + S * 0.45) }, dash ? { 'stroke-dasharray': f1(S * 1.2) + ' ' + f1(S * 0.9) } : {}), g);
      el('path', Object.assign({}, base, { stroke: C.route, 'stroke-width': f1(w) }, dash ? { 'stroke-dasharray': f1(S * 1.2) + ' ' + f1(S * 0.9) } : {}), g);
      if (head !== false) {
        var bx = tip[0] - ux * hl, by = tip[1] - uy * hl, hw = hl * 0.62;
        el('path', { d: 'M' + f1(tip[0]) + ' ' + f1(tip[1]) + 'L' + f1(bx - uy * hw) + ' ' + f1(by + ux * hw) + 'L' + f1(bx + uy * hw) + ' ' + f1(by - ux * hw) + 'Z',
          fill: C.route, stroke: C.ink, 'stroke-width': f1(S * 0.25), 'stroke-linejoin': 'round' }, g);
      }
      return g;
    }
    function shorten(from, to, by) { var dx = to[0] - from[0], dy = to[1] - from[1], l = Math.hypot(dx, dy) || 1; return [f1(to[0] - dx / l * by), f1(to[1] - dy / l * by)]; }

    // jobs: zone bubbles first (under everything), then the arrows
    function drawJob(j, i, mine) {
      var h = at(i); if (!h || !j) return;
      var p = (j.pts || []).map(function (q) { return xy(q[0], q[1]); });
      if (j.kind === 'route' && p.length) arrow(h, p, mine);
      else if (j.kind === 'block' && p.length) {
        // a block is a short stub with a T on the end; a far block point (a run block) gets the full line
        var dx = p[0][0] - h[0], dy = p[0][1] - h[1], l = Math.hypot(dx, dy);
        if (l < S * 0.5) { dx = 0; dy = -1; l = 1; }
        var reach = Math.max(l, R + S * 1.6), e = [f1(h[0] + dx / l * reach), f1(h[1] + dy / l * reach)], g = arrow(h, [e], mine, false, false);
        var nx = -dy / l * S * 1.2, ny = dx / l * S * 1.2;
        el('line', { x1: f1(e[0] - nx), y1: f1(e[1] - ny), x2: f1(e[0] + nx), y2: f1(e[1] + ny), stroke: C.ink, 'stroke-width': f1(S * (mine ? 1.15 : 0.95)), 'stroke-linecap': 'round' }, g);
        el('line', { x1: f1(e[0] - nx), y1: f1(e[1] - ny), x2: f1(e[0] + nx), y2: f1(e[1] + ny), stroke: C.route, 'stroke-width': f1(S * (mine ? 0.7 : 0.5)), 'stroke-linecap': 'round' }, g);
      }
      else if (j.kind === 'zone' && p.length) arrow(h, [shorten(h, p[0], S * 0.6)], mine);
      else if (j.kind === 'man' && opp[j.target]) { var t = opp[j.target]; arrow(h, [shorten(h, xy(t.ahead, t.z), R + S * 0.6)], mine, true); }
      else if (j.kind === 'rush' && opp[0]) { var q = xy(opp[0].ahead, opp[0].z); arrow(h, [shorten(h, q, mine ? R + S * 0.6 : Math.hypot(q[0] - h[0], q[1] - h[1]) * 0.45)], mine); }
    }
    jobs.forEach(function (j, i) {
      if ((whole || i === me) && j.kind === 'zone' && j.pts && j.pts.length) {
        var c = xy(j.pts[0][0], j.pts[0][1]), mine = i === me;
        el('ellipse', { cx: c[0], cy: c[1], rx: f1(j.radius * S * 0.9), ry: f1(j.radius * S), fill: C.route, 'fill-opacity': mine ? '.32' : '.14',
          stroke: mine ? C.route : C.chalk, 'stroke-opacity': mine ? '1' : '.6', 'stroke-width': f1(S * (mine ? 0.4 : 0.25)), 'stroke-dasharray': f1(S * 1) + ' ' + f1(S * 0.7) }, svg);
      }
    });

    // the defense: red X's (under the arrows, like ink on a page)
    function cross(c, fill, ink, big) {
      var a = R * (big ? 1.25 : 1.05), t = a * 0.34, q = [[t, a], [t, t], [a, t], [a, -t], [t, -t], [t, -a], [-t, -a], [-t, -t], [-a, -t], [-a, t], [-t, t], [-t, a]];
      return el('path', { d: 'M' + q.map(function (v) { return f1(v[0]) + ' ' + f1(v[1]); }).join('L') + 'Z', fill: fill, stroke: ink, 'stroke-width': f1(S * 0.28), 'stroke-linejoin': 'round',
        transform: 'translate(' + c[0] + ' ' + c[1] + ') rotate(45)' }, svg);
    }
    function ring(c, fill, ink, dot, big) {
      var r = R * (big ? 1.2 : 1);
      el('circle', { cx: c[0], cy: c[1], r: f1(r), fill: fill, stroke: ink, 'stroke-width': f1(S * 0.38) }, svg);
      if (dot) el('circle', { cx: c[0], cy: c[1], r: f1(r * 0.36), fill: dot }, svg);
    }
    defense.forEach(function (s, i) { if (mineOff || i !== me) cross(xy(s.ahead, s.z), C.x, C.xInk); });

    jobs.forEach(function (j, i) { if (whole && i !== me) drawJob(j, i, false); });
    drawJob(jobs[me], me, true);

    offense.forEach(function (s, i) { if (!mineOff || i !== me) ring(xy(s.ahead, s.z), C.o, C.ink, C.ink); });

    // you: your shape in your team's colour, a white halo and a YOU tag
    var hm = at(me);
    if (hm) {
      el('circle', { cx: hm[0], cy: hm[1], r: f1(R * 2.1), fill: '#fff', 'fill-opacity': '.18', stroke: '#fff', 'stroke-width': f1(S * 0.3) }, svg);
      if (mineOff) ring(hm, 'var(--team)', '#fff', '#fff', true); else cross(hm, 'var(--team)', '#fff', true);
      var ty2 = hm[1] - R * 2.1 - S * 0.6, tw = S * 4.4, th = S * 2.1;
      if (ty2 - th < 0) ty2 = hm[1] + R * 2.1 + S * 0.6 + th;
      el('rect', { x: f1(hm[0] - tw / 2), y: f1(ty2 - th), width: f1(tw), height: f1(th), rx: f1(th / 2), fill: 'var(--team)', stroke: '#fff', 'stroke-width': f1(S * 0.22) }, svg);
      var tx2 = el('text', { x: hm[0], y: f1(ty2 - th * 0.28), 'text-anchor': 'middle', fill: 'var(--team-ink)', 'font-size': f1(S * 1.45),
        'font-family': 'Arial Rounded MT Bold, Trebuchet MS, system-ui, sans-serif', 'font-weight': '800' }, svg);
      tx2.textContent = 'YOU';
    }
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

  // the list: names only, so it reads at a glance; the picture of the tapped play goes in the preview
  function drawPlays() {
    var box = $('hPlays');
    box.innerHTML = '';
    H.plays.forEach(function (p) {
      var b = document.createElement('button');
      b.className = 'play';
      b.setAttribute('role', 'option');
      b.setAttribute('aria-pressed', String(p.id === H.pick));
      b.setAttribute('aria-selected', String(p.id === H.pick));
      b.setAttribute('aria-label', 'Choose ' + p.name + (p.desc ? ': ' + p.desc : ''));
      b.disabled = H.locked;
      var n = H.thumbs[p.id] || 0;
      if (n > 0) { var th = document.createElement('span'); th.className = 'thumbs'; th.textContent = '👍 ' + n; b.appendChild(th); }
      var t = document.createElement('b'); t.textContent = p.name; b.appendChild(t);
      var d = document.createElement('small'); d.textContent = p.desc || ''; b.appendChild(d);
      b.addEventListener('click', function () { if (!H.locked) { H.pick = p.id; drawPlays(); } });
      box.appendChild(b);
    });
    drawPreview();
    var go = $('hGo');
    go.disabled = H.locked || !H.pick || (!H.caller && H.pick === H.mine);
    $('hLine').textContent = H.locked ? 'Locked in. Waiting for the other team...'
      : H.caller ? 'You call it. Tap a play, then LOCK IN. 👍 = teammates\u2019 picks.'
      : H.mine ? 'You suggested it. Your caller decides.'
      : 'Tap a play and SUGGEST it to your caller.';
  }

  function drawPreview() {
    var pv = $('hPrev');
    pv.innerHTML = '';
    var p = null;
    H.plays.forEach(function (q) { if (q.id === H.pick) p = q; });
    pv.classList.toggle('empty', !p);
    if (!p) { pv.textContent = 'Tap a play to see it here'; return; }
    var h = document.createElement('h3'); h.textContent = p.name;
    if (p.desc) { var d = document.createElement('small'); d.textContent = ' \u00b7 ' + p.desc; h.appendChild(d); }
    pv.appendChild(h);
    var f = document.createElement('div'); f.className = 'pv-field'; pv.appendChild(f);
    var job = p.jobs ? p.jobs[H.me] : null;
    var j = document.createElement('p'); j.className = 'pv-job';
    var you = document.createElement('span'); you.textContent = 'You: '; j.appendChild(you);
    j.appendChild(document.createTextNode(job ? job.text : ''));
    pv.appendChild(j);
    // measured after the text is in, so the picture fills exactly the space that is left
    var r = f.getBoundingClientRect();
    f.appendChild(diagram(p, H.spots, H.opp, H.me, true, H.side, r.height > 0 ? r.width / r.height : 0));
  }
  // redraw when the box changes size (turning the phone, or the huddle screen appearing after the plays arrived)
  var lastSize = '';
  function refit() {
    var r = $('hPrev').getBoundingClientRect(), sz = Math.round(r.width) + 'x' + Math.round(r.height);
    if (sz !== lastSize) { lastSize = sz; if (r.width > 0 && H.pick) drawPreview(); }
  }
  if (window.ResizeObserver) new ResizeObserver(refit).observe($('hPrev'));
  else window.addEventListener('resize', refit);

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
    f.appendChild(diagram(msg.play, msg.spots || [], msg.opp || [], msg.me, false, msg.side, 0));
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
