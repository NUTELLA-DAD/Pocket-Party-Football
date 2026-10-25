// Pocket Party Football phone layouts: the right-hand buttons for each moment of a play (M1-f).
// The game picks the layout with a `layout` message; this file only draws buttons and reports presses.
// Usage from app.js:
//   PPLayouts.mount(document.getElementById('btnBox'), send, function () { return aid; });
//   PPLayouts.show('qb', { receivers: [{ shape: '△', label: 'WR #11' }] });
// Presses go out as { t:'btn', b, d:1|0, aid } like every other button; a throw adds tgt and lob,
// a kick sends { t:'kick', power, aim, aid }.
(function () {
  'use strict';
  var LOB_HOLD_MS = 350, KICK_AUTO_MS = 5000;

  // One row per layout: the line under the name plate, whether the stamina bar shows, and the buttons.
  // A button: [action, label, style, hint, hold]. style: 'main' = team colour, 'alt' = second colour, '' = chalk.
  var LAYOUTS = {
    wait:    { line: 'A bot is playing your spot. You\'re back next play.', stam: false,
               btns: [['find', 'FIND ME', 'main', 'Flash your ring on the TV']] },
    qbpre:   { line: 'Tap HIKE when everyone is set', stam: false,
               btns: [['hike', 'HIKE', 'main', 'Starts the play'], ['find', 'FIND ME', '', 'Flash your ring on the TV']] },
    qb:      { line: 'Tap = bullet pass · hold = lob', stam: false, receivers: true,
               btns: [['scramble', 'SCRAMBLE', 'alt', 'Run it yourself']] },
    carrier: { line: 'Ball in hands! Run for it', stam: true,
               btns: [['sprint', 'SPRINT', 'main', 'Hold', true], ['juke', 'JUKE', 'alt', 'Stick picks the side'],
                      ['spin', 'SPIN', ''], ['stiff', 'STIFF ARM', '']] },
    route:   { line: 'Run your route', stam: true,
               btns: [['sprint', 'SPRINT', 'main', 'Hold', true], ['call', 'CALL FOR IT', 'alt', 'Your shape pulses on the TV'],
                      ['catch', 'CATCH', '', 'Jump for the ball']] },
    block:   { line: 'Hold BLOCK next to a defender', stam: true,
               btns: [['block', 'BLOCK', 'main', 'Hold to lock on', true], ['sprint', 'SPRINT', '', 'Hold', true]] },
    rush:    { line: 'Beat your blocker, get the QB', stam: true,
               btns: [['swim', 'SWIM', 'alt', 'Rush move'], ['bull', 'BULL RUSH', ''],
                      ['tackle', 'TACKLE', 'main'], ['swat', 'SWAT', '', 'Bat the pass']] },
    lb:      { line: 'Read run or pass, then hit', stam: true,
               btns: [['sprint', 'SPRINT', '', 'Hold', true], ['tackle', 'TACKLE', 'main'],
                      ['dive', 'DIVE TACKLE', 'alt', 'Big reach, slow to get up'], ['swat', 'SWAT', '']] },
    cover:   { line: 'Stay with your man. Jump for the pick', stam: true,
               btns: [['sprint', 'SPRINT', '', 'Hold', true], ['tackle', 'TACKLE', 'main'],
                      ['jump', 'JUMP', 'alt', 'Pick or swat'], ['dive', 'DIVE TACKLE', '']] },
    kick:    { line: 'Drag up from the ball: longer = harder, sideways = aim', stam: false, kick: true, noStick: true, btns: [] }
  };

  var box = null, sendFn = function () {}, aidFn = function () { return ''; };
  var current = '', kickTimer = null;

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
  function addButton(spec) {
    var action = spec[0], label = spec[1], style = spec[2], hint = spec[3];
    var b = el('button', 'btn' + (style ? ' ' + style : ''), label);
    b.setAttribute('data-act', action);
    b.setAttribute('aria-label', label + (hint ? ': ' + hint : ''));
    if (hint) b.appendChild(el('span', 'k', hint));
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
  function addReceiver(r) {
    var b = el('button', 'btn rcv');
    b.setAttribute('data-act', 'throw');
    b.setAttribute('data-tgt', r.shape);
    b.setAttribute('aria-label', 'Throw to ' + r.shape + ' ' + r.label + ': tap for a bullet, hold for a lob');
    b.appendChild(el('span', 'shape', r.shape));
    b.appendChild(document.createTextNode(r.label));
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
    box.appendChild(b);
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
      if (L.receivers) (data.receivers || []).forEach(addReceiver);
      L.btns.forEach(addButton);
      if (L.kick) addKickPad();
      return { stam: L.stam, stick: !L.noStick, line: data.line || L.line, layout: name };
    },

    current: function () { return current; },

    // Let go of everything (screen lock, layout change mid-press): every held button sends its release.
    releaseAll: function () {
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
