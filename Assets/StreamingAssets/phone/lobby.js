// Couch Bowl: the lobby (WS-07, FEATURES.lobby). Before kickoff each person picks one offense spot and one
// defense spot on their team (two-way players). A bot's spot is yours at once; a person's spot asks them to swap.
// Any phone taps KICK OFF to start. The game sends: ui {layout: 'lobby'}, lineup {...} (your team only).
// This page sends: spot {side, spot, aid}, swap {yes, aid}, kickoff {aid}, lobbyme {} (back to the lobby screen).
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var L = { held: false, lineup: null, shown: false, askedAt: 0, kicked: false };

  // our own stylesheet and screen, so index.html needs only the script tag
  var css = document.createElement('link');
  css.rel = 'stylesheet'; css.href = '/lobby.css';
  document.head.appendChild(css);
  var view = document.createElement('section');
  view.id = 'lobbyView';
  view.className = 'view lobby hidden';
  view.innerHTML =
    '<div class="card lhead"><div><h2 id="lTitle">LOBBY</h2><p class="sub">You play both ways: one spot on offense, one on defense.</p></div>' +
    '<div class="lyou" id="lYou"></div></div>' +
    '<div class="card lask hidden" id="lAsk" role="alertdialog" aria-live="assertive"><p id="lAskText"></p>' +
    '<div class="lrow"><button class="btn main" id="lYes" aria-label="Yes, swap spots">SWAP</button>' +
    '<button class="btn" id="lNo" aria-label="No, keep my spot">NO THANKS</button></div></div>' +
    '<p class="line" id="lLine" aria-live="polite"></p>' +
    '<div class="lcols">' +
    '<div class="lside"><h3>On offense</h3><div class="lspots" id="lOff"></div></div>' +
    '<div class="lside"><h3>On defense</h3><div class="lspots" id="lDef"></div></div>' +
    '</div>' +
    '<p class="sub" id="lCount"></p>' +
    '<div class="lrow"><button class="big" id="lKick" aria-label="Kick off: start the game for everyone">KICK OFF</button>' +
    '<button class="btn small" id="lTeam" aria-label="Change team">Change team</button></div>';
  (document.querySelector('.app') || document.body).appendChild(view);

  function send(o) { if (window.PP) window.PP.send(o); }
  function aid() { return window.PP ? window.PP.aid() : ''; }

  function show() {
    document.querySelectorAll('.view').forEach(function (v) { v.classList.toggle('hidden', v !== view); });
    L.shown = true;
  }
  function hide() { view.classList.add('hidden'); L.shown = false; }

  function spotButton(s, side, i) {
    var b = document.createElement('button');
    b.className = 'lspot' + (s.me ? ' me' : '') + (s.person && !s.me ? ' person' : '') + (s.away ? ' away' : '');
    var sideName = side === 'o' ? 'offense' : 'defense';
    b.setAttribute('aria-pressed', String(!!s.me));
    b.setAttribute('aria-label', s.me ? 'Your spot on ' + sideName + ': ' + s.pos + ' number ' + s.number
      : s.person ? 'Ask ' + s.who + ' to swap for ' + s.pos + ' number ' + s.number + ' on ' + sideName
      : 'Take the ' + s.pos + ' number ' + s.number + ' spot on ' + sideName + ' from a bot');
    var pos = document.createElement('b'); pos.textContent = s.pos + ' ' + s.number;
    var who = document.createElement('i'); who.textContent = s.me ? 'YOU' : s.away ? s.who + ' (away)' : s.who;
    b.appendChild(pos); b.appendChild(who);
    b.addEventListener('click', function () { if (!s.me) send({ t: 'spot', side: side, spot: i, aid: aid() }); });
    return b;
  }

  function label(list, i) { var s = list && list[i]; return s ? s.pos + ' #' + s.number : '-'; }

  function render() {
    var m = L.lineup;
    if (!m) return;
    $('lTitle').textContent = m.teamName + ' LOBBY';
    $('lYou').innerHTML = '';
    [['OFF', label(m.offense, m.you.o)], ['DEF', label(m.defense, m.you.d)]].forEach(function (r) {
      var d = document.createElement('div');
      var k = document.createElement('span'); k.textContent = r[0];
      d.appendChild(k); d.appendChild(document.createTextNode(' ' + r[1]));
      $('lYou').appendChild(d);
    });
    ['lOff', 'lDef'].forEach(function (id, k) {
      var box = $(id), list = k === 0 ? m.offense : m.defense;
      box.innerHTML = '';
      list.forEach(function (s, i) { box.appendChild(spotButton(s, k === 0 ? 'o' : 'd', i)); });
    });
    var ask = m.ask;
    $('lAsk').classList.toggle('hidden', !ask);
    if (ask) $('lAskText').textContent = ask.from + ' wants your ' + ask.give + ' spot. You would get ' + ask.get + '.';
    if (m.line) $('lLine').textContent = m.line;
    else if (m.held && L.kicked) $('lLine').textContent = '';
    L.kicked = !m.held;
    var bots = Math.max(0, m.players - m.people);
    $('lCount').textContent = m.people + (m.people === 1 ? ' person' : ' people') + ' · ' + bots + (bots === 1 ? ' bot' : ' bots') + ' on your team';
    $('lKick').disabled = !m.held;
    if (!m.held) $('lLine').textContent = 'Kickoff! Get ready for the huddle...';
  }

  $('lYes').addEventListener('click', function () { send({ t: 'swap', yes: true, aid: aid() }); $('lAsk').classList.add('hidden'); });
  $('lNo').addEventListener('click', function () { send({ t: 'swap', yes: false, aid: aid() }); $('lAsk').classList.add('hidden'); });
  $('lKick').addEventListener('click', function () { send({ t: 'kickoff', aid: aid() }); });
  $('lTeam').addEventListener('click', function () {
    hide();
    var pick = $('pickView');
    if (pick) document.querySelectorAll('.view').forEach(function (v) { v.classList.toggle('hidden', v !== pick); });
  });

  // every message from the game passes here first (app.js)
  function onMessage(msg) {
    if (!msg) return;
    if (msg.t === 'ui') {
      if (msg.layout === 'lobby') { setTimeout(show, 0); return; }   // after app.js has drawn its own screens
      if (L.shown) hide();
      // before kickoff the game belongs to the lobby: ask for it back (but let a team pick through)
      var now = Date.now();
      if (L.held && msg.layout !== 'pick' && now - L.askedAt > 1000) { L.askedAt = now; send({ t: 'lobbyme' }); }
    } else if (msg.t === 'lineup') {
      L.lineup = msg;
      L.held = !!msg.held;
      render();
    }
  }

  window.Lobby = { onMessage: onMessage };
})();
