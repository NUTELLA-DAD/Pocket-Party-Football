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
(function () {
  'use strict';
  var MIN_POWER = 0.08, FULL_SWIPE = 0.75, STEPS = 10;
  var root = null, field, title, sub, hint, line, fill, pct, posts, arrow;
  var layoutKick = false, meterOn = null, kind = '', yards = 0;
  var pointer = null, x0 = 0, y0 = 0, done = false, last = { power: 0, aim: 0 };
  // the two taps (sweep): which arrow is sweeping, since when (ms), the aim the first tap set
  var sweep = null, stage = '', stageAt = 0, setAim = 0, raf = 0, wind = null, tapBtn, flagL, flagR, windLine;

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
  function since() { return (performance.now() - stageAt) / 1000; }

  function startStage(name) {
    if (stage === name) return;
    stage = name; stageAt = performance.now();
    tapBtn.textContent = name === 'aim' ? 'TAP TO AIM' : 'TAP TO KICK';
    tapBtn.className = 'big ktap ' + (name === 'aim' ? 'aim' : 'go');
    if (!raf) raf = requestAnimationFrame(frame);
  }

  function tap() {
    if (done || !sweep) return;
    if (stage === 'aim') {
      setAim = Math.round(aimNow(since()) * 100) / 100;
      if (window.PP) window.PP.send({ t: 'kickaim', aim: setAim, aid: window.PP.aid() });
      startStage('power');
      return;
    }
    if (stage !== 'power') return;
    var p = Math.round(powerNow(since()) * 100) / 100;
    done = true;
    if (window.PP) window.PP.send({ t: 'kick', power: p, aim: setAim, aid: window.PP.aid() });
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
    line.style.height = (stage === 'aim' ? track * 0.45 : Math.max(10, track * power)) + 'px';
    line.style.transform = 'rotate(' + (aim * 30) + 'deg)';
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
    windLine.textContent = !wind ? '' : mph < 1 ? 'No wind' : 'Wind ' + mph + ' mph ' +
      (Math.abs(right) >= Math.abs(along) ? (right > 0 ? 'left to right \u2192' : '\u2190 right to left') : along > 0 ? 'at your back \u2191' : 'in your face \u2193');
  }

  function reset() {
    done = false; pointer = null; stage = ''; setAim = 0;
    posts.style.top = ''; posts.style.bottom = '';
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
    root.classList.toggle('sweep', !!sweep);
    tapBtn.classList.toggle('hidden', !sweep || done);
    if (sweep && show) {
      placeSweep();
      if (!done) {
        hint.textContent = stage === 'power' ? 'Tap when the arrow is over the bar. Not in the red!' : 'Tap when the arrow points where you want it. Mind the wind';
        if (!raf) raf = requestAnimationFrame(frame);
      }
    }
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
        if (msg.on && msg.sweep) {
          build();
          sweep = msg.sweep; wind = msg.wind || null;
          if (msg.sweep.stage === 'power' && stage !== 'power') { if (typeof msg.sweep.aim === 'number') setAim = msg.sweep.aim; startStage('power'); }
          else if (msg.sweep.stage === 'aim' && !stage) startStage('aim');
        }
        refresh();
      }
    },
    // for tests: what is on show
    state: function () { return { shown: !!root && !root.classList.contains('hidden'), kind: kind, done: done, last: last, stage: stage, sweep: !!sweep }; }
  };
})();
