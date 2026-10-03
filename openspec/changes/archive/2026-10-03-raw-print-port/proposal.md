## Why

Hoy solo se puede imprimir en la X5h pasando por el driver IPP de Windows, la API REST o la CLI. Casi todo el software de tiques (TPV, `python-escpos`, `node-thermal-printer`, RawBT en Android, integraciones de Home Assistant) y cualquier script (`nc`) esperan una impresora de red en el puerto TCP 9100 que entienda **ESC/POS**. Con un puerto 9100 se imprimiría desde ese software sin driver, sin spooler y sin token.

## What Changes

- **Puerto TCP 9100 (RAW/JetDirect)** en el servicio, **desactivado por defecto**, con alcance igual que IPP (solo este PC o red local) y regla de firewall solo para redes privadas.
- **Detección del contenido** por sus primeros bytes:
  - ESC/POS (empieza por `ESC` o `GS`, o contiene órdenes de control),
  - PNG, JPEG, PDF y PWG Raster (se imprimen como un documento normal),
  - texto plano (UTF-8 sin órdenes de control).
- **Intérprete ESC/POS** (subconjunto práctico) que dibuja el tique a 384 puntos con celdas de caracteres fijas (32 columnas, 42 en fuente B) y lo imprime como cualquier otro trabajo:
  - texto con juegos de caracteres (CP437, CP858, CP1252) y UTF-8, negrita, subrayado, tamaño doble/múltiple, inverso y alineación;
  - avance de papel y **corte** (que marca el fin de un tique);
  - imágenes raster (`GS v 0`, `ESC *`);
  - códigos QR (`GS ( k`) y de barras (`GS k`: Code 128, Code 39, EAN-13, UPC-A);
  - respuestas de estado (`DLE EOT`, `GS r`) a partir del estado de la impresora.
- **Caracteres CJK, coreano y emoji** (añadido tras las pruebas exóticas): los caracteres de ancho completo ocupan **dos celdas** (24 puntos) con una fuente del sistema (japonesa, china o coreana según el texto), el **modo kanji** (`FS &`/`FS .`, `FS C`) y la tabla CP932 (`ESC t 1`) decodifican Shift-JIS (o GBK/Big5/EUC-KR configurables), los símbolos y emoji caen a Segoe UI Symbol, los caracteres combinados se componen (NFC) y los de ancho cero no ocupan celda.
- **Fin de trabajo** por corte, por cierre de la conexión o por 2 s sin datos.
- **Límites y seguridad**: 16 MB por conexión, 4 conexiones simultáneas, tiempo máximo de lectura; cada trabajo aparece en la cola con el origen (`raw@<ip>`), así que se ve quién imprime.
- **Ajustes** en la bandeja: activar el puerto, elegir el número y ver su estado y el aviso de que el puerto no tiene autenticación.

## Capabilities

### New Capabilities
- `raw-print-port`: el listener TCP 9100, la detección del contenido, el fin de trabajo, los límites y el alcance de red.
- `escpos-emulation`: el intérprete ESC/POS y su render.
- `raw-print-settings`: el apartado de *Ajustes* que lo activa y muestra su estado.

### Modified Capabilities
<!-- Ninguna: el puerto 9100 es una entrada nueva a la misma cola; los requisitos existentes no cambian. -->

## Impact

- **Código**:
  - `MiniPrinter.Control`: `ServiceSettings` (`RawPortEnabled`, `RawPort`).
  - nuevo `MiniPrinter.Escpos` (intérprete y render, sin dependencias de Windows, probable de probar con fixtures).
  - `MiniPrinter.Service`: `RawPortHost` (listener, detección, límites, estado) y `FirewallRule` (segunda regla para el puerto).
  - `MiniPrinter.Tray`: sección de *Ajustes*.
- **Dependencias**: ninguna nueva (ZXing y ImageSharp ya están; las páginas de códigos salen de `System.Text.Encoding.CodePages`).
- **Seguridad**: el puerto 9100 no tiene autenticación por definición. Por eso va desactivado por defecto, escucha solo en `127.0.0.1` salvo en modo red local, abre el firewall solo en redes privadas, limita tamaño, conexiones y tiempos, y muestra el origen de cada trabajo.
- **Compatibilidad**: sin tocar los ajustes no cambia nada.
- **Fuera de alcance**: el modo "raw para desarrolladores" (tramas del protocolo ya codificadas), autenticación en el puerto 9100, escrituras de derecha a izquierda y con formas contextuales (árabe, hebreo, tailandés), los comandos de gaveta o de corte físico y las tablas CJK de las impresoras reales (se usan fuentes del sistema).
