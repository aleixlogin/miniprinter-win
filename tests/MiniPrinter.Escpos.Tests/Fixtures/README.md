# Fixtures ESC/POS

Los `.bin` son flujos de bytes **reales** generados con `python-escpos` 3.1 (impresora `Dummy`, perfil por
defecto) mediante `tools/generate_escpos_fixtures.py`. Los `.png` son el resultado de referencia del
intérprete (revisado a ojo) y se regeneran con `UPDATE_GOLDEN=1 dotnet test tests/MiniPrinter.Escpos.Tests`.

| Fixture | Contenido |
|---|---|
| `receipt` | cabecera doble y negrita, líneas a dos columnas, total, QR nativo (`GS ( k`) y corte |
| `styles` | negrita, subrayado 1 y 2, doble ancho, doble alto, inverso, fuente B, alineaciones |
| `charset` | ñ, acentos, euro y cajas: python-escpos cambia solo de tabla (`ESC t` 0, 13 y 15) |
| `logo-raster` | imagen `GS v 0` 256 × 96 centrada |
| `logo-columns` | imagen `ESC *` de 24 puntos |
| `barcodes` | EAN-13, Code 128 (`{B…`) y Code 39 con `GS k` formato B |
| `qr-image` | QR dibujado por python-escpos y enviado como imagen |
| `two-tickets` | dos tiques en un flujo, separados por cortes (`two-tickets-0.png`, `-1.png`) |
