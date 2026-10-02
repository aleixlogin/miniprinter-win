## Context

MiniPrinter (spec `x5h-windows-printing`) ya está en producción en el equipo del usuario: servicio de Windows con IPP, cola de trabajos, transporte RFCOMM, bandeja WPF y API de control en loopback. Este change añade siete capacidades (ver `proposal.md`). Hechos de partida relevantes:

- La receta `d1` admite modo texto (`BE 01`) y energía de texto (8000); hoy `JobQueue` siempre envía `IsText = false`.
- `Rasterizer.ChooseMode` ya clasifica cada página (umbral si < 8 % de medios tonos).
- `PrinterSession` ya consulta `A3` cada 30 s mientras está conectada y desconecta a los 60 s de inactividad.
- `PrintJobBuilder.BuildPage(..., endsPage)` y `BuildPageEnd` ya permiten trabajos sin fin de página intermedio.
- `IppHost` levanta su propio Kestrel (loopback o LAN); la API de control vive en otro listener solo en `127.0.0.1:8632`.
- El byte 2 de `A3` (batería) vale 39–40 y su unidad es desconocida.

## Goals / Non-Goals

**Goals:**
- Texto más nítido sin que el usuario tenga que elegir nada.
- Obtener los datos para descifrar el byte de batería y, una vez conocida la unidad, mostrar % y avisar.
- Tiras continuas para tiques y listas.
- Imprimir notas, portapapeles y archivos sin abrir otra aplicación.
- Integración con automatizaciones sin debilitar la API de control.
- Plantillas útiles y códigos legibles.
- PDF en todos los puntos de entrada.

**Non-Goals:**
- Editor visual de plantillas o diseñador WYSIWYG.
- Escala de grises real (el perfil `d1` es de 1 bit).
- Autenticación por usuarios, OAuth o HTTPS en la API de automatización (token de portador sobre la red local; HTTPS queda para el futuro).
- Formatos de documento más allá de PDF, PNG, JPEG, PWG Raster y TXT.
- Determinar automáticamente la unidad de la batería: la decide el usuario o un cambio posterior a la vista del registro.

## Decisions

### D1. Modo texto por página
`Rasterizer.Rasterize` devuelve también la clasificación (`RasterResult { Bitmap, IsText }`) usando `ChooseMode` sobre la página escalada. `JobQueue` decide el modo según el ajuste nuevo `PrintMode` (`Auto` | `Image` | `Text`) y construye cada página con `PrintOptions.IsText`, de modo que la receta envía `BE` y la energía/velocidad correspondientes. No se toca `PrintJobBuilder`: ya admite `IsText`.
- *Alternativa descartada*: decidir por trabajo. Un documento mixto (texto + foto) imprimiría la foto con energía de texto, más oscura y empastada.

### D2. Telemetría de batería
Nuevo `TelemetryLog` en el servicio que se suscribe a `PrinterSession.MessageReceived` y escribe cada `DeviceState` en `%ProgramData%\MiniPrinter\telemetry\status-YYYYMMDD.csv` (`timestamp,link,alarm,paperSensor,battery`). Rota por tamaño (5 MB) y borra archivos de más de 30 días al arrancar y a diario.
- **Muestreo**: `SessionOptions` gana `KeepAliveUntil` y un intervalo de sondeo variable; la API de control expone `POST /api/telemetry/sampling` (duración) y `DELETE` para detenerlo. Durante el muestreo no hay desconexión por inactividad y el sondeo pasa a 60 s.
- **Interpretación**: `BatteryInterpreter` en `MiniPrinter.Control` (compartido por servicio y bandeja) convierte el byte según `BatteryUnit` (`Unknown` | `Percent` | `Decivolts`). La curva de décimas de voltio es lineal por tramos sobre puntos típicos de una celda de litio (3,3 V = 0 %, 3,7 V ≈ 50 %, 4,2 V = 100 %).
- **Aviso**: el servicio calcula `BatteryPercent` y `LowBattery` (con histéresis de +5 %) y los publica en `StatusDto`; `JobQueue.GetPrinter` añade `other-warning` mientras `LowBattery`. La bandeja notifica al pasar `LowBattery` de falso a verdadero.
- **Exportación**: `GET /api/telemetry/export` devuelve todos los CSV concatenados; la bandeja lo guarda con un diálogo.

### D3. Papel continuo
- **Rollo**: se añade `om_x5h-48x1000mm_48x1000mm` a `IppPrinterDescription.Media`. El recorte de blancos existente elimina el papel sobrante; no hace falta lógica nueva.
- **Páginas continuas**: ajustes `ContinuousPages` (falso por defecto) y `PageGapMm` (4). Con el ajuste activo, `JobQueue` envía cada página con `endsPage: false` (la receta repite la cabecera, que es inocua y permite cambiar de modo texto/imagen entre páginas), inserta `PageGapMm × 8` filas en blanco entre páginas y envía `BuildPageEnd` una sola vez al final o al cancelar. El recorte de blancos por página (ya existente) une el contenido.
- *Alternativa descartada*: un tamaño IPP de largo variable (`media-size` con `y-dimension` como rango). El IPP Class Driver de Windows no lo ofrece de forma fiable en el diálogo de impresión.

### D4. Renderizado de texto en el servicio
Nuevo `TextRenderer` en `MiniPrinter.Imaging` con `SixLabors.ImageSharp.Drawing` (`SixLabors.Fonts`): fuentes del sistema (Segoe UI por defecto, Segoe UI Emoji como reserva), ajuste de línea por palabras a 384 px menos 2 mm de margen y alineación configurable. Se dibuja en `L8` y se convierte con umbral (sin tramado). Se usa en el servicio para la nota rápida, el portapapeles, los TXT, la API y las plantillas. **Todo el renderizado vive en el servicio**: la bandeja solo envía datos, así un único sitio garantiza el mismo resultado desde la bandeja, la API y la CLI.

### D5. Entradas comunes de impresión
Nuevo `PrintRequests` en el servicio con operaciones `PrintText`, `PrintFile` (bytes + nombre o tipo), `PrintQr` y `PrintTemplate`, que producen `MonoBitmap` o documentos y los encolan en `JobQueue` (`SubmitBitmap` / `SubmitDocument`). Lo usan:
- la API de control (bandeja): `POST /api/print/text|file|template`, en loopback con el token de control;
- la API de automatización (D6), en el listener IPP.

### D6. API de automatización
Se mapea `/api/v1/...` en el `WebApplication` de `IppHost`, de modo que hereda el alcance de red (loopback o LAN) sin abrir otro puerto. Un filtro de endpoint responde `404` si `AutomationApiEnabled` es falso y `401` si `Authorization: Bearer` no coincide (comparación en tiempo constante) con `%ProgramData%\MiniPrinter\automation.token`. El token se genera al activar la API y se regenera con `POST /api/automation/token` (API de control); la bandeja lo muestra con un botón Copiar. Límites: Kestrel `MaxRequestBodySize` de 16 MB para estas rutas y 20 000 caracteres de texto. Los endpoints de control nunca se mapean en este listener (test de regresión).

### D7. Plantillas
`TemplateRenderer` en `MiniPrinter.Imaging`:
- **QR**: `ZXing.QrCode.Internal.Encoder.encode` para obtener la matriz; tamaño de módulo = `floor(384 / (módulos + 8))`, que se rechaza si es < 4. Corrección de errores M.
- **Código de barras**: `ZXing.OneD.Code128Writer` / `EAN13Writer`; ancho de barra mínimo 2 puntos; validación del dígito de control EAN-13 con mensaje del valor esperado.
- **todo / label / sticker**: composición vertical con `TextRenderer` y primitivas de ImageSharp.Drawing (casillas de 6 × 6 mm con borde de 2 puntos). `sticker` pasa la imagen por el pipeline normal (ajuste al ancho + tramado).
- Definiciones declarativas (`TemplateDefinition`: nombre, campos obligatorios y opcionales) para que la bandeja genere los formularios, la API valide y la CLI muestre la ayuda.
- Vista previa: `POST /api/templates/{nombre}/preview` (API de control) devuelve PNG; la bandeja lo muestra en la pestaña Plantillas y guarda los últimos valores en `%LocalAppData%\MiniPrinter\templates.json`.

### D8. PDF
`PdfRasterizer` en `MiniPrinter.Imaging` con **Docnet.Core** (MIT, incluye `pdfium.dll` para win-x64): abre el documento desde bytes, renderiza página a página con factor de escala `203 / 72` en BGRA, convierte a gris y entrega `GrayImage` con `Dpi = 203`, de modo que el ajuste al contenido y el tramado existentes se aplican igual. Es un iterador perezoso: una página en memoria cada vez, y la cancelación entre páginas evita renderizar las restantes. Contraseña o archivo dañado → `InvalidPdfException` con mensaje claro → trabajo `aborted`.
- `ImageDecoder` detecta `%PDF-`. `IppPrinterDescription.DocumentFormats` añade `application/pdf`.
- **Selección de páginas**: `IPrintBackend.SubmitDocument` gana un parámetro `DocumentOptions { PageRanges }`; `IppPrinterService` lee `page-ranges` (1setOf `rangeOfInteger`) en Print-Job y Send-Document; la CLI añade `--pages 2-3,5`.
- *Alternativa descartada*: PDFtoImage (arrastra SkiaSharp, ~10 MB más de binarios nativos).

### D9. Impresión rápida en la bandeja
- **Portapapeles**: `Clipboard.ContainsImage` → PNG → `POST /api/print/file`; `ContainsText` → `POST /api/print/text`.
- **Soltar en el panel**: `AllowDrop` en `MainWindow`; cada archivo se envía con `POST /api/print/file`.
- **Enviar a**: `MiniPrinter.Tray.exe --print <archivos>` envía los archivos con `ControlClient` y termina sin abrir la bandeja (no choca con el mutex de instancia única). `install.ps1` crea `MiniPrinter.lnk` en la carpeta SendTo del usuario que instala, y la bandeja lo crea al arrancar si falta (otros usuarios).
- **Nota rápida**: `RegisterHotKey` (Win32) sobre una ventana oculta (`HwndSource`); ajuste `QuickNoteHotkey` (Ctrl+Alt+P por defecto, guardado en `%LocalAppData%`). Ventana con `TextBox`, selector de tamaño, vista previa (`POST /api/print/text/preview`) e impresión con Ctrl+Enter. Si `RegisterHotKey` falla, notificación y campo para elegir otro atajo.

### D10. Ajustes nuevos
`ServiceSettings` añade: `PrintMode`, `ContinuousPages`, `PageGapMm`, `BatteryUnit`, `LowBatteryPercent` (20), `AutomationApiEnabled` (falso), `TextFont` ("Segoe UI") y `TextSizePt` (10). Se mantiene la regla existente: `PUT /api/settings` no puede cambiar la impresora seleccionada.

## Risks / Trade-offs

- **[El modo texto empasta o quema imágenes mal clasificadas]** → clasificación conservadora (< 8 % de medios tonos) y ajuste manual; los tests cubren texto, foto y mixto.
- **[La unidad de la batería sigue sin estar clara tras el registro]** → la interpretación es configurable y por defecto se muestra el valor en bruto; el aviso solo se activa con una unidad elegida.
- **[ImageSharp.Drawing requiere una versión distinta de ImageSharp]** → fijar la versión de Drawing compatible con ImageSharp 3.1 (o actualizar ambas a la vez) y comprobarlo en la primera tarea.
- **[Docnet.Core no es seguro para hilos]** → el rasterizado de PDF se hace en el worker único de la cola; la CLI usa una sola instancia.
- **[Exponer una API en la red]** → desactivada por defecto, token propio regenerable, límites de tamaño y solo en modo red local; tests de que los endpoints de control no aparecen en el listener IPP.
- **[`RegisterHotKey` en conflicto con otros programas]** → aviso y atajo configurable.
- **[El tamaño de la publicación crece (PDFium, fuentes)]** → aceptable (+~6 MB); PDFium solo se carga al recibir un PDF.
- **[Repetir la cabecera entre páginas continuas]** → es inofensivo según la receta; se valida con la impresora real en las tareas.

## Migration Plan

- Ajustes nuevos con valores por defecto que mantienen el comportamiento actual (salvo el modo texto Automático, que solo afecta a páginas de texto).
- `settings.json` existente se carga sin cambios (propiedades nuevas con valor por defecto).
- La cola de Windows debe recrearse para que aparezcan `application/pdf` y el tamaño rollo: el despliegue se hace con `uninstall.ps1 -KeepConfig` + `install.ps1`, como hasta ahora.
- Rollback: reinstalar la versión anterior con los mismos scripts; los ajustes nuevos se ignoran.

## Verification (2026-10-02)

Desplegado con `uninstall.ps1 -KeepConfig` + `install.ps1` y probado por el usuario con la X5h-E07A: modo texto/imagen, tira continua, plantillas (QR y código de barras escaneables), PDF (CLI y móvil), nota rápida, portapapeles, Enviar a y API de automatización funcionan. 156 tests automáticos en verde (incluida la decodificación con ZXing de los QR/códigos generados y los PDF de prueba con selección de páginas). `pdfium.dll` presente en la publicación win-x64. La telemetría escribe `status-YYYYMMDD.csv` desde el primer arranque.

## Open Questions

- ¿Qué tipografía da mejor resultado a 203 dpi en la X5h (Segoe UI frente a Consolas o Arial)? Se decidirá con una impresión de comparación.
- ¿La X5h distingue algo más en modo texto aparte de la energía (velocidad, densidad)? Comparar impresiones en la tarea de verificación.
- Unidad del byte de batería: pendiente de un registro de descarga completa.
