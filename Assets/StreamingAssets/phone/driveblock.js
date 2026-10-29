// Pocket Party Football phone: the lineman's drive stick (the game's FEATURES.driveBlock).
// When the game sends the 'block' layout with driveStick: true, layouts.js asks this file for the right-hand side:
// a small SPRINT gem and, instead of the BLOCK gem, a BLOCK stick.
//   Touch the stick: BLOCK is held ({ t:'btn', b:'block', d:1, aid }), exactly like holding the old button.
//   Slide your thumb: the man you have locked up is shoved that way. While held it streams
//   { t:'drive', x, y, aid } at DRIVE_HZ (x right, y DOWN, -1..1, the same frame as the move stick).
//   Let go: { t:'drive', x:0, y:0, aid } then { t:'btn', b:'block', d:0, aid }.
// Sideways is the easy shove (he leans at the ball); straight back is hard. The left stick still moves you.
(function () {
  'use strict';
  var DRIVE_HZ = 20, DEAD = 0.25;
  var stopFn = null;

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text) e.textContent = text;
    return e;
  }

  // Adds SPRINT and the BLOCK stick to box. press(action, down) sends a btn message; send sends any message; aid()
  // is the screen on show.
  function add(box, press, send, aid) {
    var sp = el('button', 'btn g-teal dsprint');
    sp.setAttribute('data-act', 'sprint');
    sp.setAttribute('aria-label', 'SPRINT: hold to run faster');
    sp.appendChild(el('span', 'lb', 'SPRINT'));
    sp.appendChild(el('span', 'hk', 'HOLD'));
    sp.addEventListener('pointerdown', function (e) {
      e.preventDefault();
      try { sp.setPointerCapture(e.pointerId); } catch (err) {}
      sp.classList.add('on'); press('sprint', true);
    });
    function spUp() { if (sp.classList.contains('on')) { sp.classList.remove('on'); press('sprint', false); } }
    sp.addEventListener('pointerup', spUp);
    sp.addEventListener('pointercancel', spUp);
    box.appendChild(sp);

    var ds = el('div', 'tstick dstick');
    ds.setAttribute('role', 'button');
    ds.setAttribute('data-act', 'block');
    ds.setAttribute('aria-label', 'BLOCK stick: touch and hold to block, slide to shove your man that way');
    var arrows = el('div', 'darrows'); ds.appendChild(arrows);
    var knob = el('div', 'tknob'); ds.appendChild(knob);
    var tag = el('div', 'ttag', 'BLOCK'); ds.appendChild(tag);
    var id = null, x = 0, y = 0, timer = null;

    function driveMsg() { send({ t: 'drive', x: x, y: y, aid: aid() }); }
    function measure(e) {
      var b = ds.getBoundingClientRect(), r = b.width / 2;
      var dx = (e.clientX - b.left - r) / r, dy = (e.clientY - b.top - r) / r;
      var m = Math.sqrt(dx * dx + dy * dy);
      if (m > 1) { dx /= m; dy /= m; m = 1; }
      x = Math.round(dx * 100) / 100; y = Math.round(dy * 100) / 100;
      knob.style.transform = 'translate(' + (dx * r * 0.6) + 'px,' + (dy * r * 0.6) + 'px)';
      tag.textContent = m < DEAD ? 'BLOCKING' : 'DRIVE!';
      ds.classList.toggle('driving', m >= DEAD);
    }
    function stop() {
      if (id === null) return;
      id = null; clearInterval(timer); timer = null; stopFn = null;
      x = 0; y = 0; driveMsg();
      ds.classList.remove('on', 'driving');
      knob.style.transform = ''; tag.textContent = 'BLOCK';
      press('block', false);
    }
    ds.addEventListener('pointerdown', function (e) {
      e.preventDefault();
      if (id !== null) return;
      try { ds.setPointerCapture(e.pointerId); } catch (err) {}
      id = e.pointerId; ds.classList.add('on');
      press('block', true);
      measure(e); driveMsg();
      timer = setInterval(driveMsg, 1000 / DRIVE_HZ);
      stopFn = stop;
    });
    ds.addEventListener('pointermove', function (e) { if (e.pointerId === id) measure(e); });
    ds.addEventListener('pointerup', function (e) { if (e.pointerId === id) stop(); });
    ds.addEventListener('pointercancel', function (e) { if (e.pointerId === id) stop(); });
    box.appendChild(ds);
  }

  window.PPDrive = {
    line: 'Hold BLOCK on your man, slide it to shove him. Sideways opens a gap!',
    add: add,
    // Let go (layout change, screen lock): sends the drive zero and the BLOCK release. Returns true if it was held.
    stop: function () { if (!stopFn) return false; stopFn(); return true; }
  };
})();
