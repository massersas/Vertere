const fs = require('fs');
const path = require('path');

const backendUrl = process.env.BACKEND_URL || '';
const outDir = path.join(__dirname, '..', 'dist', 'schedulerpdv-front', 'browser');
const outPath = path.join(outDir, 'config.js');

if (!fs.existsSync(outDir)) {
  throw new Error(`Output directory not found: ${outDir}`);
}

const content = `window.__BACKEND_URL__ = ${JSON.stringify(backendUrl)};\n`;
fs.writeFileSync(outPath, content, 'utf-8');
