// Fails if any phone page script has a syntax error (one bad line kills the whole page: JOIN does nothing).
// Run: node tools/phone/syntax-check.js
const fs = require('fs'), path = require('path'), vm = require('vm');
const dir = path.join(__dirname, '..', '..', 'Assets', 'StreamingAssets', 'phone');
let bad = 0;
for (const f of fs.readdirSync(dir).filter(n => n.endsWith('.js'))) {
  try { new vm.Script(fs.readFileSync(path.join(dir, f), 'utf8'), { filename: f }); console.log('ok   ' + f); }
  catch (e) { bad++; console.log('FAIL ' + f + ': ' + e.message + '\n' + (e.stack || '').split('\n').slice(0, 3).join('\n')); }
}
if (bad) process.exitCode = 1;

// The receiver shapes the QB throws to: five different symbols. (Once they were all "???" after an encoding slip,
// so every throw went to the same receiver.) Written as \u escapes in game.json so encoding can't eat them again.
const game = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'Assets', '_Project', 'Config', 'game.json'), 'utf8'));
const shapes = (game.play && game.play.shapes) || [];
if (shapes.length < 5 || new Set(shapes).size !== shapes.length || shapes.some(s => /\?/.test(s))) {
  console.log('FAIL game.json play.shapes must be 5 different symbols: ' + JSON.stringify(shapes)); process.exitCode = 1;
} else console.log('ok   game.json play.shapes ' + shapes.join(' '));
