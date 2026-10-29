// Pocket Party Football: the secret huddle and the job diagram (M1-b).
// Only this team's phones ever get the plays; the TV and the other team see check marks.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var SVG = 'http' + '://www.w3.org/2000/svg';   // the SVG namespace name (not a download)
  var H = { plays: [], spots: [], opp: [], me: 0, caller: false, locked: false, mine: '', pick: '', thumbs: {}, side: '', ends: 0, tab: '' };
  var P = { lineup: null, open: false, asked: -1 };   // FEATURES.huddleSwitch: the team's spots, and the position picker
  var clockTimer = null, turfN = 0;

  // ---- the playbook picture: a coach's chalkboard on grass. Offense = cream O's, defense = red X's, jobs = yellow arrows. ----
  // Play frame: [ahead, z] yards from the ball, ahead = the offense's direction, +z = the far sideline on the TV.
  // Drawn like a real playbook: forward is UP, the TV's far sideline is on the left (the TV picture turned a quarter turn).
  // Not to scale, on purpose: the line is spread out so eleven circles fit side by side, and deep routes are squeezed.
  var S = 10, SIDE = 26.7;                    // SVG units per yard; the sideline, yards from the middle
  var C = { grass: '#12932f', grassOut: '#0a4a1a', zone: '#ffa04a', zoneInk: '#e2560a', chalk: '#efe8c8', line: '#f6f1de',
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
    // the grass is Kenny's turf picture (turf.png), tiled at a fixed size so every card has the same blades
    var pid = 'turf' + (++turfN), pat = el('pattern', { id: pid, patternUnits: 'userSpaceOnUse', width: S * 30, height: f1(S * 30 * 406 / 512) }, el('defs', {}, svg));
    el('rect', { width: S * 30, height: f1(S * 30 * 406 / 512), fill: C.grass }, pat);
    el('image', { href: 'turf.png', width: S * 30, height: f1(S * 30 * 406 / 512), preserveAspectRatio: 'none' }, pat);
    el('rect', { width: f1(W), height: f1(H), fill: 'url(#' + pid + ')' }, svg);
    var xl = X(wz(SIDE)), xr = X(wz(-SIDE));
    // out of bounds past the sidelines: the same grass, darker
    if (xl > 0) el('rect', { width: xl, height: f1(H), fill: C.grassOut, 'fill-opacity': '.55' }, svg);
    if (xr < W) el('rect', { x: xr, width: f1(W - xr), height: f1(H), fill: C.grassOut, 'fill-opacity': '.55' }, svg);
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
        el('ellipse', { cx: c[0], cy: c[1], rx: f1(j.radius * S * 0.9), ry: f1(j.radius * S), fill: C.zone, 'fill-opacity': mine ? '.6' : '.3',   // Kenny's zones: orange, dashed
          stroke: C.zoneInk, 'stroke-opacity': mine ? '1' : '.7', 'stroke-width': f1(S * (mine ? 0.55 : 0.35)), 'stroke-linecap': 'round', 'stroke-dasharray': f1(S * 1) + ' ' + f1(S * 0.9) }, svg);
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
    $('hTitle').textContent = H.caller ? 'CHOOSE YOUR PLAY' : 'SUGGEST A PLAY';
    $('hSub').textContent = (H.side === 'offense' ? 'OFFENSE' : 'DEFENSE') + ' · ' + (me.pos || '') + ' #' + (me.number || '');
    $('hGo').textContent = H.caller ? 'LOCK IN \u2713' : 'SUGGEST';
    var g = H.pick ? groupOf(playById(H.pick)) : '';
    H.tab = g || (groups().indexOf(H.tab) >= 0 ? H.tab : groups()[0] || '');
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

  // ---- the playbook: tabs (PASS / RUN / SPECIAL), a row of play cards with the whole play drawn, LOCK IN ----
  // The kind of play comes from the play itself (who runs it, its name), so a new play file needs nothing extra.
  function groupOf(p) {
    if (!p) return '';
    var n = (p.id + ' ' + p.name).toLowerCase();
    if (H.side === 'defense') return /blitz/.test(n) ? 'BLITZ' : /goal|prevent/.test(n) ? 'SPECIAL' : 'COVER';
    if (/\bfg\b|field goal|punt|kick/.test(n)) return 'SPECIAL';
    if (p.runner != null || /dive|sneak|sweep|toss|draw|run/.test(n)) return 'RUN';
    return 'PASS';
  }
  function groups() {
    var order = H.side === 'defense' ? ['COVER', 'BLITZ', 'SPECIAL'] : ['PASS', 'RUN', 'SPECIAL'];
    return order.filter(function (g) { return H.plays.some(function (p) { return groupOf(p) === g; }); });
  }
  function playById(id) { var f = null; H.plays.forEach(function (p) { if (p.id === id) f = p; }); return f; }

  function drawTabs() {
    var box = $('hTabs'), gs = groups();
    box.innerHTML = '';
    box.classList.toggle('hidden', gs.length < 2);
    gs.forEach(function (g) {
      var b = document.createElement('button');
      b.className = 'htab';
      b.setAttribute('role', 'tab');
      b.setAttribute('aria-selected', String(g === H.tab));
      var n = 0;
      H.plays.forEach(function (p) { if (groupOf(p) === g) n += H.thumbs[p.id] || 0; });
      b.setAttribute('aria-label', 'Show the ' + g.toLowerCase() + ' plays' + (n ? ', ' + n + ' teammate picks' : ''));
      b.textContent = g;
      if (n > 0) { var th = document.createElement('span'); th.className = 'thumbs'; th.textContent = '👍 ' + n; b.appendChild(th); }
      b.addEventListener('click', function () { H.tab = g; drawPlays(); });
      box.appendChild(b);
    });
  }

  function drawPlays() {
    drawTabs();
    var box = $('hPlays');
    box.innerHTML = '';
    var shown = H.plays.filter(function (p) { return groups().length < 2 || groupOf(p) === H.tab; });
    shown.forEach(function (p) {
      var b = document.createElement('button');
      b.className = 'play';
      b.setAttribute('role', 'option');
      b.setAttribute('data-id', p.id);
      b.setAttribute('aria-label', 'Choose ' + p.name + (p.desc ? ': ' + p.desc : ''));
      b.disabled = H.locked;
      var f = document.createElement('div'); f.className = 'pf'; b.appendChild(f);
      var t = document.createElement('b'); t.textContent = p.name; b.appendChild(t);
      var n = H.thumbs[p.id] || 0;
      if (n > 0) { var th = document.createElement('span'); th.className = 'thumbs'; th.textContent = '👍 ' + n; b.appendChild(th); }
      var ck = document.createElement('span'); ck.className = 'ck'; ck.textContent = '✓'; b.appendChild(ck);
      b.addEventListener('click', function () { if (!H.locked) { H.pick = p.id; markPick(); } });
      box.appendChild(b);
    });
    drawPictures();
    markPick();
  }
  // every card's picture, fitted to the card (all cards are the same size, so measure the first)
  var lastSize = '';
  function drawPictures() {
    var fs = $('hPlays').querySelectorAll('.pf');
    if (!fs.length) return;
    var r = fs[0].getBoundingClientRect();
    lastSize = Math.round(r.width) + 'x' + Math.round(r.height);
    var aspect = r.height > 0 ? r.width / r.height : 0;
    for (var i = 0; i < fs.length; i++) {
      var p = playById(fs[i].parentNode.getAttribute('data-id'));
      fs[i].innerHTML = '';
      if (p) fs[i].appendChild(diagram(p, H.spots, H.opp, H.me, true, H.side, aspect));
    }
  }
  // a tap only moves the gold rim and the check; the pictures stay
  function markPick() {
    var cards = $('hPlays').querySelectorAll('.play');
    for (var i = 0; i < cards.length; i++) {
      var on = cards[i].getAttribute('data-id') === H.pick;
      cards[i].setAttribute('aria-pressed', String(on));
      cards[i].setAttribute('aria-selected', String(on));
    }
    drawPreview();
    var go = $('hGo');
    go.disabled = H.locked || !H.pick || (!H.caller && H.pick === H.mine);
    $('hLine').textContent = H.locked ? 'Locked in. Waiting for the other team...'
      : H.caller ? 'You call it. Tap a play, then LOCK IN. 👍 = teammates’ picks.'
      : H.mine ? 'You suggested it. Your caller decides.'
      : 'Tap a play and SUGGEST it to your caller.';
  }

  // under the cards: the picked play's name and what YOU do in it
  function drawPreview() {
    var pv = $('hPrev');
    pv.innerHTML = '';
    var p = playById(H.pick);
    pv.classList.toggle('empty', !p);
    if (!p) { pv.textContent = 'Tap a play to see your job'; return; }
    var job = p.jobs ? p.jobs[H.me] : null;
    var you = document.createElement('span'); you.textContent = p.name + ' · You: '; pv.appendChild(you);
    pv.appendChild(document.createTextNode(job ? job.text : (p.desc || '')));
  }
  // redraw the pictures when the cards change size (turning the phone, or the huddle screen appearing after the plays arrived)
  function refit() {
    var f = $('hPlays').querySelector('.pf');
    if (!f) return;
    var r = f.getBoundingClientRect(), sz = Math.round(r.width) + 'x' + Math.round(r.height);
    if (sz !== lastSize && r.width > 0) drawPictures();
  }
  if (window.ResizeObserver) new ResizeObserver(refit).observe($('hPlays'));
  else window.addEventListener('resize', refit);

  $('hGo').addEventListener('click', function () {
    if (!H.pick || H.locked) return;
    window.PP.send({ t: H.caller ? 'lock' : 'suggest', id: H.pick, aid: window.PP.aid() });
  });

  // ---- a new position between plays (FEATURES.huddleSwitch): the same spot and swap presses as the lobby ----
  function posSide() { return H.side === 'defense' ? 'd' : 'o'; }
  function onLineup(msg) {
    P.lineup = msg;
    var on = !!msg.switch;
    $('hSwitch').classList.toggle('hidden', !on);
    if (!on) { openPos(false); return; }
    var you = msg.you ? msg.you[posSide()] : -1;
    if (msg.line) $('hPosLine').textContent = msg.line;
    if (msg.ask) openPos(true);
    else if (P.open && P.asked >= 0 && you === P.asked) {   // you got the spot you tapped: back to the plays
      openPos(false);
      $('hPosLine').textContent = 'Tap a bot\u2019s spot to take it. Tap a person to ask them to swap.';
    }
    drawSpots();
  }
  function openPos(open) {
    P.open = open;
    if (!open) P.asked = -1;
    $('hPos').classList.toggle('hidden', !open);
    $('hSwitch').setAttribute('aria-pressed', String(open));
    if (open) drawSpots();
  }
  function drawSpots() {
    var m = P.lineup, box = $('hSpots');
    if (!m || !P.open) return;
    var side = posSide(), list = (side === 'o' ? m.offense : m.defense) || [];
    $('hPosTitle').textContent = 'NEW POSITION ON ' + (side === 'o' ? 'OFFENSE' : 'DEFENSE');
    box.innerHTML = '';
    list.forEach(function (s, i) {
      var b = document.createElement('button');
      b.className = 'lspot' + (s.me ? ' me' : '') + (s.person && !s.me ? ' person' : '') + (s.away ? ' away' : '');
      b.setAttribute('aria-pressed', String(!!s.me));
      b.setAttribute('aria-label', s.me ? 'Your position now: ' + s.pos + ' number ' + s.number
        : s.person ? 'Ask ' + s.who + ' to swap for ' + s.pos + ' number ' + s.number
        : 'Switch to ' + s.pos + ' number ' + s.number + ' (a bot plays it now)');
      var pos = document.createElement('b'); pos.textContent = s.pos + ' ' + s.number;
      var who = document.createElement('i'); who.textContent = s.me ? 'YOU' : s.away ? s.who + ' (away)' : s.who;
      b.appendChild(pos); b.appendChild(who);
      b.addEventListener('click', function () {
        if (s.me) return;
        P.asked = i;
        window.PP.send({ t: 'spot', side: side, spot: i, aid: window.PP.aid() });
        if (!s.person) $('hPosLine').textContent = 'Switching to ' + s.pos + ' #' + s.number + '...';
      });
      box.appendChild(b);
    });
    var ask = m.ask;
    $('hAsk').classList.toggle('hidden', !ask);
    if (ask) $('hAskText').textContent = ask.from + ' wants your ' + ask.give + ' spot. You would get ' + ask.get + '.';
  }
  $('hSwitch').addEventListener('click', function () { openPos(!P.open); });
  $('hPosDone').addEventListener('click', function () { openPos(false); });
  function answer(yes) {
    window.PP.send({ t: 'swap', yes: yes, aid: window.PP.aid() });
    $('hAsk').classList.add('hidden');
    if (P.lineup) P.lineup.ask = null;
  }
  $('hAskYes').addEventListener('click', function () { answer(true); });
  $('hAskNo').addEventListener('click', function () { answer(false); });

  function onThumbs(msg) {
    H.thumbs = msg.counts || {}; H.mine = msg.mine || ''; H.locked = !!msg.locked;
    drawPlays();
  }

  // ---- your job, shown above the stick once the plays are called ----
  function onJob(msg) {
    openPos(false);   // the plays are called: the position picker is done until the next huddle
    $('hSwitch').classList.add('hidden');
    var me = (msg.spots || [])[msg.me] || {};
    var job = msg.play && msg.play.jobs ? msg.play.jobs[msg.me] : null;
    $('job').classList.remove('hidden');
    $('playView').classList.add('hasjob');   // the play card takes the logo's place in the middle of the pad
    $('jobPlay').textContent = msg.play.name + ' · you are ' + (me.pos || '') + ' #' + (me.number || '');
    $('jobText').textContent = job ? job.text : '';
    var f = $('jobField');
    f.innerHTML = '';
    f.appendChild(diagram(msg.play, msg.spots || [], msg.opp || [], msg.me, false, msg.side, 0));
  }

  function onStatus(msg) {
    if (msg.down) $('hDown').textContent = msg.down;
    if (msg.phase === 'huddle') { H.ends = Date.now() + msg.secs * 1000; tick(); }
  }

  window.Huddle = {
    onMessage: function (msg) {
      if (msg.t === 'huddle') onHuddle(msg);
      else if (msg.t === 'thumbs') onThumbs(msg);
      else if (msg.t === 'job') onJob(msg);
      else if (msg.t === 'status') onStatus(msg);
      else if (msg.t === 'lineup') onLineup(msg);
    }
  };
})();
