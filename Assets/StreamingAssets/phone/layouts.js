// Pocket Party Football phone layouts: the right-hand buttons for each moment of a play (M1-f).
// The game picks the layout with a `layout` message; this file only draws buttons and reports presses.
// Usage from app.js:
//   PPLayouts.mount(document.getElementById('btnBox'), send, function () { return aid; });
//   PPLayouts.show('qb', { receivers: [{ shape: '△', label: 'WR #11' }] });
// Presses go out as { t:'btn', b, d:1|0, aid } like every other button; a throw adds tgt and lob,
// a kick sends { t:'kick', power, aim, aid }.
// With throwStick the QB gets a second stick that aims the pass: while held it streams { t:'aim', x, y, arc, aid },
// on release { t:'throw', x, y, arc, aid }, let go in the middle to cancel ({ t:'aim', cancel:1, aid }).
// x, y are the raw stick (-1..1, same frame as the move stick). arc 0 = flat bullet, 1 = high lob: a quick flick is a
// bullet, and the longer you hold the aim the higher the arc (full lob after LOB_FULL_MS).
(function () {
  'use strict';
  var LOB_HOLD_MS = 350, KICK_AUTO_MS = 5000;
  var AIM_HZ = 20, FLICK_MS = 150, LOB_FULL_MS = 800, AIM_DEAD = 0.25;

  // One row per layout: the line under the player tag, whether the stamina bar shows, and the buttons.
  // A button: [action, label, colour, hint, hold]. The first button is the big one under the right thumb, then the
  // ring around it (like a game pad's A, B, Y, X). The colour belongs to the action, so SPRINT is always teal and
  // TACKLE always red, whatever the moment.
  var LAYOUTS = {
    wait:    { line: 'A bot is playing your spot. You\'re back next play.', stam: false,
               btns: [['find', 'FIND ME', 'purple', 'Flash your ring on the TV']] },
    qbpre:   { line: 'Tap HIKE when everyone is set', stam: false,
               btns: [['hike', 'HIKE', 'teal', 'Starts the play'], ['find', 'FIND ME', 'purple', 'Flash your ring on the TV']] },
    qb:      { line: 'Tap = bullet pass · hold = lob', stam: false, receivers: true,
               btns: [['scramble', 'SCRAMBLE', 'teal', 'Run it yourself']] },
    carrier: { line: 'Ball in hands! Run for it', stam: true,
               btns: [['sprint', 'SPRINT', 'teal', 'Hold', true], ['stiff', 'STIFF ARM', 'red', 'Keeps off a man in front'],
                      ['juke', 'JUKE', 'yellow', 'Stick picks the side'], ['spin', 'SPIN', 'purple'],
                      ['divein', 'DIVE', 'orange', 'Near the goal line']] },
    route:   { line: 'Run your route', stam: true,
               btns: [['sprint', 'SPRINT', 'teal', 'Hold', true], ['catch', 'CATCH', 'red', 'Jump for the ball'],
                      ['call', 'CALL FOR IT', 'yellow', 'Your shape pulses on the TV']] },
    block:   { line: 'Hold BLOCK next to a defender', stam: true,
               btns: [['block', 'BLOCK', 'red', 'Hold to lock on', true], ['sprint', 'SPRINT', 'teal', 'Hold', true]] },
    rush:    { line: 'Beat your blocker, get the QB', stam: true,
               btns: [['tackle', 'TACKLE', 'red'], ['swim', 'SWIM', 'yellow', 'Rush move'],
                      ['bull', 'BULL RUSH', 'purple', 'Drive him back'], ['swat', 'SWAT', 'blue', 'Bat the pass']] },
    lb:      { line: 'Read run or pass, then hit', stam: true,
               btns: [['tackle', 'TACKLE', 'red'], ['sprint', 'SPRINT', 'teal', 'Hold', true],
                      ['dive', 'DIVE TACKLE', 'orange', 'Big reach, slow to get up'], ['swat', 'SWAT', 'blue', 'Bat the pass']] },
    cover:   { line: 'Stay with your man. Jump for the pick', stam: true,
               btns: [['tackle', 'TACKLE', 'red'], ['sprint', 'SPRINT', 'teal', 'Hold', true],
                      ['jump', 'JUMP', 'yellow', 'Pick or swat'], ['dive', 'DIVE TACKLE', 'orange', 'Big reach, slow to get up']] },
    kick:    { line: 'Drag up from the ball: longer = harder, sideways = aim', stam: false, kick: true, noStick: true, btns: [] }
  };

  // Little line pictures on the gems (24 x 24, drawn with the text colour). Offline: plain SVG paths, no icon font.
  var ICONS = {
    sprint: 'M5 6l6 6-6 6M12 6l6 6-6 6',
    juke: 'M5 20l7-8-5-3 8-6m0 0h-5m5 0v5',
    spin: 'M20 12a8 8 0 1 1-2.6-5.9M20 3.5v5h-5',
    stiff: 'M8 13V6.5a1.5 1.5 0 0 1 3 0V11m0-6a1.5 1.5 0 0 1 3 0v6m0-5a1.5 1.5 0 0 1 3 0v8c0 4-2.5 7-6.5 7-3 0-5-2-6-5l-1.5-3.5a1.5 1.5 0 0 1 2.7-1.3L8 14',
    divein: 'M4 5l12 12M16 9v8H8M3 21h18',
    tackle: 'M12 2.5l2.2 5.6 5.8-2-2.6 5.4 4.8 2.7-5.6 1.1.9 6-4.6-3.8L8.6 21l.4-6-5.6-1 4.8-2.8-2.7-5.4 5.8 2.2z',
    dive: 'M3 12h13M11 6l6 6-6 6M3 20h18',
    swat: 'M12 21V9M7 13l5-5 5 5M5 4h14',
    jump: 'M12 19V5M6 11l6-6 6 6M8 21h8',
    call: 'M3 10v4h3l8 5V5l-8 5H3zM18 9a4 4 0 0 1 0 6M20.5 6.5a8 8 0 0 1 0 11',
    'catch': '~M3.5 12c3.5-6.5 13.5-6.5 17 0-3.5 6.5-13.5 6.5-17 0zM9 12h6M10.5 10.5v3M13.5 10.5v3',
    hike: 'M12 2.5v9M8 6.5l4-4 4 4M4.5 18c3.5-4 11.5-4 15 0-3.5 4-11.5 4-15 0z',
    block: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z',
    swim: 'M3 16c3-3 6-3 9 0s6 3 9 0M7 11l5-6 5 6',
    bull: 'M3 12h12M10 6l6 6-6 6M20 4v16',
    find: 'M12 2.5v4M12 17.5v4M2.5 12h4M17.5 12h4M12 8a4 4 0 1 1 0 8 4 4 0 0 1 0-8z',
    scramble: 'M13 2L4 14h7l-1 8 9-12h-7z'
  };
  function icon(action) {
    var d = ICONS[action];
    if (!d) return null;
    var s = document.createElementNS('http' + '://www.w3.org/2000/svg', 'svg');   // the SVG namespace name (not a download)
    s.setAttribute('viewBox', '0 0 24 24'); s.setAttribute('class', 'ic'); s.setAttribute('aria-hidden', 'true');
    var p = document.createElementNS('http' + '://www.w3.org/2000/svg', 'path');
    if (d.charAt(0) === '~') { d = d.slice(1); p.setAttribute('transform', 'rotate(-35 12 12)'); }   // ~ = a football, tipped
    p.setAttribute('d', d); s.appendChild(p);
    return s;
  }

  var box = null, sendFn = function () {}, aidFn = function () { return ''; };
  var current = '', kickTimer = null, aimStop = null;

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text) e.textContent = text;
    return e;
  }

  function press(name, down, extra) {
    var m = { t: 'btn', b: name, d: down ? 1 : 0, aid: aidFn() };
    if (extra) for (var k in extra) m[k] = extra[k];
    sendFn(m);
  }

  // A plain button: down on pointerdown, up on release; released buttons never send twice.
  // In the ring of gems each one takes the next spot: s0 = the big one, then s1..s4.
  function addButton(spec, i) {
    var action = spec[0], label = spec[1], colour = spec[2], hint = spec[3], hold = spec[4];
    var b = el('button', 'btn gem g-' + colour + ' s' + i + (hold ? ' hold' : ''));
    b.setAttribute('data-act', action);
    b.setAttribute('aria-label', label + (hint ? ': ' + hint : ''));
    var ic = icon(action);
    if (ic) b.appendChild(ic);
    b.appendChild(el('span', 'lb', label));
    if (hint) b.appendChild(el('span', 'k', hint));
    if (hold) b.appendChild(el('span', 'hk', 'HOLD'));
    b.addEventListener('pointerdown', function (e) {
      e.preventDefault();
      try { b.setPointerCapture(e.pointerId); } catch (err) {}
      b.classList.add('on');
      press(action, true);
    });
    function up() {
      if (!b.classList.contains('on')) return;
      b.classList.remove('on');
      press(action, false);
    }
    b.addEventListener('pointerup', up);
    b.addEventListener('pointercancel', up);
    box.appendChild(b);
  }

  // A receiver button: tap = bullet, hold past LOB_HOLD_MS = lob. The shape matches the badge over him on the TV.
  function addReceiver(r, parent) {
    var b = el('button', 'btn rcv g-ivory');
    b.setAttribute('data-act', 'throw');
    b.setAttribute('data-tgt', r.shape);
    b.setAttribute('aria-label', 'Throw to ' + r.shape + ' ' + r.label + ': tap for a bullet, hold for a lob');
    b.appendChild(el('span', 'shape', r.shape));
    b.appendChild(el('span', 'lb', r.label));
    if (r.open) b.classList.add('open');
    var t0 = 0;
    b.addEventListener('pointerdown', function (e) {
      e.preventDefault();
      try { b.setPointerCapture(e.pointerId); } catch (err) {}
      t0 = e.timeStamp; b.classList.add('on');
      press('throw', true, { tgt: r.shape });
    });
    function up(e) {
      if (!b.classList.contains('on')) return;
      b.classList.remove('on');
      press('throw', false, { tgt: r.shape, lob: (e.timeStamp - t0) >= LOB_HOLD_MS });
    }
    b.addEventListener('pointerup', up);
    b.addEventListener('pointercancel', up);
    (parent || box).appendChild(b);
  }

  // The throw stick: drag to aim (the TV draws the arc and where it lands), let go to throw.
  // The ring fills as you hold: empty = BULLET, full = LOB.
  function addThrowStick(parent) {
    var ts = el('div', 'tstick');
    ts.setAttribute('role', 'button');
    ts.setAttribute('aria-label', 'Throw stick: drag toward a receiver and let go to throw. Quick flick = bullet, hold longer = lob. Let go in the middle to cancel');
    var ring = el('div', 'tring'); ts.appendChild(ring);
    var knob = el('div', 'tknob'); ts.appendChild(knob);
    var tag = el('div', 'ttag', 'THROW'); ts.appendChild(tag);
    var id = null, t0 = 0, x = 0, y = 0, timer = null;
    function arcNow() {
      var held = performance.now() - t0;
      return held < FLICK_MS ? 0 : Math.round(Math.min(1, held / LOB_FULL_MS) * 100) / 100;
    }
    function draw() {
      var a = arcNow();
      ring.style.setProperty('--arc', a);
      tag.textContent = Math.sqrt(x * x + y * y) < AIM_DEAD ? 'THROW' : a < 0.34 ? 'BULLET' : a < 0.67 ? 'TOUCH' : 'LOB';
    }
    function aimMsg() { draw(); sendFn({ t: 'aim', x: x, y: y, arc: arcNow(), aid: aidFn() }); }
    function measure(e) {
      var b = ts.getBoundingClientRect(), r = b.width / 2;
      var dx = (e.clientX - b.left - r) / r, dy = (e.clientY - b.top - r) / r;
      var m = Math.sqrt(dx * dx + dy * dy);
      if (m > 1) { dx /= m; dy /= m; }
      x = Math.round(dx * 100) / 100; y = Math.round(dy * 100) / 100;
      knob.style.transform = 'translate(' + (dx * r * 0.6) + 'px,' + (dy * r * 0.6) + 'px)';
    }
    function stop(throwIt) {
      if (id === null) return;
      id = null; clearInterval(timer); timer = null; aimStop = null;
      var a = arcNow();
      if (throwIt && Math.sqrt(x * x + y * y) >= AIM_DEAD) {
        sendFn({ t: 'throw', x: x, y: y, arc: a, aid: aidFn() });
        tag.textContent = 'THROWN!';
      } else {
        sendFn({ t: 'aim', cancel: 1, aid: aidFn() });
        tag.textContent = 'THROW';
      }
      ts.classList.remove('on'); ring.style.setProperty('--arc', 0);
      knob.style.transform = ''; x = 0; y = 0;
    }
    ts.addEventListener('pointerdown', function (e) {
      e.preventDefault();
      if (id !== null) return;
      try { ts.setPointerCapture(e.pointerId); } catch (err) {}
      id = e.pointerId; t0 = performance.now(); ts.classList.add('on');
      measure(e); aimMsg();
      timer = setInterval(aimMsg, 1000 / AIM_HZ);
      aimStop = function () { stop(false); };
    });
    ts.addEventListener('pointermove', function (e) { if (e.pointerId === id) { measure(e); draw(); } });
    ts.addEventListener('pointerup', function (e) { if (e.pointerId === id) { measure(e); stop(true); } });
    ts.addEventListener('pointercancel', function (e) { if (e.pointerId === id) stop(false); });
    (parent || box).appendChild(ts);
  }

  // The kicking meter: drag up from the ball. Power = drag length over the pad height, aim = sideways offset.
  function addKickPad() {
    var pad = el('div', 'kickpad');
    pad.setAttribute('aria-label', 'Kicking pad: drag up from the ball and let go to kick');
    pad.appendChild(el('div', 'posts'));
    var ball = el('div', 'kball'); pad.appendChild(ball);
    var hint = el('div', 'khint', 'Drag up from the ball'); pad.appendChild(hint);
    var line = el('div', 'kline'); pad.appendChild(line);
    var start = null, done = false;
    function measure(e) {
      var b = pad.getBoundingClientRect();
      var x0 = b.width / 2, y0 = b.height - 24;
      var x = e.clientX - b.left, y = e.clientY - b.top;
      var power = Math.max(0, Math.min(1, (y0 - y) / (b.height - 60)));
      var aim = Math.max(-1, Math.min(1, (x - x0) / (b.width / 2)));
      var len = Math.sqrt((x - x0) * (x - x0) + (y - y0) * (y - y0));
      line.style.height = len + 'px';
      line.style.transform = 'rotate(' + Math.atan2(x - x0, y0 - y) + 'rad)';
      return { power: Math.round(power * 100) / 100, aim: Math.round(aim * 100) / 100 };
    }
    function kick(p) {
      if (done) return;
      done = true; clearTimeout(kickTimer);
      sendFn({ t: 'kick', power: p.power, aim: p.aim, aid: aidFn() });
      hint.textContent = 'Kicked! ' + Math.round(p.power * 100) + '% power';
      pad.classList.add('done');
    }
    pad.addEventListener('pointerdown', function (e) {
      if (done) return;
      e.preventDefault();
      try { pad.setPointerCapture(e.pointerId); } catch (err) {}
      start = e.pointerId; measure(e);
    });
    pad.addEventListener('pointermove', function (e) {
      if (e.pointerId !== start) return;
      var p = measure(e);
      hint.textContent = 'Power ' + Math.round(p.power * 100) + '%';
    });
    pad.addEventListener('pointerup', function (e) { if (e.pointerId === start) { start = null; kick(measure(e)); } });
    pad.addEventListener('pointercancel', function () { start = null; line.style.height = '0'; });
    box.appendChild(pad);
    // nobody swipes: the bot kicks; the game decides that, the phone just stops waiting
    clearTimeout(kickTimer);
    kickTimer = setTimeout(function () { if (!done) { done = true; hint.textContent = 'Too slow: the bot kicked'; pad.classList.add('done'); } }, KICK_AUTO_MS);
  }

  var PPLayouts = {
    names: Object.keys(LAYOUTS),

    mount: function (container, send, getAid) {
      box = container; sendFn = send || sendFn; aidFn = getAid || aidFn;
    },

    // data: { line?: string, receivers?: [{ shape, label, open? }] }. Returns { stam, stick, line, layout }:
    // the caller shows the stamina bar when stam is true and hides the stick (class no-stick on .pad) when stick is false.
    show: function (name, data) {
      var L = LAYOUTS[name];
      if (!L || !box) return null;
      data = data || {};
      PPLayouts.releaseAll();
      current = name;
      clearTimeout(kickTimer);
      box.innerHTML = '';
      box.className = 'btns layout-' + name;
      if (L.receivers && data.throwStick) {
        // two sticks: the receivers shrink to a column of shape buttons (tap still throws to that man)
        box.className += ' ts';
        var col = el('div', 'rcvs');
        (data.receivers || []).forEach(function (r) { addReceiver(r, col); });
        box.appendChild(col);
        addThrowStick();
        return { stam: L.stam, stick: true, line: data.line || 'Right stick: aim, let go to throw. Flick = bullet, hold = lob', layout: name };
      }
      if (L.receivers) (data.receivers || []).forEach(function (r) { addReceiver(r); });
      else if (!L.kick) box.className += ' cluster n' + L.btns.length;
      L.btns.forEach(addButton);
      if (L.kick) addKickPad();
      return { stam: L.stam, stick: !L.noStick, line: data.line || L.line, layout: name };
    },

    current: function () { return current; },

    // Let go of everything (screen lock, layout change mid-press): every held button sends its release.
    releaseAll: function () {
      if (aimStop) aimStop();
      if (!box) return;
      var held = box.querySelectorAll('.on');
      for (var i = 0; i < held.length; i++) {
        held[i].classList.remove('on');
        var act = held[i].getAttribute('data-act');
        if (act === 'throw') press('throw', false, { tgt: held[i].getAttribute('data-tgt'), lob: false });
        else press(act, false);
      }
    }
  };

  window.PPLayouts = PPLayouts;
})();
