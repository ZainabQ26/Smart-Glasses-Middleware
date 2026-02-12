const fs = require('fs');

try {
  const content = fs.readFileSync('results.json', 'utf8');
  let data;
  try {
      data = JSON.parse(content);
  } catch(e) { 
      console.log("Error parsing JSON:", e.message);
      process.exit(1);
  }

  const failures = [];

  function traverse(node) {
     if (node.specs) {
         node.specs.forEach(spec => {
             spec.tests.forEach(test => {
                 if (test.status === 'unexpected' || test.status === 'failed') {
                     failures.push({
                         file: spec.file,
                         title: spec.title,
                         error: test.results.find(r => r.status === 'failed' || r.status === 'timedOut')?.error?.message?.slice(0, 200) || 'Unknown error'
                     });
                 }
             });
         });
     }
     if (node.suites) {
         node.suites.forEach(traverse);
     }
  }

  traverse(data);

  // Group by file
  const grouped = {};
  failures.forEach(f => {
    if (!grouped[f.file]) grouped[f.file] = [];
    grouped[f.file].push(f);
  });

  console.log(`Found ${failures.length} failures in ${Object.keys(grouped).length} files.\n`);

  for (const file in grouped) {
    console.log(`## ${file}`);
    grouped[file].forEach(f => {
      console.log(`  - ${f.title}`);
      console.log(`    Error: ${f.error}`);
    });
    console.log('');
  }

} catch (err) {
  console.error(err);
}
