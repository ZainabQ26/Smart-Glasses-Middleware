const fs = require('fs');
(async () => {
  try {
    const fd = new FormData();
    fd.append('file', new Blob([Buffer.from([0x25,0x50,0x44,0x46])]), 'sample.pdf');
    fd.append('label', 'e2e-pdf');
    const res = await fetch('http://localhost:5000/api/admin/files', { method: 'POST', body: fd });
    const text = await res.text();
    console.log('status:', res.status);
    console.log(text);
  } catch (e) {
    console.error(e);
    process.exit(1);
  }
})();
