## Why

MiniPrinter ya convierte la X5h-E07A en una impresora estándar de Windows (spec `x5h-windows-printing`). El uso real de estas impresoras (notas, tiques, listas, etiquetas) pide más: texto más nítido, saber cuánta batería queda, tiras continuas en vez de páginas, formas rápidas de imprimir sin abrir una aplicación, integración con automatizaciones, plantillas y PDF.

## What Changes

- **Modo texto**: las páginas que son mayoritariamente texto se envían en modo texto de la impresora (`BE 01`, energía de texto del perfil), con un ajuste Automático / Siempre imagen / Siempre texto.
- **Telemetría de batería**: registro de las lecturas de `A3` para averiguar la unidad del byte de batería; interpretación configurable, porcentaje en la bandeja y aviso de batería baja.
- **Papel continuo**: tamaños de rollo largo y opción de unir las páginas de un trabajo en una tira continua sin avance entre páginas.
- **Impresión rápida**: imprimir el portapapeles, soltar archivos en el panel o enviarlos desde el Explorador, y una nota rápida con atajo de teclado global.
- **API de automatización**: endpoints REST para imprimir texto, imágenes, QR y plantillas desde scripts, Home Assistant o n8n, con token propio y desactivada por defecto. Accesible desde la red solo en modo red local.
- **Plantillas**: QR, código de barras, lista de tareas, etiqueta y pegatina, generadas por el servicio a 384 px y usables desde la bandeja y desde la API.
- **PDF**: aceptar `application/pdf` en IPP, en la CLI y en la API, rasterizando con PDFium.

## Capabilities

### New Capabilities
- `text-mode-printing`: elección por página entre modo imagen y modo texto de la impresora, y su ajuste.
- `battery-telemetry`: registro de lecturas de estado, interpretación del byte de batería, visualización y aviso de batería baja.
- `continuous-paper`: tamaños de rollo largo y unión de páginas en una tira continua.
- `quick-print`: impresión del portapapeles, de archivos soltados o enviados y nota rápida con atajo global.
- `automation-api`: API REST de impresión para automatizaciones, con autenticación por token.
- `print-templates`: plantillas de QR, código de barras, lista de tareas, etiqueta y pegatina.
- `pdf-printing`: aceptación y rasterizado de documentos PDF.

### Modified Capabilities
<!-- Ninguna: las nuevas capacidades amplían x5h-windows-printing sin cambiar sus requisitos. La API de control sigue sin exponerse a la red; la API de automatización es un endpoint de impresión distinto. -->

## Impact

- **Código**: `MiniPrinter.Protocol` (modo texto en la receta), `MiniPrinter.Imaging` (renderizado de texto, plantillas, PDF), `MiniPrinter.Ipp` (formatos y medios nuevos), `MiniPrinter.Service` (cola, telemetría, API de automatización), `MiniPrinter.Tray` (impresión rápida, plantillas, batería), `MiniPrinter.Cli` (PDF, plantillas).
- **Dependencias nuevas**: renderizado de PDF con PDFium (p. ej. `PDFtoImage`, binarios BSD/Apache), generación de QR y códigos de barras (p. ej. `ZXing.Net`, Apache-2.0) y renderizado de texto (`SixLabors.ImageSharp.Drawing` + `SixLabors.Fonts`).
- **Seguridad**: la API de automatización amplía la superficie expuesta en modo red local; usa un token distinto del de la API de control, va desactivada por defecto y limita el tamaño de las peticiones.
- **Datos**: registro de telemetría en `%ProgramData%\MiniPrinter\telemetry\` con rotación.
- **Compatibilidad**: los trabajos existentes siguen funcionando igual con los valores por defecto (modo texto Automático solo afecta a páginas clasificadas como texto; páginas continuas desactivadas por defecto).
