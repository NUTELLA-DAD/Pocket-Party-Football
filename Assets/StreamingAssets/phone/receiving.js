// Couch Bowl: CALL FOR IT on the QB's phone (WS-05).
// The game sends { t:'call', k, shape, number, pos, ms } to the QB's phone only, when a receiver taps CALL FOR IT.
// That receiver's button pulses (two stepped looks, no smooth wobble) for ms, and the line under the name says who.
// The pulse stops by itself, or when the buttons change (the throw, the whistle).
(function () {
  'use strict';
  var timer = null, lit = null;

  // the pulse look lives here so the stylesheets stay untouched
  var css = document.createElement('style');
  css.textContent =
    '@keyframes pp-call { from { outline-color: #ffd34d; } to { outline-color: transparent; } }' +
    '.btn.rcv.calling { outline: 4px solid #ffd34d; outline-offset: -4px; animation: pp-call 0.25s steps(2, jump-none) infinite; }';
  document.head.appendChild(css);

  function stop() {
    clearTimeout(timer); timer = null;
    if (lit) lit.classList.remove('calling');
    lit = null;
  }

  function onCall(msg) {
    var box = document.getElementById('btnBox');
    if (!box) return;
    var btns = box.querySelectorAll('.btn.rcv');
    var b = null;
    for (var i = 0; i < btns.length; i++) if (btns[i].getAttribute('data-tgt') === msg.shape) b = btns[i];
    if (!b) return;
    stop();
    lit = b;
    b.classList.add('calling');
    var line = document.getElementById('line');
    if (line) line.textContent = msg.shape + ' ' + msg.pos + ' #' + msg.number + ' is calling for it!';
    timer = setTimeout(stop, Math.max(0, msg.ms || 0));
  }

  window.PPReceiving = { onCall: onCall, stop: stop };
})();
