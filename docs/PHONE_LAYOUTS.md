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

Actions: `find`, `hike`, `throw`, `scramble`, `sprint` (hold), `juke`, `spin`, `stiff`, `call`, `catch`, `block` (hold), `swim`, `bull`, `tackle`, `swat`, `dive`, `jump`. A layout change while a button is held sends that button's release first, so the game never sees a stuck SPRINT or BLOCK.

## Checking it
`node tools/phone/layouts-test.js` (needs Node and Playwright) draws all 10 layouts in a phone-sized headless browser, checks tap = bullet, hold = lob, release on layout change and the kick message, and saves `.shots/phone-<layout>.png`. Open `tools/phone/layouts-harness.html` in a browser to click through them by hand.
