// Generates ESC/POS streams with node-thermal-printer for the MiniPrinter.Escpos tests.
//
// Usage (from an empty folder):
//     npm install node-thermal-printer
//     node <repo>/tools/generate_node_escpos_fixtures.js
//
// It writes tests/MiniPrinter.Escpos.Tests/Fixtures/node-<name>.bin: the bytes the library would send to
// an Epson-compatible printer (nothing is sent anywhere; the buffer is taken before execute()).
// The reference rasters are produced by the interpreter itself (UPDATE_GOLDEN=1 dotnet test tests/MiniPrinter.Escpos.Tests).
const fs = require("fs");
const path = require("path");
const { ThermalPrinter, PrinterTypes, CharacterSet } = require("node-thermal-printer");

const out = path.resolve(__dirname, "..", "tests", "MiniPrinter.Escpos.Tests", "Fixtures");
fs.mkdirSync(out, { recursive: true });

function printer() {
  return new ThermalPrinter({ type: PrinterTypes.EPSON, interface: "tcp://127.0.0.1:9", characterSet: CharacterSet.PC858_EURO, width: 32 });
}

const cases = {
  receipt() {
    const p = printer();
    p.alignCenter(); p.bold(true); p.setTextDoubleHeight(); p.println("TIENDA NODE"); p.setTextNormal(); p.bold(false);
    p.println("C/ Mayor 12");
    p.drawLine();
    p.alignLeft();
    p.leftRight("2 x Cafe con leche", "3,60");
    p.leftRight("1 x Tostada", "2,20");
    p.tableCustom([{ text: "Cant", align: "LEFT", width: 0.2 }, { text: "Producto", align: "CENTER", width: 0.5 }, { text: "Total", align: "RIGHT", width: 0.3 }]);
    p.drawLine();
    p.bold(true); p.println("TOTAL 5,80 €"); p.bold(false);
    p.println("Ñandú año canción");
    p.cut();
    return p;
  },
  styles() {
    const p = printer();
    p.bold(true); p.println("Negrita"); p.bold(false);
    p.underline(true); p.println("Subrayado"); p.underlineThick(true); p.println("Subrayado grueso"); p.underlineThick(false);
    p.invert(true); p.println(" Inverso "); p.invert(false);
    p.setTextDoubleHeight(); p.println("Doble alto"); p.setTextDoubleWidth(); p.println("Doble"); p.setTextQuadArea(); p.println("Cuadruple"); p.setTextNormal();
    p.setTextSize(2, 3); p.println("2x3"); p.setTextNormal();
    p.alignRight(); p.println("Derecha"); p.alignCenter(); p.println("Centro"); p.alignLeft();
    p.cut();
    return p;
  },
  codes() {
    const p = printer();
    p.alignCenter();
    p.printQR("https://example.com/node", { cellSize: 6, correction: "M", model: 2 });
    p.newLine();
    p.printBarcode("5901234123457", 67, { hriPos: 2, hriFont: 0, width: 2, height: 60 });
    p.newLine();
    p.code128("NODE-0042", { height: 60, text: 1 });
    p.newLine();
    p.pdf417("PDF417 desde node", { rowHeight: 3, width: 3, correction: 2, truncated: false, columns: 0 });
    p.cut();
    return p;
  },
  raw() {
    const p = printer();
    p.println("Antes");
    p.openCashDrawer();
    p.beep();
    p.println("Despues de cajon y pitido");
    p.partialCut();
    p.println("Segundo ticket");
    p.cut();
    return p;
  },
};

for (const [name, build] of Object.entries(cases)) {
  const buffer = build().getBuffer();
  fs.writeFileSync(path.join(out, `node-${name}.bin`), buffer);
  console.log(`node-${name}.bin: ${buffer.length} bytes`);
}
