// Pocket Party Football: the kick meter (WS-01). Only the kicker's phone ever shows it: the game sends the 'kick'
// layout and { t:'kickmeter', on, kind, yards } to that one phone. It covers the whole screen while he lines up.
// Swipe up from anywhere on the field: the further up, the harder the kick; drift sideways to aim (right = the
// kicker's right). Letting go sends { t:'kick', power: 0..1, aim: -1..1, aid } with the aid of the screen on show.
// app.js hands every message to PPKick.onMessage; window.PP (app.js) sends and knows the aid.
//
// A field goal with the game's kickSweep: kickmeter also carries sweep { stage: 'aim'|'power', aimS, powerS, need,
// shank, aim? } and maybe wind { mph, right, along }. Then it is two taps, no swipe: an arrow from the ball sweeps
// left and right (a full sweep there and back takes aimS seconds); tap anywhere (or the big button) to set the aim,
// which snaps the ball: { t:'kickaim', aim, aid }. Then the arrow grows and shrinks (powerS seconds there and back):
// the higher its tip, the harder and higher the kick. The posts are drawn where the tip just clears the bar (need);
// the red top of the meter (shank and up) is too hard and hooks. Tap again to kick: { t:'kick', power, aim, aid }.
// The sweep is the same straight back-and-forth sum the game uses (Sim/Kicking.cs: AimSweep, PowerSweep).
// With the game's kickSync the sweep also carries t (the game's seconds into the arrow now sweeping, sent again every
// quarter second), so the arrow here runs on the TV's clock, not the phone's, and both show the same arrow; a tap adds
// st (the seconds this arrow showed). maxDeg leans the arrow as far as the kick really goes, and postsYd / distYd draw
// the posts to the same scale, so an arrow between the posts here is between the posts on the TV.
//
// A punt or a field goal with the game's kickArc: sweep carries arc: true (stage 'aim' | 'power' | 'pitch', powerS,
// pitchS, minDeg, maxDeg, aimDeg, shank, need?, aim, power?, clear?, good?, t?). Lined up, a shaded arrow on a little
// field shows the kick; hold the arrows (or drag the field) to steer it left and right ({ t:'kicksteer', aim, aid }),
// then HIKE ({ t:'kickaim', aim, aid }) snaps it. Then Kenny's POWER meter fills up and down: tap to stop it
// ({ t:'kickpower', power, aid }); then the PITCH ANGLE needle swings up and down: tap to stop it and kick
// ({ t:'kick', pitch: 0..1, aid }). The art is in kick/ (punt-* and fg-*). Taps add st on the game's clock, as above.
(function () {
  'use strict';
  var MIN_POWER = 0.08, FULL_SWIPE = 0.75, STEPS = 10;
  var root = null, field, title, sub, hint, line, fill, pct, posts, arrow;
  var layoutKick = false, meterOn = null, kind = '', yards = 0;
  var pointer = null, x0 = 0, y0 = 0, done = false, last = { power: 0, aim: 0 };
  // the two taps (sweep): which arrow is sweeping, since when (ms), the aim the first tap set
  var sweep = null, stage = '', stageAt = 0, setAim = 0, raf = 0, wind = null, tapBtn, flagL, flagR, windLine;
  // kickSync: the game's clock for this stage (synced once a t came), and held still while the game's clock stands
  var synced = false, lastT = -1, frozen = -1;
  // the arc (kickArc): its pieces, the aim he steers, the power he stopped, the arrow held down (-1 / 1), the drag
  var arcBox, arcPlate, arcField, arcArrow, arcPosts, arcDown, arcGauges, arcFill, arcPointer, arcMark, arcDial, arcWedge,
    arcNeedle, arcGem, arcBtn, arcLeft, arcRight, arcHint, arcWind, arcSet = '';
  var arcAim = 0, arcPower = 0, arcHold = 0, arcDrag = null, arcSentAim = null, arcSentAt = 0, arcHeldAt = 0, arcAimSeen = false;

  function el(tag, cls, text, parent) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text) e.textContent = text;
    if (parent) parent.appendChild(e);
    return e;
  }

  function build() {
    if (root) return;
    root = el('section', 'kickmeter hidden', null, document.body);
    root.id = 'kickMeter';
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-label', 'Kick meter: swipe up to kick');
    var head = el('div', 'khead', null, root);
    title = el('h2', 'ktitle', 'KICK', head);
    sub = el('p', 'sub ksub', '', head);
    field = el('div', 'kfield', null, root);
    field.setAttribute('role', 'button');
    field.setAttribute('aria-label', 'Kick: swipe up from anywhere and let go. Further up = harder, sideways = aim');
    posts = el('div', 'kposts', null, field);
    arrow = el('div', 'kdown', 'DOWNFIELD', field);
    el('div', 'kball', null, field);
    line = el('div', 'kaimline', null, field);
    var meter = el('div', 'kpower', null, field);
    fill = el('i', null, null, meter);
    pct = el('div', 'kpct', '', field);
    flagL = el('i', 'kflag l', null, posts);
    flagR = el('i', 'kflag r', null, posts);
    windLine = el('div', 'kwind hidden', '', field);
    hint = el('p', 'line khint', '', root);
    hint.setAttribute('aria-live', 'polite');
    tapBtn = el('button', 'big ktap hidden', 'TAP TO AIM', root);
    tapBtn.setAttribute('aria-label', 'Kick: tap to set the aim, then tap again to set the power');
    tapBtn.addEventListener('pointerdown', function (e) { e.preventDefault(); tap(); });
    buildArc();

    field.addEventListener('pointerdown', function (e) {
      if (sweep) { e.preventDefault(); tap(); return; }
      if (done || pointer !== null) return;
      e.preventDefault();
      try { field.setPointerCapture(e.pointerId); } catch (err) {}
      pointer = e.pointerId; x0 = e.clientX; y0 = e.clientY;
      draw(measure(e));
    });
    field.addEventListener('pointermove', function (e) { if (!sweep && e.pointerId === pointer) draw(measure(e)); });
    field.addEventListener('pointerup', function (e) {
      if (sweep || e.pointerId !== pointer) return;
      pointer = null;
      kick(measure(e));
    });
    field.addEventListener('pointercancel', function (e) { if (e.pointerId === pointer) { pointer = null; draw({ power: 0, aim: 0 }); } });
  }

  // power: how far up from where the finger went down, over FULL_SWIPE of the field; aim: sideways over half its width
  function measure(e) {
    var b = field.getBoundingClientRect();
    var power = Math.max(0, Math.min(1, (y0 - e.clientY) / (b.height * FULL_SWIPE)));
    var aim = Math.max(-1, Math.min(1, (e.clientX - x0) / (b.width / 2)));
    return { power: Math.round(power * 100) / 100, aim: Math.round(aim * 100) / 100 };
  }

  // the bar fills in tenths; the line from the ball leans the way you aim and grows with the power
  function draw(p) {
    last = p;
    var step = Math.floor(p.power * STEPS) / STEPS;
    fill.style.height = (step * 100) + '%';
    fill.className = step >= 0.8 ? 'hot' : step >= 0.5 ? 'warm' : '';
    pct.textContent = p.power > 0 ? Math.round(step * 100) + '%' : '';
    line.style.height = (p.power * 70) + '%';
    line.style.transform = 'rotate(' + (p.aim * 30) + 'deg)';
    hint.textContent = p.power > 0 ? 'Power ' + Math.round(step * 100) + '% · ' + aimWords(p.aim) + '. Let go to kick'
      : 'Swipe up: further = harder. Drift sideways to aim';
  }

  function aimWords(a) {
    if (Math.abs(a) < 0.1) return 'straight';
    return (Math.abs(a) < 0.5 ? 'a bit ' : '') + (a < 0 ? 'left' : 'right');
  }

  function kick(p) {
    if (done) return;
    if (p.power < MIN_POWER) { draw({ power: 0, aim: 0 }); hint.textContent = 'Swipe further up to kick it'; return; }
    done = true;
    if (window.PP) window.PP.send({ t: 'kick', power: p.power, aim: p.aim, aid: window.PP.aid() });
    root.classList.add('done');
    hint.textContent = 'Kicked! ' + Math.round(p.power * 100) + '% power, ' + aimWords(p.aim) + '. Watch the TV';
  }

  // ---- the two taps ----

  // 0 -> 1 -> 0 over one period, a straight line each way (no sine)
  function triangle(t, period) {
    if (!(period > 0)) return 0;
    var x = t / period; x -= Math.floor(x);
    return x < 0.5 ? x * 2 : (1 - x) * 2;
  }
  function aimNow(t) { return triangle(t + sweep.aimS / 4, sweep.aimS) * 2 - 1; }   // starts in the middle going right
  function powerNow(t) { return triangle(t, sweep.powerS); }
  function since() { return frozen >= 0 ? frozen : (performance.now() - stageAt) / 1000; }
  function leanDeg() { return sweep && sweep.maxDeg > 0 ? sweep.maxDeg : 30; }
  // the game's seconds into this stage: the arrow runs on from there (the same t twice: the game is paused, hold still)
  function syncTo(t) {
    if (typeof t !== 'number' || !(t >= 0)) return;
    frozen = t === lastT ? t : -1;
    lastT = t; synced = true;
    stageAt = performance.now() - t * 1000;
  }
  function tapMsg(m) { if (synced) m.st = Math.round(since() * 1000) / 1000; return m; }

  function startStage(name) {
    if (stage === name) return;
    stage = name; stageAt = performance.now(); synced = false; lastT = -1; frozen = -1;
    tapBtn.textContent = name === 'aim' ? 'TAP TO AIM' : 'TAP TO KICK';
    tapBtn.className = 'big ktap ' + (name === 'aim' ? 'aim' : 'go');
    if (!raf) raf = requestAnimationFrame(frame);
  }

  function tap() {
    if (done || !sweep) return;
    if (stage === 'aim') {
      setAim = Math.round(aimNow(since()) * 100) / 100;
      if (window.PP) window.PP.send(tapMsg({ t: 'kickaim', aim: setAim, aid: window.PP.aid() }));
      var onClock = synced;
      startStage('power');
      synced = onClock;   // (the game starts its power sweep as this tap lands: the same clock, give or take the wifi)
      return;
    }
    if (stage !== 'power') return;
    var p = Math.round(powerNow(since()) * 100) / 100;
    done = true;
    if (window.PP) window.PP.send(tapMsg({ t: 'kick', power: p, aim: setAim, aid: window.PP.aid() }));
    last = { power: p, aim: setAim };
    root.classList.add('done');
    tapBtn.className = 'big ktap hidden';
    var clears = p >= (sweep.need || 0);
    hint.textContent = 'Kicked! ' + Math.round(p * 100) + '% power, ' + aimWords(setAim) + (p >= sweep.shank ? '. Too hard, it may hook!' : clears ? '' : '. Short of the bar?') + ' Watch the TV';
  }

  // one picture of the arrow: its lean is the aim; in the power sweep its tip climbs toward (and over) the bar
  function frame() {
    raf = 0;
    if (!sweep || done || !root || root.classList.contains('hidden')) return;
    var b = field.getBoundingClientRect(), track = Math.max(40, b.height - 60);
    var aim = stage === 'aim' ? aimNow(since()) : setAim;
    var power = stage === 'power' ? powerNow(since()) : 0;
    var lean = aim * leanDeg(), reach = track * Math.max(0.05, Math.min(0.97, sweep.need || 0.5));
    // lining up: the arrow reaches the crossbar's height, so its tip is where the kick crosses the goal line
    line.style.height = (stage === 'aim' ? reach / Math.cos(lean * Math.PI / 180) : Math.max(10, track * power)) + 'px';
    line.style.transform = 'rotate(' + lean + 'deg)';
    line.className = 'kaimline ' + (stage === 'power' && power >= sweep.shank ? 'hot' : stage === 'power' && power >= sweep.need ? 'clear' : '');
    fill.style.height = (power * 100) + '%';
    fill.className = power >= sweep.shank ? 'hot' : power >= sweep.need ? '' : 'warm';
    pct.textContent = stage === 'power' ? Math.round(power * 100) + '%' : '';
    raf = requestAnimationFrame(frame);
  }

  // the posts stand where the tip of the arrow just clears the bar; the wind flags on top of them stream with it
  function placeSweep() {
    var b = field.getBoundingClientRect(), track = Math.max(40, b.height - 60);
    var need = Math.max(0.05, Math.min(0.97, sweep.need || 0.5));
    posts.style.top = 'auto';
    posts.style.bottom = (36 + track * need - 6) + 'px';
    // to scale with the arrow: half the posts' width over the distance to them, at the crossbar's height
    if (sweep.postsYd > 0 && sweep.distYd > 0) {
      var half = Math.max(10, Math.min(b.width * 0.4, track * need * sweep.postsYd / sweep.distYd));
      posts.style.width = (half * 2) + 'px'; posts.style.marginLeft = (-half) + 'px';
    }
    var mark = el('b', 'kneed', null, null);
    var old = field.querySelector('.kpower .kneed');
    if (old) old.remove();
    mark.style.bottom = (need * 100) + '%';
    field.querySelector('.kpower').appendChild(mark);
    var redOld = field.querySelector('.kpower .kshank');
    if (redOld) redOld.remove();
    var red = el('b', 'kshank', null, null);
    red.style.height = ((1 - sweep.shank) * 100) + '%';
    field.querySelector('.kpower').appendChild(red);
    var mph = wind ? wind.mph : 0;
    var right = wind ? wind.right : 0, along = wind ? wind.along : 0;
    [flagL, flagR].forEach(function (f) {
      f.classList.toggle('hidden', !wind);
      f.style.width = (8 + Math.min(1, mph / 15) * 22) + 'px';
      f.style.transform = right < 0 ? 'scaleX(-1)' : '';
      f.classList.toggle('calm', mph < 3);
    });
    windLine.classList.toggle('hidden', !wind);
    windLine.textContent = windWords();
  }

  function windWords() {
    if (!wind) return '';
    var mph = wind.mph, right = wind.right, along = wind.along;
    return mph < 1 ? 'No wind' : 'Wind ' + mph + ' mph ' +
      (Math.abs(right) >= Math.abs(along) ? (right > 0 ? 'left to right \u2192' : '\u2190 right to left') : along > 0 ? 'at your back \u2191' : 'in your face \u2193');
  }

  function reset() {
    done = false; pointer = null; stage = ''; setAim = 0;
    arcAim = 0; arcPower = 0; arcHold = 0; arcDrag = null; arcSentAim = null; arcAimSeen = false;
    posts.style.top = ''; posts.style.bottom = ''; posts.style.width = ''; posts.style.marginLeft = '';
    root.classList.remove('done');
    draw({ power: 0, aim: 0 });
  }

  function refresh() {
    build();
    var show = layoutKick && meterOn !== false;
    var goal = kind === 'FIELD GOAL' || kind === 'EXTRA POINT';
    title.textContent = kind || 'KICK';
    sub.textContent = goal && yards > 0 ? yards + ' yards to the posts' : kind === 'KICKOFF' ? 'Kick it deep, the team runs down to cover'
      : kind === 'PUNT' ? 'Kick it away, the team runs down to cover' : '';
    posts.classList.toggle('hidden', !goal);
    arrow.classList.toggle('hidden', goal);
    root.classList.toggle('hidden', !show);
    document.documentElement.classList.toggle('kicking', show);
    var arc = !!(sweep && sweep.arc);
    root.classList.toggle('sweep', !!sweep && !arc);
    root.classList.toggle('arc', arc);
    tapBtn.classList.toggle('hidden', !sweep || done || arc);
    if (arc && show) { arcRefresh(goal); return; }
    if (sweep && show) {
      placeSweep();
      if (!done) {
        hint.textContent = stage === 'power' ? 'Tap when the arrow is over the bar. Not in the red!' : 'Tap when the arrow points where you want it. Mind the wind';
        if (!raf) raf = requestAnimationFrame(frame);
      }
    }
  }

  // ---- the arc (kickArc) ----

  // Where things are in each piece of art (shares of its size): the meter's channel and its 0% / 100% marks, the
  // dial's pivot, the needle's reach and the shaded band's (of the dial's width), the needle's hub (of the needle's
  // width), the dial's height over its width.
  var ART = {
    punt: { chanL: 0.34, chanR: 0.66, zeroY: 0.761, fullY: 0.124, pivotX: 0.0775, pivotY: 0.801, reach: 0.52, wedge: 0.74, hub: 0.1926, tall: 389 / 341, power: '192 / 426', dial: '341 / 389' },
    fg: { chanL: 0.345, chanR: 0.663, zeroY: 0.736, fullY: 0.145, pivotX: 0.120, pivotY: 0.774, reach: 0.48, wedge: 0.7, hub: 0.1751, tall: 407 / 354, power: '217 / 425', dial: '354 / 407' }
  };
  var STEER_PER_S = 1.2, STEER_SEND_MS = 80;
  function art(name) { return (window.PPKickArt || 'kick/') + name + '.png'; }   // (a test page sets PPKickArt)

  function img(cls, parent, alt) {
    var i = el('img', cls, null, parent);
    i.alt = alt || ''; i.draggable = false;
    return i;
  }

  function buildArc() {
    arcBox = el('div', 'karc', null, root);
    arcPlate = img('kplate', arcBox, '');
    arcField = el('div', 'karcfield', null, arcBox);
    arcField.setAttribute('role', 'slider');
    arcField.setAttribute('aria-label', 'Aim: drag left or right to steer the kick');
    arcPosts = el('div', 'kposts', null, arcField);
    arcDown = el('div', 'kdown', 'DOWNFIELD', arcField);
    arcArrow = el('div', 'karrow', null, arcField);
    arcWind = el('div', 'kwind hidden', '', arcField);
    el('i', 'karrowhead', null, arcArrow);
    el('div', 'kball', null, arcField);
    arcLeft = el('button', 'ksteer l', '◀', arcField);
    arcLeft.setAttribute('aria-label', 'Steer the kick left');
    arcRight = el('button', 'ksteer r', '▶', arcField);
    arcRight.setAttribute('aria-label', 'Steer the kick right');
    [[arcLeft, -1], [arcRight, 1]].forEach(function (b) {
      b[0].addEventListener('pointerdown', function (e) { e.preventDefault(); e.stopPropagation(); arcHold = b[1]; arcHeldAt = performance.now(); arcLoop(); });
      ['pointerup', 'pointercancel', 'pointerleave'].forEach(function (ev) { b[0].addEventListener(ev, function () { if (arcHold === b[1]) { arcHold = 0; steerSend(true); } }); });
    });
    arcField.addEventListener('pointerdown', function (e) {
      if (stage !== 'aim' || done) return;
      e.preventDefault();
      try { arcField.setPointerCapture(e.pointerId); } catch (err) {}
      arcDrag = { id: e.pointerId, x: e.clientX, aim: arcAim };
    });
    arcField.addEventListener('pointermove', function (e) {
      if (!arcDrag || e.pointerId !== arcDrag.id) return;
      var b = arcField.getBoundingClientRect();
      steerTo(arcDrag.aim + (e.clientX - arcDrag.x) / (b.width / 2));
    });
    ['pointerup', 'pointercancel'].forEach(function (ev) {
      arcField.addEventListener(ev, function (e) { if (arcDrag && e.pointerId === arcDrag.id) { arcDrag = null; steerSend(true); } });
    });

    arcGauges = el('div', 'kgauges', null, arcBox);
    var pw = el('div', 'kpowerart', null, arcGauges);
    img('kart', pw, 'Power meter');
    arcFill = el('i', 'kfillart', null, pw);
    arcMark = img('kmark', pw, '');
    arcPointer = img('kpointer', pw, '');
    arcDial = el('div', 'kdialart', null, arcGauges);
    img('kart', arcDial, 'Pitch angle dial');
    arcWedge = el('i', 'kwedge', null, arcDial);
    arcGem = img('kgem', arcDial, '');
    arcNeedle = img('kneedle', arcDial, '');
    arcGauges.addEventListener('pointerdown', function (e) { e.preventDefault(); arcTap(); });

    arcHint = el('p', 'line khint karchint', '', arcBox);
    arcHint.setAttribute('aria-live', 'polite');
    arcBtn = el('button', 'big karcbtn', 'HIKE', arcBox);
    arcBtn.addEventListener('pointerdown', function (e) { e.preventDefault(); arcTap(); });
  }

  // the art for this kick (punt-* or fg-*), set once per kind
  function arcArt(goal) {
    var set = goal ? 'fg' : 'punt';
    if (arcSet === set) return ART[set];
    arcSet = set;
    var a = ART[set];
    arcPlate.src = art(set + '-title');
    arcPlate.alt = goal ? 'FIELD GOAL' : 'PUNT';
    arcBox.querySelector('.kpowerart .kart').src = art(set + '-power');
    arcDial.querySelector('.kart').src = art(set + '-dial');
    arcFill.style.backgroundImage = 'url("' + art(set + '-fill') + '")';
    arcPointer.src = art(set + '-pointer');
    arcNeedle.src = art(set + '-needle');
    arcMark.src = art('fg-mark');
    arcGem.src = art('punt-mark');
    arcBox.querySelector('.kpowerart').style.aspectRatio = a.power;
    arcDial.style.aspectRatio = a.dial;
    arcFill.style.left = (a.chanL * 100) + '%'; arcFill.style.width = ((a.chanR - a.chanL) * 100) + '%';
    arcFill.style.top = (a.fullY * 100) + '%'; arcFill.style.height = ((a.zeroY - a.fullY) * 100) + '%';
    var nw = a.reach / (1 - a.hub);
    arcNeedle.style.width = (nw * 100) + '%';
    arcNeedle.style.left = ((a.pivotX - nw * a.hub) * 100) + '%';
    arcNeedle.style.top = (a.pivotY * 100) + '%';
    arcNeedle.style.transformOrigin = (a.hub * 100) + '% 50%';
    arcWedge.style.left = ((a.pivotX - a.wedge) * 100) + '%';
    arcWedge.style.width = (a.wedge * 200) + '%';
    arcWedge.style.top = ((a.pivotY - a.wedge / a.tall) * 100) + '%';
    arcWedge.style.height = (a.wedge * 200 / a.tall) + '%';
    return a;
  }

  function steerTo(a) {
    arcAim = Math.round(Math.max(-1, Math.min(1, a)) * 100) / 100;
    steerSend(false);
    arcDraw();
  }
  // the steered aim to the game, at most every STEER_SEND_MS (and once more when the finger lifts)
  function steerSend(force) {
    if (!window.PP || arcAim === arcSentAim) return;
    var now = performance.now();
    if (!force && now - arcSentAt < STEER_SEND_MS) return;
    arcSentAim = arcAim; arcSentAt = now;
    window.PP.send({ t: 'kicksteer', aim: arcAim, aid: window.PP.aid() });
  }
  // an arrow held down: the aim runs that way at STEER_PER_S
  function arcLoop() {
    if (!arcHold || stage !== 'aim' || done) return;
    var now = performance.now(), dt = Math.min(0.1, (now - arcHeldAt) / 1000);
    arcHeldAt = now;
    steerTo(arcAim + arcHold * STEER_PER_S * dt);
    requestAnimationFrame(arcLoop);
  }

  function pitchShare(t) { return triangle(t, sweep.pitchS); }
  function pitchDeg(share) { return sweep.minDeg + (sweep.maxDeg - sweep.minDeg) * share; }

  function arcStart(name) {
    if (stage === name) return;
    stage = name; stageAt = performance.now(); synced = false; lastT = -1; frozen = -1;
    if (!raf) raf = requestAnimationFrame(arcFrame);
  }

  // the one big button (and a tap on the meters): HIKE, then the power, then the pitch
  function arcTap() {
    if (done || !sweep || !sweep.arc || !window.PP) return;
    if (stage === 'aim') {
      arcHold = 0; arcDrag = null;
      window.PP.send({ t: 'kickaim', aim: arcAim, aid: window.PP.aid() });
      arcStart('power');
    } else if (stage === 'power') {
      arcPower = Math.round(powerNow(since()) * 100) / 100;
      window.PP.send(tapMsg({ t: 'kickpower', power: arcPower, aid: window.PP.aid() }));
      arcStart('pitch');
    } else if (stage === 'pitch') {
      var share = Math.round(pitchShare(since()) * 100) / 100;
      window.PP.send(tapMsg({ t: 'kick', pitch: share, aid: window.PP.aid() }));
      done = true;
      last = { power: arcPower, aim: arcAim, pitch: share };
      root.classList.add('done');
      arcHint.textContent = 'Kicked! ' + Math.round(arcPower * 100) + '% power at ' + Math.round(pitchDeg(share)) + '°' +
        (arcPower >= sweep.shank ? '. Too hard, it may hook!' : '') + ' Watch the TV';
      arcDraw(share);
    }
    arcRefresh(sweep.need !== undefined || kind === 'FIELD GOAL' || kind === 'EXTRA POINT');
  }

  // the game's word: its stage leads (it snaps the ball itself if he waits too long), its clock runs the meters
  function arcMessage(sw, w) {
    sweep = sw; wind = w || null;
    if (sw.stage === 'aim' && !stage) { arcStart('aim'); if (typeof sw.aim === 'number' && !arcAimSeen) { arcAim = sw.aim; arcAimSeen = true; } }
    else if (sw.stage === 'power' && (stage === '' || stage === 'aim')) { if (typeof sw.aim === 'number') arcAim = sw.aim; arcStart('power'); }
    else if (sw.stage === 'pitch' && stage !== 'pitch' && !done) { if (typeof sw.power === 'number') arcPower = sw.power; arcStart('pitch'); }
    if (sw.stage === stage && !done && stage !== 'aim') syncTo(sw.t);
    if (stage === 'pitch' && typeof sw.power === 'number') arcPower = sw.power;
  }

  function arcRefresh(goal) {
    arcArt(goal);
    var aiming = stage === 'aim' || stage === '';
    arcField.classList.toggle('hidden', !aiming);
    arcGauges.classList.toggle('hidden', aiming);
    arcPosts.classList.toggle('hidden', !goal);
    arcDown.classList.toggle('hidden', goal);
    arcWind.classList.toggle('hidden', !wind);
    arcWind.textContent = windWords();
    arcMark.classList.toggle('hidden', !goal || typeof sweep.need !== 'number');
    arcBtn.classList.toggle('hidden', done);
    arcBtn.textContent = aiming ? 'HIKE' : stage === 'power' ? 'TAP FOR POWER' : 'TAP FOR PITCH';
    arcBtn.className = 'big karcbtn ' + (aiming ? 'aim' : 'go') + (done ? ' hidden' : '');
    arcBtn.setAttribute('aria-label', aiming ? 'Hike: snap the ball with the kick aimed where the arrow points'
      : stage === 'power' ? 'Tap to stop the power meter' : 'Tap to stop the pitch angle and kick');
    if (!done) {
      arcHint.textContent = aiming ? 'Steer the arrow with ◀ ▶ (or drag), then HIKE'
        : stage === 'power' ? 'Tap to stop the power. Not in the red!' : 'Tap to stop the angle. ' + (goal ? 'Green clears the bar' : 'Green hangs it high');
    }
    // the shaded part of the dial: a field goal's climbs that clear the bar (once the power is set), a punt's high hang
    var win = goal ? sweep.clear : sweep.good;
    var lo = win && win.length === 2 ? win[0] : 0, hi = win && win.length === 2 ? win[1] : -1;
    arcWedge.classList.toggle('hidden', !(hi >= lo) || aiming);
    if (hi >= lo) arcWedge.style.background = 'conic-gradient(rgba(95,211,138,0) 0deg ' + (90 - hi) + 'deg, rgba(95,211,138,.6) ' +
      (90 - hi) + 'deg ' + (90 - lo) + 'deg, rgba(95,211,138,0) ' + (90 - lo) + 'deg)';
    arcGem.classList.toggle('hidden', goal || !(hi >= lo) || aiming);
    if (!goal && hi >= lo) {
      var a = ART.punt, mid = (lo + hi) / 2 * Math.PI / 180, r = a.wedge * 0.97;
      arcGem.style.left = ((a.pivotX + Math.cos(mid) * r) * 100) + '%';
      arcGem.style.top = ((a.pivotY - Math.sin(mid) * r / a.tall) * 100) + '%';
      arcGem.style.transform = 'translate(-50%, -50%) rotate(' + (90 - (lo + hi) / 2) + 'deg)';
    }
    if (typeof sweep.need === 'number') {
      var A = ART[arcSet];
      arcMark.style.top = ((A.zeroY - (A.zeroY - A.fullY) * sweep.need) * 100) + '%';
    }
    arcDraw();
    if (!done && !raf && !aiming) raf = requestAnimationFrame(arcFrame);
  }

  // one picture: the arrow's lean on the little field, the meter's fill and pointer, the needle on the dial
  function arcDraw(stopped) {
    if (!arcBox || !sweep) return;
    var a = ART[arcSet] || ART.punt;
    var lean = arcAim * (sweep.aimDeg > 0 ? sweep.aimDeg : 15);
    arcArrow.style.transform = 'rotate(' + lean + 'deg)';
    var power = stage === 'power' && !done ? powerNow(since()) : stage === 'pitch' || done ? arcPower : 0;
    var share = typeof stopped === 'number' ? stopped : stage === 'pitch' && !done ? pitchShare(since()) : done && last.pitch !== undefined ? last.pitch : 0;
    arcFill.style.clipPath = 'inset(' + ((1 - power) * 100) + '% 0 0 0)';
    arcPointer.style.top = ((a.zeroY - (a.zeroY - a.fullY) * power) * 100) + '%';
    arcPointer.style.left = (a.chanR * 100) + '%';
    arcPointer.classList.toggle('hot', power >= sweep.shank);
    arcNeedle.style.transform = 'translateY(-50%) rotate(' + (-pitchDeg(share)) + 'deg)';
  }

  function arcFrame() {
    raf = 0;
    if (!sweep || !sweep.arc || done || !root || root.classList.contains('hidden')) return;
    arcDraw();
    if (stage === 'power' || stage === 'pitch') raf = requestAnimationFrame(arcFrame);
  }

  window.PPKick = {
    onMessage: function (msg) {
      if (msg.t === 'ui') {
        var was = layoutKick;
        layoutKick = msg.layout === 'kick';
        if (!layoutKick) { meterOn = null; sweep = null; }
        if (layoutKick && !was) { build(); reset(); }
        refresh();
      } else if (msg.t === 'kickmeter') {
        meterOn = !!msg.on;
        if (msg.on) { kind = msg.kind || ''; yards = msg.yards || 0; }
        else sweep = null;
        // the two taps: the game's own stage leads (it snaps the ball itself if he waits too long)
        if (msg.on && msg.sweep && msg.sweep.arc) { build(); arcMessage(msg.sweep, msg.wind); }
        else if (msg.on && msg.sweep) {
          build();
          sweep = msg.sweep; wind = msg.wind || null;
          if (msg.sweep.stage === 'power' && stage !== 'power') { if (typeof msg.sweep.aim === 'number') setAim = msg.sweep.aim; startStage('power'); }
          else if (msg.sweep.stage === 'aim' && !stage) startStage('aim');
          // kickSync: run on the game's clock (a late word about a stage this phone has left changes nothing)
          if (msg.sweep.stage === stage && !done) {
            syncTo(msg.sweep.t);
            if (stage === 'power' && typeof msg.sweep.aim === 'number') setAim = msg.sweep.aim;
          }
        }
        refresh();
      }
    },
    // for tests: what is on show
    state: function () {
      return { shown: !!root && !root.classList.contains('hidden'), kind: kind, done: done, last: last, stage: stage, sweep: !!sweep,
        arc: !!(sweep && sweep.arc), aim: arcAim, power: arcPower };
    }
  };
})();
