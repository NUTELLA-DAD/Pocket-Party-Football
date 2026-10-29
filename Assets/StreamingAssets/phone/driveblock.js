// Pocket Party Football phone: the linemen's drive sticks (the game's FEATURES.driveBlock and FEATURES.driveRush).
// When the game sends the 'block' layout with driveStick: true, layouts.js asks this file for the right-hand side:
// a small SPRINT gem and, instead of the BLOCK gem, a BLOCK stick.
//   Touch the stick: BLOCK is held ({ t:'btn', b:'block', d:1, aid }), exactly like holding the old button.
//   Slide your thumb: the man you have locked up is shoved that way. While held it streams
//   { t:'drive', x, y, aid } at DRIVE_HZ (x right, y DOWN, -1..1, the same frame as the move stick).
//   Let go: { t:'drive', x:0, y:0, aid } then { t:'btn', b:'block', d:0, aid }.
// Sideways is the easy shove (he leans at the ball); straight back is hard. The left stick still moves you.
// The 'rush' layout with driveStick: true is the defender's mirror: a column of small TACKLE, SWIM and SWAT gems and,
// instead of the BULL RUSH gem, a BULL RUSH stick. Touching it is the bull rush press ({ t:'btn', b:'bull', d:1 }),
// sliding shoves the blocker who has you held up the same way (the same 'drive' messages), letting go sends the zero
// drive and then { t:'btn', b:'bull', d:0 }. Sideways rips you free and opens a gap; straight at the QB is hard.
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

  // A small round gem (SPRINT, or the rusher's TACKLE / SWIM / SWAT) in the column beside the stick. hold: a held
  // button (HOLD under the label), else a tap.
  function gem(box, press, action, label, colour, aria, hold) {
    var sp = el('button', 'btn g-' + colour + ' dsprint dgem-' + action);
    sp.setAttribute('data-act', action);
    sp.setAttribute('aria-label', aria);
    sp.appendChild(el('span', 'lb', label));
    if (hold) sp.appendChild(el('span', 'hk', 'HOLD'));
    sp.addEventListener('pointerdown', function (e) {
      e.preventDefault();
      try { sp.setPointerCapture(e.pointerId); } catch (err) {}
      sp.classList.add('on'); press(action, true);
    });
    function spUp() { if (sp.classList.contains('on')) { sp.classList.remove('on'); press(action, false); } }
    sp.addEventListener('pointerup', spUp);
    sp.addEventListener('pointercancel', spUp);
    box.appendChild(sp);
    return sp;
  }

  // Adds SPRINT and the BLOCK stick to box. press(action, down) sends a btn message; send sends any message; aid()
  // is the screen on show.
  function add(box, press, send, aid) {
    gem(box, press, 'sprint', 'SPRINT', 'teal', 'SPRINT: hold to run faster', true);
    stick(box, press, send, aid, 'block', 'BLOCK', 'BLOCKING', 'BLOCK stick: touch and hold to block, slide to shove your man that way');
  }

  // The defender's side (FEATURES.driveRush): TACKLE, SWIM and SWAT in a column, then the BULL RUSH stick.
  function addRush(box, press, send, aid) {
    var col = el('div', 'dcol');
    gem(col, press, 'tackle', 'TACKLE', 'red', 'TACKLE: bring down the ball carrier');
    gem(col, press, 'swim', 'SWIM', 'yellow', 'SWIM: a rush move, slips the block');
    gem(col, press, 'swat', 'SWAT', 'blue', 'SWAT: bat the pass down');
    box.appendChild(col);
    stick(box, press, send, aid, 'bull', 'BULL RUSH', 'RUSHING', 'BULL RUSH stick: touch for a bull rush, hold and slide to shove your blocker that way');
  }

  // The stick itself: act is the button it holds (block / bull), label its resting name, holding what it says while
  // touched in the middle, aria its aria-label.
  function stick(box, press, send, aid, act, label, holding, aria) {
    var ds = el('div', 'tstick dstick');
    ds.setAttribute('role', 'button');
    ds.setAttribute('data-act', act);
    ds.setAttribute('aria-label', aria);
    var arrows = el('div', 'darrows'); ds.appendChild(arrows);
    var knob = el('div', 'tknob'); ds.appendChild(knob);
    var tag = el('div', 'ttag', label); ds.appendChild(tag);
    var id = null, x = 0, y = 0, timer = null;

    function driveMsg() { send({ t: 'drive', x: x, y: y, aid: aid() }); }
    function measure(e) {
      var b = ds.getBoundingClientRect(), r = b.width / 2;
      var dx = (e.clientX - b.left - r) / r, dy = (e.clientY - b.top - r) / r;
      var m = Math.sqrt(dx * dx + dy * dy);
      if (m > 1) { dx /= m; dy /= m; m = 1; }
      x = Math.round(dx * 100) / 100; y = Math.round(dy * 100) / 100;
      knob.style.transform = 'translate(' + (dx * r * 0.6) + 'px,' + (dy * r * 0.6) + 'px)';
      tag.textContent = m < DEAD ? holding : 'DRIVE!';
      ds.classList.toggle('driving', m >= DEAD);
    }
    function stop() {
      if (id === null) return;
      id = null; clearInterval(timer); timer = null; stopFn = null;
      x = 0; y = 0; driveMsg();
      ds.classList.remove('on', 'driving');
      knob.style.transform = ''; tag.textContent = label;
      press(act, false);
    }
    ds.addEventListener('pointerdown', function (e) {
      e.preventDefault();
      if (id !== null) return;
      try { ds.setPointerCapture(e.pointerId); } catch (err) {}
      id = e.pointerId; ds.classList.add('on');
      press(act, true);
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
    rushLine: 'Hold BULL RUSH on your blocker, slide it to shove him. Sideways rips you free!',
    add: add,
    addRush: addRush,   // FEATURES.driveRush
    // Let go (layout change, screen lock): sends the drive zero and the button's release. Returns true if it was held.
    stop: function () { if (!stopFn) return false; stopFn(); return true; }
  };
})();
