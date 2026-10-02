## 1. Base y dependencias

- [x] 1.1 Añadir `SixLabors.ImageSharp.Drawing` (versión compatible con ImageSharp 3.1), `ZXing.Net` y `Docnet.Core` a `MiniPrinter.Imaging`; comprobar que compila y que la publicación win-x64 incluye `pdfium.dll`; actualizar `NOTICE`
- [x] 1.2 Añadir a `ServiceSettings` los ajustes `PrintMode`, `ContinuousPages`, `PageGapMm`, `BatteryUnit`, `LowBatteryPercent`, `AutomationApiEnabled`, `TextFont` y `TextSizePt`, con validación en `SettingsStore` y test de carga de un `settings.json` antiguo

## 2. Modo texto (text-mode-printing)

- [x] 2.1 Hacer que `Rasterizer.Rasterize` devuelva `RasterResult { Bitmap, IsText }` y adaptar los usos (cola, CLI)
- [x] 2.2 En `JobQueue`, elegir el modo por página según `PrintMode` y pasar `PrintOptions.IsText`; tests con el simulador para texto (`AF 40 1F`, `BE 01`), foto (`AF 88 13`, `BE 00`), documento mixto y modos forzados
- [x] 2.3 Añadir el selector "Modo de impresión" a la pestaña Ajustes de la bandeja

## 3. Telemetría de batería (battery-telemetry)

- [x] 3.1 Implementar `TelemetryLog` (CSV diario, rotación a 5 MB, limpieza de más de 30 días) suscrito a los `DeviceState` de la sesión, con tests de escritura y rotación
- [x] 3.2 Añadir `KeepAliveUntil` e intervalo de sondeo variable a `PrinterSession`, y los endpoints `POST/DELETE /api/telemetry/sampling`; test de que no desconecta durante el muestreo
- [x] 3.3 Implementar `BatteryInterpreter` (Unknown / Percent / Decivolts) con tests de la curva, y publicar `BatteryPercent` y `LowBattery` (con histéresis) en `StatusDto`; `other-warning` en IPP mientras haya batería baja
- [x] 3.4 Añadir `GET /api/telemetry/export`
- [x] 3.5 Bandeja: mostrar batería según la unidad, botón de muestreo (duración) y de exportación, selector de unidad y umbral, notificación de batería baja

## 4. Papel continuo (continuous-paper)

- [x] 4.1 Añadir el tamaño `om_x5h-48x1000mm_48x1000mm` a `IppPrinterDescription.Media` y test de atributos
- [x] 4.2 Implementar "Páginas continuas" en `JobQueue` (sin fin de página intermedio, hueco de `PageGapMm × 8` filas, fin de página único al final o al cancelar), con tests del simulador para 3 páginas, ajuste desactivado y cancelación a mitad
- [x] 4.3 Añadir el interruptor "Páginas continuas" y el hueco a la pestaña Ajustes

## 5. Renderizado de texto y entradas comunes

- [x] 5.1 Implementar `TextRenderer` (fuente y tamaño configurables, ajuste por palabras a 384 px, alineación, reserva de emoji, salida 1 bit) con tests de partición de líneas y caracteres en español
- [x] 5.2 Implementar `PrintRequests` en el servicio (`PrintText`, `PrintFile`, `PrintQr`, `PrintTemplate`) sobre `JobQueue`, incluido TXT como documento
- [x] 5.3 Exponer en la API de control `POST /api/print/text`, `/api/print/text/preview` y `/api/print/file`, y los métodos correspondientes en `ControlClient`, con tests de integración

## 6. PDF (pdf-printing)

- [x] 6.1 Implementar `PdfRasterizer` con Docnet.Core (página a página, 203 dpi, gris, `Dpi = 203`) e `InvalidPdfException` para PDF dañado o con contraseña; tests con PDF generados (una página, varias páginas, dañado)
- [x] 6.2 Detectar `%PDF-` en `ImageDecoder` y en la comprobación de formato de `JobQueue`; añadir `application/pdf` a `DocumentFormats`
- [x] 6.3 Añadir `DocumentOptions { PageRanges }` a `IPrintBackend.SubmitDocument`, leer `page-ranges` en `IppPrinterService` y aplicarlo en la cola; opción `--pages` en la CLI; tests
- [x] 6.4 `miniprinter print archivo.pdf` y test de extremo a extremo con el simulador (trabajo `completed`, PDF con contraseña `aborted` sin bloquear la cola)

## 7. Plantillas (print-templates)

- [x] 7.1 Implementar `TemplateDefinition` y `TemplateRenderer` con `qr`, `barcode` (Code 128 y EAN-13), `todo`, `label` y `sticker`; tests de módulo mínimo de QR, contenido demasiado largo, dígito de control EAN-13 y campos obligatorios
- [x] 7.2 Comprobar que el QR y el código de barras renderizados se decodifican con el lector de ZXing (test automático)
- [x] 7.3 Exponer `GET /api/templates`, `POST /api/templates/{nombre}/preview` y `POST /api/print/template/{nombre}` en la API de control
- [x] 7.4 Añadir `miniprinter template <nombre> --campo valor` a la CLI
- [x] 7.5 Pestaña "Plantillas" en la bandeja: formulario generado a partir de la definición, vista previa y recuerdo de los últimos valores

## 8. API de automatización (automation-api)

- [x] 8.1 Mapear `/api/v1/print/{text,image,qr,template/{nombre}}` y `/api/v1/jobs/{id}` en el listener de `IppHost` usando `PrintRequests`, con respuesta `202 { jobId }`
- [x] 8.2 Filtro de activación (`404` si está desactivada) y de token Bearer (`401`), token en `automation.token`, `POST /api/automation/token` para regenerarlo; límites de 16 MB y 20 000 caracteres; errores `400` claros
- [x] 8.3 Tests de integración: sin token, token incorrecto, API desactivada, regeneración, plantilla desconocida, y que ningún endpoint de control responde en el listener IPP
- [x] 8.4 Bandeja: activar/desactivar la API, mostrar, copiar y regenerar el token, y mostrar las URL
- [x] 8.5 README: ejemplos con curl, PowerShell, `rest_command` de Home Assistant y nodo HTTP de n8n

## 9. Impresión rápida en la bandeja (quick-print)

- [x] 9.1 "Imprimir portapapeles" en el menú del icono (imagen o texto; aviso si está vacío)
- [x] 9.2 Soltar archivos en la ventana del panel
- [x] 9.3 `MiniPrinter.Tray.exe --print <archivos>` sin abrir la bandeja; acceso directo en SendTo creado por `install.ps1` y por la bandeja si falta; eliminarlo en `uninstall.ps1`
- [x] 9.4 Ventana de nota rápida con `RegisterHotKey` (Ctrl+Alt+P configurable), vista previa e impresión con Ctrl+Enter; aviso si el atajo está ocupado

## 10. Verificación y despliegue

- [x] 10.1 Ejecutar todos los tests, publicar y reinstalar con `uninstall.ps1 -KeepConfig` + `install.ps1`
- [x] 10.2 Verificar con la impresora real: texto en modo texto frente a modo imagen, tira continua de 3 páginas, QR y código de barras escaneables con el móvil, PDF desde el móvil y desde la CLI, nota rápida, portapapeles y Enviar a
- [ ] 10.3 Iniciar un muestreo de batería largo y anotar en `design.md` lo que se sepa de la unidad
- [x] 10.4 Actualizar README (ajustes nuevos, impresión rápida, plantillas, PDF, API) y `design.md` con los resultados
