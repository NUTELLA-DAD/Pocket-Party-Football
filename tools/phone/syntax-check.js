// Fails if any phone page script has a syntax error (one bad line kills the whole page: JOIN does nothing).
// Run: node tools/phone/syntax-check.js
const fs = require('fs'), path = require('path'), vm = require('vm');
const dir = path.join(__dirname, '..', '..', 'Assets', 'StreamingAssets', 'phone');
let bad = 0;
for (const f of fs.readdirSync(dir).filter(n => n.endsWith('.js'))) {
  try { new vm.Script(fs.readFileSync(path.join(dir, f), 'utf8'), { filename: f }); console.log('ok   ' + f); }
  catch (e) { bad++; console.log('FAIL ' + f + ': ' + e.message + '\n' + (e.stack || '').split('\n').slice(0, 3).join('\n')); }
}
process.exit(bad ? 1 : 0);
