// Pocket Party Football: the final card on the phone (WS-12). When the game ends the TV sends `postgame`: the final
// score, the winner, the player of the game, both teams' totals (public) and this person's own numbers (`mine`, only
// to this phone). The card covers the screen until HIDE; it comes back only if the numbers change, and goes away
// when a new huddle starts. Names are set as text, never as HTML. Offline: no fonts or files from the internet.
(function () {
  'use strict';
  var last = '', hiddenFor = '';

  var css = [
    '#postgame { position: fixed; inset: 0; z-index: 50; background: rgba(9, 26, 20, .97); color: var(--chalk, #f6f1de);',
    '  display: flex; align-items: center; justify-content: center; padding: 12px; touch-action: none; overflow: hidden; }',
    '#postgame .card { width: 100%; max-width: 640px; max-height: 100%; display: grid; gap: 10px; align-content: center; min-height: 0; }',
    '#postgame .final { font-family: var(--display, system-ui); font-size: 15px; letter-spacing: .14em; color: var(--muted, #a8c4b4); margin: 0; text-align: center; }',
    '#postgame .win { font-family: var(--display, system-ui); font-size: 28px; margin: 0; text-align: center; }',
    '#postgame .teams { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }',
    '#postgame .team { border-radius: 14px; padding: 8px 10px; display: grid; gap: 2px; min-width: 0; box-shadow: 0 4px 0 rgba(0,0,0,.35); }',
    '#postgame .team .nm { font-family: var(--display, system-ui); font-size: 15px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }',
    '#postgame .team .sc { font-family: var(--display, system-ui); font-size: 40px; line-height: 1; font-variant-numeric: tabular-nums; }',
    '#postgame .team .tot { font-size: 12px; opacity: .9; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }',
    '#postgame .mine { background: var(--ink, #0d2219); border-radius: 14px; padding: 10px 12px; display: grid; gap: 4px; }',
    '#postgame .mine .who { font-size: 13px; color: var(--muted, #a8c4b4); margin: 0; }',
    '#postgame .mine .line { font-family: var(--display, system-ui); font-size: 20px; margin: 0; overflow-wrap: anywhere; }',
    '#postgame .mvp { font-size: 13px; color: var(--warn, #ffcf4a); margin: 0; text-align: center; overflow-wrap: anywhere; }',
    '#postgame .hide { justify-self: center; min-width: 160px; }',
    '@media (orientation: landscape) and (max-height: 500px) {',
    '  #postgame .card { grid-template-columns: 1fr 1fr; max-width: 900px; column-gap: 14px; }',
    '  #postgame .final, #postgame .win, #postgame .mvp, #postgame .hide { grid-column: 1 / -1; }',
    '  #postgame .win { font-size: 24px; }',
    '  #postgame .team .sc { font-size: 34px; }',
    '}'
  ].join('\n');

  function el(tag, cls, text, parent) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = String(text);
    if (parent) parent.appendChild(e);
    return e;
  }

  var root = null;
  function mount() {
    if (root) return root;
    var st = document.createElement('style');
    st.textContent = css;
    document.head.appendChild(st);
    root = el('section', 'hidden');
    root.id = 'postgame';
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-label', 'Final score');
    document.body.appendChild(root);
    return root;
  }

  function teamName(t) { return (t && (t.name || t.short)) || ''; }

  function draw(msg) {
    var r = mount();
    r.textContent = '';
    var card = el('div', 'card', null, r);
    var teams = msg.teams || [];
    el('p', 'final', 'FINAL', card);
    var w = msg.winner;
    el('h2', 'win', w >= 0 && teams[w] ? teamName(teams[w]).toUpperCase() + ' WIN!' : "IT'S A TIE!", card);
    var row = el('div', 'teams', null, card);
    teams.forEach(function (t) {
      var b = el('div', 'team', null, row);
      b.style.background = t.color || '#335';
      b.style.color = '#fff';
      el('span', 'nm', teamName(t).toUpperCase(), b);
      el('span', 'sc', t.score | 0, b);
      el('span', 'tot', (t.yds | 0) + ' YDS · ' + (t.tds | 0) + ' TD · ' + (t.picks | 0) + ' INT', b);
    });
    var m = msg.mine;
    if (m) {
      var box = el('div', 'mine', null, card);
      el('p', 'who', 'YOU  #' + (m.number | 0) + ' ' + (m.pos || '') + (teams[m.team] ? '  ·  ' + teamName(teams[m.team]).toUpperCase() : ''), box);
      el('p', 'line', m.line || 'NO STATS', box);
    }
    if (msg.mvp) el('p', 'mvp', msg.mvp, card);
    var hide = el('button', 'big hide', 'HIDE', card);
    hide.setAttribute('aria-label', 'Hide the final score card');
    hide.addEventListener('click', function () { hiddenFor = last; r.classList.add('hidden'); });
    r.classList.toggle('hidden', hiddenFor === last);
  }

  function onMessage(msg) {
    if (!msg) return;
    if (msg.t === 'huddle') {
      // a new game is on: the card goes, and the next final shows again
      last = ''; hiddenFor = '';
      if (root) root.classList.add('hidden');
      return;
    }
    if (msg.t !== 'postgame') return;
    var key = JSON.stringify(msg);
    if (key === last) return;
    last = key;
    draw(msg);
  }

  window.PostGame = { onMessage: onMessage };
})();
