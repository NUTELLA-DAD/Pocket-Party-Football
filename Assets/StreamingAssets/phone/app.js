// Pocket Party Football phone controller. Served by the game itself; no internet needed.
(function () {
  'use strict';
  var STICK_HZ = 25, RETRY_MIN_MS = 500, RETRY_MAX_MS = 4000;
  var $ = function (id) { return document.getElementById(id); };
  function capture(el, id) { try { el.setPointerCapture(id); } catch (e) {} }
  var store = {
    get: function (k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
    set: function (k, v) { try { localStorage.setItem(k, v); } catch (e) {} }
  };

  // ---- join ----
  var m = location.pathname.match(/^\/join\/([A-Za-z]{4})/);
  var code = m ? m[1].toUpperCase() : (store.get('lastCode') || '');
  $('code').value = code;
  $('name').value = store.get('name') || '';
  var pid = store.get('pid');
  if (!pid) { pid = Math.random().toString(36).slice(2) + Date.now().toString(36); store.set('pid', pid); }

  document.addEventListener('contextmenu', function (e) { e.preventDefault(); });
  document.addEventListener('gesturestart', function (e) { e.preventDefault(); });

  var ws = null, wantJoin = false, retryMs = RETRY_MIN_MS, retryTimer = null;
  var aid = '', myTeam = -1, teams = [];

  $('joinBtn').addEventListener('click', function () {
    var n = $('name').value.trim().toUpperCase();
    var c = $('code').value.trim().toUpperCase();
    if (!n) { $('joinMsg').textContent = 'Type your name'; return; }
    if (c.length !== 4) { $('joinMsg').textContent = 'The room code has 4 letters (on the TV)'; return; }
    store.set('name', n); store.set('lastCode', c); code = c;
    wantJoin = true;
    $('joinMsg').textContent = 'Joining...';
    connect();
  });

  // ---- connection: a screen lock or Wi-Fi blip reconnects with the saved token, same player ----
  function connect() {
    if (ws && (ws.readyState === 0 || ws.readyState === 1)) return;
    ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws');
    ws.onopen = function () {
      retryMs = RETRY_MIN_MS;
      send({ t: 'join', code: code, name: store.get('name') || '', token: store.get('tok:' + code) || '', pid: pid });
    };
    ws.onmessage = function (ev) {
      var msg; try { msg = JSON.parse(ev.data); } catch (e) { return; }
      if (msg.t === 'joined') onJoined(msg);
      else if (msg.t === 'lobby') { teams = msg.teams || teams; drawTeams(); }
      else if (msg.t === 'picked') onPicked(msg);
      else if (msg.t === 'ui') onUi(msg);
      else if (msg.t === 'me') onMe(msg);
      else {
        if (msg.t === 'status') onStatus(msg);
        if (window.Huddle) window.Huddle.onMessage(msg);   // huddle, thumbs, job, status (huddle.js)
      }
      else if (msg.t === 'err') onErr(msg.m);
    };
    ws.onclose = function () {
      ws = null;
      if (!wantJoin) return;
      $('reconnect').classList.remove('hidden');
      clearTimeout(retryTimer);
      retryTimer = setTimeout(connect, retryMs);
      retryMs = Math.min(RETRY_MAX_MS, retryMs * 2);
    };
  }
  function send(o) { if (ws && ws.readyState === 1) ws.send(JSON.stringify(o)); }

  document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'visible' && wantJoin && !ws) { retryMs = RETRY_MIN_MS; connect(); }
    if (document.visibilityState === 'hidden') { releaseStick(); if (window.PPLayouts) window.PPLayouts.releaseAll(); }
  });

  function onErr(text) {
    $('joinMsg').textContent = text;
    show('joinView');
    wantJoin = false;
    if (ws) ws.close();
  }

  function onJoined(msg) {
    store.set('tok:' + code, msg.token);
    $('reconnect').classList.add('hidden');
    teams = msg.teams || [];
    myTeam = msg.team;
    $('pickTitle').textContent = 'Pick your side, ' + msg.name;
    $('meName').textContent = msg.name;
    drawTeams();
  }

  function onPicked(msg) {
    myTeam = msg.team;
    document.documentElement.style.setProperty('--team', msg.color);
    document.documentElement.style.setProperty('--team-2', msg.color2);
    $('meName').textContent = msg.name + ' · ' + msg.teamName;
    $('meNum').textContent = msg.pos + ' #' + msg.number;
    drawTeams();
  }

  // stamina bar and the one-line status: out of bounds beats tired beats the normal hint
  // the strip at the top: what the TV says, the score, the down (public: never a play)
  function onStatus(msg) {
    $('tvStrip').classList.remove('hidden');
    $('tvMsg').textContent = msg.tv || '';
    var s = msg.score || [];
    var names = teams.map(function (t) { return t.short; });
    $('tvScore').textContent = names.length >= 2 && s.length >= 2
      ? names[0] + ' ' + s[0] + ' · ' + names[1] + ' ' + s[1] + (msg.down ? '  ' + msg.down : '') : '';
  }

  function onMe(msg) {
    $('stam').style.width = Math.max(0, Math.min(100, msg.st)) + '%';
    $('stam').parentNode.classList.toggle('tired', !!msg.tired);
    var line = $('line');
    line.classList.toggle('warn', !!(msg.oob || msg.tired));
    line.textContent = msg.oob ? 'Out of bounds! Get back on the field.'
      : msg.tired ? 'Out of breath! Sprint comes back soon.'
      : baseLine;
  }

  // the buttons for this moment of a play: drawn by layouts.js into #btnBox; 'walk' shows the plain walking buttons
  var baseLine = 'Run with the stick. Hold SPRINT to go faster.';
  if (window.PPLayouts) window.PPLayouts.mount($('btnBox'), send, function () { return aid; });
  function playLayout(msg) {
    var r = window.PPLayouts ? window.PPLayouts.show(msg.layout, { receivers: msg.receivers || [], line: msg.line }) : null;
    var play = !!r;
    $('btnBox').classList.toggle('hidden', !play);
    $('walkBtns').classList.toggle('hidden', play);
    var pad = document.querySelector('#playView .pad');
    if (pad) pad.classList.toggle('no-stick', play && r.stick === false);
    baseLine = play ? r.line : 'Run with the stick. Hold SPRINT to go faster.';
    $('line').textContent = baseLine;
    show('playView');
  }

  function onUi(msg) {
    aid = msg.aid;
    if (window.PPLayouts && window.PPLayouts.names.indexOf(msg.layout) >= 0) { playLayout(msg); return; }
    if (msg.layout === 'walk') playLayout({ layout: 'none' });
    if (msg.layout === 'pick') show('pickView');
    else if (msg.layout === 'walk') show('playView');
    else if (msg.layout === 'huddle') show('huddleView');
  }

  function show(id) {
    ['joinView', 'pickView', 'huddleView', 'playView'].forEach(function (v) { $(v).classList.toggle('hidden', v !== id); });
    if (id !== 'playView') releaseStick();
  }

  // ---- team pick ----
  function drawTeams() {
    var box = $('teams');
    box.innerHTML = '';
    teams.forEach(function (t, i) {
      var b = document.createElement('button');
      b.className = 'team';
      b.style.background = t.color;
      b.setAttribute('aria-pressed', String(i === myTeam));
      b.setAttribute('aria-label', 'Join the ' + t.name);
      b.textContent = t.name;
      var s = document.createElement('div');
      s.className = 'sub';
      s.textContent = t.people + (t.people === 1 ? ' person' : ' people') + ' · ' + Math.max(0, t.players - t.people) + ' bots';
      b.appendChild(s);
      b.addEventListener('click', function () { send({ t: 'pick', team: i }); });
      box.appendChild(b);
    });
  }
  $('teamBtn').addEventListener('click', function () { show('pickView'); });

  // ---- stick: latest value at 25 Hz while held, one zero on release ----
  var stick = $('stick'), knob = $('knob'), stickId = null, sx = 0, sy = 0, stickTimer = null;
  function moveStick(e) {
    var b = stick.getBoundingClientRect(), r = b.width / 2;
    var x = (e.clientX - b.left - r) / r, y = (e.clientY - b.top - r) / r;
    var mag = Math.sqrt(x * x + y * y);
    if (mag > 1) { x /= mag; y /= mag; }
    sx = Math.round(x * 100) / 100; sy = Math.round(y * 100) / 100;
    knob.style.transform = 'translate(' + (x * r * 0.6) + 'px,' + (y * r * 0.6) + 'px)';
  }
  stick.addEventListener('pointerdown', function (e) {
    e.preventDefault();
    stickId = e.pointerId; capture(stick, stickId); moveStick(e);
    sendStick();
    clearInterval(stickTimer);
    stickTimer = setInterval(sendStick, 1000 / STICK_HZ);
  });
  stick.addEventListener('pointermove', function (e) { if (e.pointerId === stickId) moveStick(e); });
  function endStick(e) { if (e.pointerId === stickId) releaseStick(); }
  stick.addEventListener('pointerup', endStick);
  stick.addEventListener('pointercancel', endStick);
  function sendStick() { send({ t: 'stick', x: sx, y: sy }); }
  function releaseStick() {
    if (stickId === null && !stickTimer) return;
    stickId = null; clearInterval(stickTimer); stickTimer = null;
    sx = 0; sy = 0; knob.style.transform = '';
    sendStick();
  }

  // ---- buttons: every press carries the aid of the layout it was pressed on ----
  document.querySelectorAll('[data-b]').forEach(function (b) {
    var name = b.getAttribute('data-b');
    b.addEventListener('pointerdown', function (e) {
      e.preventDefault(); capture(b, e.pointerId); b.classList.add('on');
      send({ t: 'btn', b: name, d: 1, aid: aid });
    });
    function up() {
      if (!b.classList.contains('on')) return;
      b.classList.remove('on');
      send({ t: 'btn', b: name, d: 0, aid: aid });
    }
    b.addEventListener('pointerup', up); b.addEventListener('pointercancel', up);
  });

  // for huddle.js: send a message, and the aid of the screen on show (every press carries it)
  window.PP = { send: send, aid: function () { return aid; } };

  // a saved token for this room: rejoin straight away (reload, or the browser killed the tab)
  if (code && store.get('tok:' + code) && store.get('name')) { wantJoin = true; connect(); }
})();
