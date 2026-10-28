# Pocket Party Football - notes for Claude Code

A couch football party game in Unity: one TV runs the game, up to 22 phones join by QR code and act as controllers. 11 players per team (Arrows vs Grains), bots fill every spot no human took. Side-on camera, Wind Waker-style toon look.

Read first: `docs/PLAN.md` (the design), then `docs/BUILD_BRIEF.md` (the M0/M1 jobs, in order). The phone UI target is `docs/mockup/controller.html`. The visual target is `docs/art/ART_DIRECTION.md` and the concept pictures next to it (camera and HUD from `concepts/04-in-game-camera.png`).

This game reuses the design of the Airship Crew Unity remake: clone https://github.com/NUTELLA-DAD/QR-PHONE-GAME (public) and read `UNITY.md` (sections 4 and 5: architecture, phone server, protocol, gates) and `LESSONS.md`. Bring the designs and lessons, not the old JavaScript code.

## The owner (Kenny)
- Not a programmer. Explain changes in plain language and give exact, simple test steps (what to double-click, what to tap on the phone).
- Run all terminal commands yourself. Never ask Kenny to type commands.
- Ask before any download or install (Unity, packages, art packs) and before anything that costs money or extra usage.
- Keep a double-clickable way to launch the game working at all times.

## Architecture rules
- Unity 6 LTS, URP, Windows .exe first. Pin the exact Unity version in `tools/pinned-unity.txt` and `ProjectSettings/ProjectVersion.txt`; never upgrade without asking.
- `Assets/_Project/Scripts/Sim` is pure C# (asmdef with `noEngineReferences: true`): fixed 30 Hz step, seeded RNG, sorted collections, no `UnityEngine`, no `Time`, no `DateTime.Now`, no `System.Random`. The field is flat: players move in 2D, the ball has height.
- Unity only draws (`View`) and never writes to the sim. Unity physics only for cosmetic effects.
- The game hosts its own phone server (`Net`): plain HTTP + WebSocket over `TcpListener`, no internet needed, LAN address preferred like the old `server.js`. Write it as a reusable `PocketParty.Net` assembly.
- Phone page lives in `Assets/StreamingAssets/phone/`: no CDNs, no web fonts from the internet, `touch-action: none`, no `navigator.vibrate`, every button names its action, presses carry the `aid` guard, token rejoin after a screen lock.
- Secret play calling: a team's plays and calls are sent ONLY to that team's phones. The TV shows only "CALLED ✓". A test must prove the other team's sockets never receive the play id.
- Every tunable number in `Assets/_Project/Config/game.json`; plays in `Config/plays/*.json`. Every new rule behind `FEATURES.<name>` plus an `NO_<NAME>=1` env override.
- Scenes and objects are built from code and data at runtime (one near-empty Boot scene). No hand-placed prefabs, so no editor GUI work is needed.
- No wobble: no sine-of-time animation; stepped 8 fps keys for effects.
- No real team branding (no NFL/CFL names or logos). The teams are the Arrows (ARR) and the Grains (GRN). Each team's logo is ONE texture, `Resources/Logos/<SHORT>.png`; helmet and cheer-top emblems are painted by `tools/rebrand/`. The concept art still shows real logos: style reference only.

## How to work
- Do one job from `docs/BUILD_BRIEF.md` at a time, in the stated order.
- Run the hygiene check and EditMode tests before saying a job is done; save screenshots to `.shots/`.
- Commit after each working step with a clear message. Never run two Unity instances on the same project folder.
- Report: what landed, test results verbatim, deviations, and how Kenny tests it.
