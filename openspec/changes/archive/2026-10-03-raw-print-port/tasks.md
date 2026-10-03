## 1. Biblioteca ESC/POS (escpos-emulation)

- [x] 1.1 Crear el proyecto `MiniPrinter.Escpos` (referencias a `Protocol` e `Imaging`, `System.Text.Encoding.CodePages`) y su proyecto de tests; incluirlos en la solución
- [x] 1.2 Analizador de órdenes: flujo de bytes → eventos (texto, estilo, avance, corte, imagen, QR, código de barras, consulta de estado, desconocida), con la tabla de longitudes de parámetros y el descarte seguro de las desconocidas
- [x] 1.3 Juegos de caracteres: CP437, CP850, CP858 y CP1252 con `ESC t`, detección de UTF-8, y `?` para lo que la fuente no tiene
- [x] 1.4 Render por celdas: rejilla de 12×24 (fuente A, 32 columnas) y 9×17 (fuente B, 42), fuente monoespaciada (Consolas con reservas), negrita, subrayado, tamaño ×1–×8, inverso, alineación y cierre de línea; tests de columnas alineadas y de cada estilo
- [x] 1.5 Avance (`ESC d`, `ESC J`), interlineado y fin de tique con `GS V` (avance final, recorte del blanco sobrante); un tique sin contenido no genera página
- [x] 1.6 Imágenes `GS v 0` (normal y doble) y `ESC *` (8 y 24 puntos), con recorte a 384 y alineación
- [x] 1.7 QR (`GS ( k`, funciones 65, 67, 69, 80, 81, 82) y códigos de barras (`GS k`, variantes 0–6 y 65–73; UPC-A, EAN-13, Code 39, Code 128) con `GS H/h/w/f`, reutilizando los renderizadores de plantillas; tests de lectura con ZXing
- [x] 1.8 Respuestas de estado (`DLE EOT 1–4`, `GS r 1`) calculadas de un estado abstracto (en línea, tapa, sin papel, sin dato)
- [x] 1.9 Fixtures reales: generar con `python-escpos` varios tiques (cabecera y total, columnas, estilos, QR y barras, logo) como `.bin` más su PNG de referencia, con un script `tools/generate_escpos_fixtures.py` documentado, y un test de coincidencia píxel a píxel

## 2. Listener y detección (raw-print-port)

- [x] 2.1 Añadir `RawPortEnabled` (falso) y `RawPort` (9100) a `ServiceSettings`, con validación (1024–65535) y test de que un `settings.json` antiguo no cambia nada
- [x] 2.2 `RawPortHost` (`BackgroundService` con `TcpListener`): arranca solo si está activo, alcance de `NetworkMode`, reinicio al cambiar ajustes sin trabajos en curso, estado y error de puerto ocupado
- [x] 2.3 Límites: 4 conexiones simultáneas, 16 MB por conexión, 30 s de inactividad; tests con clientes TCP reales contra el listener
- [x] 2.4 Detección del contenido (PNG, JPEG, PDF, PWG, ESC/POS, texto plano, rechazo) por los primeros bytes, con tests de cada tipo y de un binario desconocido
- [x] 2.5 Documentos en bruto: `CreateJob` + `SubmitDocument` con origen `raw@<ip>` y fin por cierre de conexión; texto plano con `TextRenderer`
- [x] 2.6 ESC/POS: pasar el flujo por el intérprete, encolar con `SubmitBitmaps` un trabajo por tique, fin por corte, cierre o 2 s sin datos; las respuestas de estado se escriben en la conexión
- [x] 2.7 Firewall: generalizar `FirewallRule` (nombre y puerto) y crear la regla `MiniPrinter RAW` en modo red local, quitándola al desactivar o cambiar de modo
- [x] 2.8 Estado del puerto en `StatusDto` (activo, puerto y error) y en el cliente de control

## 3. Bandeja (raw-print-settings)

- [x] 3.1 Apartado "Impresión directa (puerto 9100)" en *Ajustes*: casilla, puerto, estado y nota sobre la falta de autenticación
- [x] 3.2 Validación del puerto y aviso al activarlo con el modo "Toda la red local" (continuar o cancelar)
- [x] 3.3 Mostrar el estado (escuchando en…, error) y refrescarlo con el del servicio

## 4. Comprobación real y cierre

- [x] 4.1 Probar con la impresora real: `nc`/PowerShell (`TcpClient`) con una imagen y un texto, y `python-escpos` apuntando al puerto con un tique con QR y corte
  - Hecho con el servicio instalado y la X5h física: `python-escpos` (tickets, estilos, tablas de caracteres, barras, QR, imágenes, ráfagas y conexiones simultáneas), PowerShell y sockets (PNG, JPEG, PDF, texto), consultas de estado, cancelar un trabajo, retención sin papel, códigos 2D nativos y CJK.
- [x] 4.2 Comprobar con otro equipo de la red en modo red local (firewall y alcance) y que en "Solo este PC" no es accesible
  - Comprobado desde Android en la red local: en «Solo este PC» el puerto no es accesible y en «Toda la red local» sí.
- [x] 4.3 Documentar en el `README` (sección nueva: puerto 9100, órdenes soportadas, seguridad y ejemplos con `nc`, PowerShell y `python-escpos`), marcar los puntos 1 y 2 de `docs/ideas/impresion-directa.md` como hechos o pendientes, y añadir el historial de cambios
- [x] 4.4 Ejecutar todas las pruebas y resolver las preguntas abiertas del diseño reflejándolas en las specs

## 5. Caracteres CJK, coreano y emoji (escpos-emulation)

- [x] 5.1 Celdas anchas: detectar los caracteres de ancho completo, dibujarlos en dos celdas con la fuente del sistema según la región (japonesa, china, coreana), con `?` de una celda si ninguna fuente los tiene, y respetar `GS !`, negrita, inverso y subrayado
- [x] 5.2 Modo kanji (`FS &`, `FS .`, `FS C`), tabla `ESC t 1` como CP932 y página CJK del intérprete configurable (932, 936, 950, 949)
- [x] 5.3 Símbolos y emoji con Segoe UI Symbol/Emoji, normalización NFC de cada tramo y caracteres de ancho cero sin celda
- [x] 5.4 Tests (japonés, chino, coreano, Shift-JIS en modo kanji, katakana por `ESC t 1`, emoji, combinados, ancho cero, líneas que mezclan celdas anchas y estrechas) y fixtures con los bytes de cada caso
- [x] 5.5 Prueba física, README (tabla de órdenes y límites de los caracteres) y volver a ejecutar todas las pruebas

## 6. Árabe y tailandés (escpos-emulation)

- [x] 6.1 Segmentar el texto en tramos de árabe y tailandés (con los espacios y signos entre sus palabras) y dibujar cada tramo como un bloque proporcional con el motor de formas del sistema, alineado a la rejilla de celdas, con partición por palabras y alineación a la derecha en árabe
- [x] 6.2 Dejar el orden visual por celdas solo para el hebreo; tests (árabe unido y ligadura lam-alef, tailandés con marcas, tramo mezclado con latín, partición, tamaño y negrita) y fixture
- [x] 6.3 Prueba física, README y volver a ejecutar todas las pruebas
