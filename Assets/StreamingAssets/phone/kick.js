// Pocket Party Football: the kick meter (WS-01). Only the kicker's phone ever shows it: the game sends the 'kick'
// layout and { t:'kickmeter', on, kind, yards } to that one phone. It covers the whole screen while he lines up.
// Swipe up from anywhere on the field: the further up, the harder the kick; drift sideways to aim (right = the
// kicker's right). Letting go sends { t:'kick', power: 0..1, aim: -1..1, aid } with the aid of the screen on show.
// app.js hands every message to PPKick.onMessage; window.PP (app.js) sends and knows the aid.
(function () {
  'use strict';
  var MIN_POWER = 0.08, FULL_SWIPE = 0.75, STEPS = 10;
  var root = null, field, title, sub, hint, line, fill, pct, posts, arrow;
  var layoutKick = false, meterOn = null, kind = '', yards = 0;
  var pointer = null, x0 = 0, y0 = 0, done = false, last = { power: 0, aim: 0 };

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
    hint = el('p', 'line khint', '', root);
    hint.setAttribute('aria-live', 'polite');

    field.addEventListener('pointerdown', function (e) {
      if (done || pointer !== null) return;
      e.preventDefault();
      try { field.setPointerCapture(e.pointerId); } catch (err) {}
      pointer = e.pointerId; x0 = e.clientX; y0 = e.clientY;
      draw(measure(e));
    });
    field.addEventListener('pointermove', function (e) { if (e.pointerId === pointer) draw(measure(e)); });
    field.addEventListener('pointerup', function (e) {
      if (e.pointerId !== pointer) return;
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

  function reset() {
    done = false; pointer = null;
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
  }

  window.PPKick = {
    onMessage: function (msg) {
      if (msg.t === 'ui') {
        var was = layoutKick;
        layoutKick = msg.layout === 'kick';
        if (!layoutKick) meterOn = null;
        if (layoutKick && !was) { build(); reset(); }
        refresh();
      } else if (msg.t === 'kickmeter') {
        meterOn = !!msg.on;
        if (msg.on) { kind = msg.kind || ''; yards = msg.yards || 0; }
        refresh();
      }
    },
    // for tests: what is on show
    state: function () { return { shown: !!root && !root.classList.contains('hidden'), kind: kind, done: done, last: last }; }
  };
})();
