const { Resvg } = require('@resvg/resvg-js');
const fs = require('fs');

const [, , svgPath, size, out] = process.argv;
const resvg = new Resvg(fs.readFileSync(svgPath), {
  fitTo: { mode: 'width', value: Number(size) },
  background: 'rgba(0,0,0,0)',
});
fs.writeFileSync(out, resvg.render().asPng());
