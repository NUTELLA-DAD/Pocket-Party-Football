// Pocket Party Football: audibles and hot routes at the line (WS-13). Only this team's phones get the 'audible'
// message; the TV and the other team never see an audible or a hot route.
//   The caller: AUDIBLE opens the playbook; tap a play to change the call (once a down).
//   A receiver: HOT buttons swap his route; tap the lit one again to go back to the called route.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var A = { open: false, caller: false, left: 0, plays: [], hot: [], mine: '', mineText: '', routes: [] };

  // the bar sits in the name plate, so it is on screen in every layout before the snap
  var bar = document.createElement('div');
  bar.id = 'audBar';
  bar.className = 'audbar hidden';
  var plate = $('me');
  if (plate) plate.insertBefore(bar, $('meNum'));

  var sheet = document.createElement('div');
  sheet.id = 'audSheet';
  sheet.className = 'audsheet hidden';
  sheet.setAttribute('role', 'dialog');
  sheet.setAttribute('aria-label', 'Audible: change the play');
  document.body.appendChild(sheet);

  function press(o) {
    if (!window.PP) return;
    o.aid = window.PP.aid();
    window.PP.send(o);
  }

  function button(text, label, cls, onTap) {
    var b = document.createElement('button');
    b.className = cls;
    b.textContent = text;
    b.setAttribute('aria-label', label);
    b.addEventListener('click', onTap);
    return b;
  }

  function draw() {
    bar.innerHTML = '';
    var any = false;
    if (A.open && A.caller && A.left > 0 && A.plays.length) {
      bar.appendChild(button('AUDIBLE', 'Audible: change the play at the line', 'aud', openSheet));
      any = true;
    }
    if (A.open) A.hot.forEach(function (r) {
      var on = r.id === A.mine;
      var b = button(r.name.toUpperCase(), on ? 'Hot route ' + r.name + ' is on: tap to run the called route again' : 'Hot route: run ' + r.name + (r.text ? '. ' + r.text : ''),
        'aud hot', function () { press({ t: 'hot', id: on ? '' : r.id }); });
      b.setAttribute('aria-pressed', String(on));
      bar.appendChild(b);
      any = true;
    });
    if (A.open && !A.hot.length && A.routes.length) {
      var s = document.createElement('span');
      s.className = 'audnote';
      s.textContent = 'HOT: ' + A.routes.map(function (r) { return r.pos + ' #' + r.number + ' ' + r.name; }).join(', ');
      bar.appendChild(s);
      any = true;
    }
    bar.classList.toggle('hidden', !any);
    if (A.open && A.mineText) { var jt = $('jobText'); if (jt) jt.textContent = A.mineText; }
    if (!(A.open && A.caller && A.left > 0)) closeSheet();
  }

  function openSheet() {
    sheet.innerHTML = '';
    var h = document.createElement('h2');
    h.textContent = 'AUDIBLE · only your team sees this';
    sheet.appendChild(h);
    var list = document.createElement('div');
    list.className = 'audplays';
    A.plays.forEach(function (p) {
      var b = button('', 'Change the play to ' + p.name + (p.desc ? ': ' + p.desc : ''), 'play', function () {
        press({ t: 'audible', id: p.id });
        closeSheet();
      });
      var t = document.createElement('b'); t.textContent = p.name; b.appendChild(t);
      var d = document.createElement('small'); d.textContent = p.desc || ''; b.appendChild(d);
      list.appendChild(b);
    });
    sheet.appendChild(list);
    sheet.appendChild(button('KEEP THE CALL', 'Keep the play you called', 'big', closeSheet));
    sheet.classList.remove('hidden');
  }

  function closeSheet() { sheet.classList.add('hidden'); }

  function onAudible(msg) {
    A.open = !!msg.open; A.caller = !!msg.caller; A.left = msg.left || 0;
    A.plays = msg.plays || []; A.hot = msg.hot || []; A.mine = msg.mine || ''; A.mineText = msg.mineText || '';
    A.routes = msg.routes || [];
    draw();
  }

  window.Audible = {
    onMessage: function (msg) {
      if (msg.t === 'audible') onAudible(msg);
      else if (msg.t === 'huddle') { A.open = false; draw(); }
      else if (msg.t === 'job' && A.open && A.mineText) draw();   // (keep the hot route's line over the new job text)
    }
  };
})();
