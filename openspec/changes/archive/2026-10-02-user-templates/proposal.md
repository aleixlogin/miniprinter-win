## Why

Las plantillas (`print-templates`) son código C#: cinco definiciones fijas, un `switch` por nombre y tamaños de letra o de casilla escritos a fuego. El usuario solo puede rellenar campos, nunca crear ni ajustar una plantilla, y cada plantilla nueva exige tocar el servicio, la CLI y la bandeja. Para recibos, etiquetas de cable, tiques o integraciones por API hace falta que las plantillas sean datos que el usuario (o un script) pueda definir.

## What Changes

- **Plantillas definidas por el usuario**: un layout declarativo en JSON (lista de bloques `text`, `qr`, `barcode`, `image`, `line`, `spacer`… con estilo y marcadores `{{campo}}`), guardado en `%ProgramData%\MiniPrinter\templates\*.json` (compartido entre el servicio, la bandeja y la CLI). Las ven por igual la API, la CLI y la bandeja.
- **Motor único**: las 5 plantillas actuales (`qr`, `barcode`, `todo`, `label`, `sticker`) pasan a ser JSON integrados que el motor interpreta; desaparece el `switch` por nombre. El resultado impreso de las plantillas actuales con sus valores por defecto no cambia.
- **Bloques nuevos**: línea separadora (continua o punteada), espaciador, dos columnas con relleno de puntos (`Producto ..... 3,50`), fecha y hora automáticas, numeración autoincremental persistente y logo fijo guardado junto a la plantilla.
- **Plantillas integradas nuevas**: lista de la compra con cantidades, Wi‑Fi QR, contacto (vCard), etiqueta de cable, recibo/tique, marcador de lectura y cuenta atrás.
- **Más ajuste en las plantillas actuales**: `label` (tamaño, alineación, borde), `qr` (nivel de corrección de errores, tamaño de módulo), `todo` (casilla o viñeta, tamaño, numeración) y `barcode` (Code 39, UPC‑A, altura configurable).
- **Bandeja**: vista previa en vivo mientras se escribe (con *debounce*), plantillas favoritas con nombre (valores guardados bajo un nombre) e impresión de N copias o de una etiqueta por línea de una lista CSV.
- **API y CLI**: se pueden listar, validar, crear/actualizar y borrar plantillas de usuario, y imprimir con copias o lotes (`copies`, `rows`).

## Capabilities

### New Capabilities
- `template-layouts`: formato JSON declarativo de plantillas (bloques, estilos, marcadores `{{campo}}`, validación), su carpeta de usuario, la carga/recarga y las plantillas integradas como JSON.
- `template-blocks`: catálogo de bloques de layout (texto, QR, código de barras, imagen, línea, espaciador, columnas, fecha/hora, contador, logo) y sus opciones de ajuste.
- `template-batch-printing`: copias y lotes (una impresión por fila de una lista CSV o de un array JSON) con un único trabajo en la cola por impresión.
- `template-management`: listado, alta, edición y borrado de plantillas de usuario y plantillas favoritas con nombre, desde API, CLI y bandeja.

### Modified Capabilities
- `print-templates`: el catálogo deja de estar cerrado en 5 plantillas (se amplía con las nuevas y con las del usuario); `qr`, `barcode`, `todo` y `label` ganan opciones; la bandeja añade vista previa en vivo y favoritas; la API y la CLI aceptan copias y lotes.
- `automation-api`: nuevos endpoints de gestión de plantillas y parámetros `copies`/`rows` en `POST /api/v1/print/template/{nombre}`.

## Impact

- **Código**: `MiniPrinter.Imaging` (nuevo modelo de layout, intérprete de bloques, catálogo de plantillas integradas y de usuario; `Templates.cs` deja de tener el `switch`), `MiniPrinter.Service` (`ControlApi`, `AutomationApi`, `PrintRequests`: gestión, copias y lotes; almacén del contador), `MiniPrinter.Control` (contratos y cliente), `MiniPrinter.Tray` (`TemplatesPanel`: vista previa en vivo, favoritas, CSV), `MiniPrinter.Cli` (`template`, `templates` y gestión; lee la misma carpeta de plantillas porque renderiza en local).
- **Dependencias**: ninguna nueva prevista; se reutilizan ImageSharp, `ZXing.Net` (Code 39 y UPC‑A ya incluidos) y `System.Text.Json`.
- **Datos**: `%ProgramData%\MiniPrinter\templates\` (plantillas y logos), `%LocalAppData%\MiniPrinter\favorites.json` (favoritas) y un contador persistente de numeración.
- **Seguridad**: las plantillas son datos, no código (sin scripting); se limitan el tamaño del JSON, el número de bloques, el tamaño de las imágenes incrustadas y el tamaño de los lotes.
- **Compatibilidad**: los nombres, los campos y la salida de las 5 plantillas actuales se mantienen, así que las llamadas existentes de API, CLI y bandeja siguen funcionando. Los valores recordados en `templates.json` siguen siendo válidos.
