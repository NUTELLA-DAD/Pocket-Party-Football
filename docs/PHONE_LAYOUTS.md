# Phone layouts (groundwork for M1-f)

`Assets/StreamingAssets/phone/layouts.js` + `layouts.css` draw the right-hand buttons for each moment of a play, matching `docs/mockup/controller.html` and the controls table in `docs/PLAN.md`. They are not wired into `app.js` yet; M1-f does that.

## Wiring it in (M1-f)
1. `index.html`: add `<link rel="stylesheet" href="/layouts.css">` and `<script src="/layouts.js"></script>` before `app.js`, and give the right-hand button box an id (for example `btnBox`).
2. `app.js`: `PPLayouts.mount($('btnBox'), send, function () { return aid; })` once. On a `ui` message with a play layout, call `var r = PPLayouts.show(msg.layout, { receivers: msg.receivers, line: msg.line })`, then show the stamina bar when `r.stam` is true and toggle `no-stick` on `.pad` when `r.stick` is false. Call `PPLayouts.releaseAll()` when the page is hidden.
3. The game sends `layout` as one of: `wait`, `qbpre`, `qb`, `carrier`, `route`, `block`, `rush`, `lb`, `cover`, `kick`. For `qb` it also sends `receivers: [{ shape: "△", label: "WR #11", open: true }]` in the same order as the shapes over their heads on the TV.

## What the phone sends
| Press | Message |
| --- | --- |
| Any button down / up | `{ t: "btn", b: <action>, d: 1 or 0, aid }` |
| Receiver button | `b: "throw"` plus `tgt: "△"`; on release also `lob: true` when held 350 ms or longer (tap = bullet) |
| Kick pad (drag up, let go) | `{ t: "kick", power: 0..1, aim: -1..1, aid }` |

Actions: `find`, `hike`, `throw`, `scramble`, `sprint` (hold), `juke`, `spin`, `stiff`, `divein`, `call`, `catch`, `block` (hold), `swim`, `bull`, `tackle`, `swat`, `dive`, `jump`. A layout change while a button is held sends that button's release first, so the game never sees a stuck SPRINT or BLOCK.

## Checking it
`node tools/phone/layouts-test.js` (needs Node and Playwright) draws all 10 layouts in a phone-sized headless browser, checks tap = bullet, hold = lob, release on layout change and the kick message, and saves `.shots/phone-<layout>.png`. Open `tools/phone/layouts-harness.html` in a browser to click through them by hand.

## The look (navy pad, gold-rimmed gems)
Kenny's controller mockup: a navy pad, the POCKET PARTY logo, a gold-ringed MOVE stick, a player tag with a helmet in the team colours, a PAUSE button and glossy gem buttons. Each layout's buttons sit in a ring like a game pad: the first one in `LAYOUTS` is the big gem under the right thumb. The colour belongs to the action (SPRINT teal, TACKLE red, JUKE / SWIM / JUMP / CALL FOR IT yellow, SPIN / BULL RUSH / FIND ME purple, DIVE / DIVE TACKLE orange, SWAT blue), so it never changes between moments.
- PAUSE opens a menu on that phone only (FIND ME, CHANGE TEAM, BACK). The game keeps going on the TV; anything held is let go first.
- The called play stays on the pad for the whole play (name, your job, a small picture); it shrinks while the ball is live.
- The huddle is the play-select screen: the down and play clock on a gold bar, PASS / RUN / SPECIAL tabs (COVER / BLITZ / SPECIAL on defense, worked out from each play's name and runner), cards with the whole play drawn, then your job and a gold LOCK IN.
- Offline: system fonts only. SVG pictures in the CSS write their namespace as `http%3A//` so the hygiene check's no-internet rule stays meaningful.
