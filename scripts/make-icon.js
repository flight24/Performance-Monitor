const sharp = require('sharp');
const toIco = require('to-ico');
const fs = require('fs');
const path = require('path');

const svg = fs.readFileSync(path.join(__dirname, 'icon.svg'));
const sizes = [16, 32, 48, 64, 128, 256];

async function convert() {
  const pngs = [];
  for (const size of sizes) {
    const buf = await sharp(svg).resize(size, size).png().toBuffer();
    pngs.push(buf);
  }
  const ico = await toIco(pngs);
  fs.writeFileSync(path.join(__dirname, 'icon.ico'), ico);
  console.log('icon.ico created');
}

convert().catch(err => { console.error(err); process.exit(1); });