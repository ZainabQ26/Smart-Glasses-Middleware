const fs = require('fs');
const content = fs.readFileSync('src/SmartGlassesMiddleware/client/src/App.jsx', 'utf8');
console.log(content.includes('let speechChunks  = [];'));
