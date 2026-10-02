## Context

Hoy `TemplateRenderer` (`MiniPrinter.Imaging/Templates.cs`) contiene a la vez las definiciones (`Definitions`, con sus campos) y el dibujo (`Qr`, `Barcode`, `Todo`, `Label`, `Sticker`), unidos por un `switch` por nombre. Los estilos están fijos (26 pt en `label`, casilla de 40 px en `todo`, barras de 96 px en `barcode`, nivel de corrección `M` en `qr`).

Hechos de partida:
- La bandeja construye el formulario a partir de `GET /api/templates` (`TemplateDto`), así que añadir plantillas no exige tocar su código; sí lo exigen la vista previa en vivo, las favoritas y los lotes.
- El servicio expone `POST /api/templates/{name}/preview` (PNG) y `POST /api/print/template/{name}` (control, loopback) y `POST /api/v1/print/template/{name}` (automatización, con token).
- **La CLI renderiza en local** (`TemplateRenderer.Render` con su propia conexión Bluetooth). Por eso el catálogo de plantillas de usuario debe poder leerlo cualquier proceso desde disco, no solo el servicio.
- `PrintRequests.PrintTemplate` imprime las plantillas de línea en modo texto y `sticker` (`IsImage`) en modo imagen.
- `TextRenderer.Render(text, TextStyle, width)` ya admite tamaño, negrita y alineación.
- Los valores recordados de la bandeja viven en `%LocalAppData%\MiniPrinter\templates.json`.

## Goals / Non-Goals

**Goals:**
- Que una plantilla sea un dato (JSON) y no código, con el mismo motor para las integradas y las del usuario.
- Que crear una plantilla nueva (recibo, etiqueta de cable…) no requiera recompilar.
- Misma salida para las 5 plantillas actuales con sus valores por defecto.
- Mismo resultado desde bandeja, API y CLI.
- Lotes y copias sin saturar la cola.

**Non-Goals:**
- Editor visual WYSIWYG en la bandeja: las plantillas de usuario se editan como JSON (con validación y vista previa); un editor visual queda para otro change.
- Scripting o lógica en las plantillas (bucles, condicionales, expresiones): solo sustitución de campos y unos pocos valores automáticos.
- Fuentes personalizadas o varias familias de letra por plantilla.
- Sincronización entre equipos o galería compartida de plantillas.

## Decisions

### D1. Layout declarativo en JSON, no scripting
Una plantilla es un documento JSON con `name`, `title`, `description`, `fields[]` (mismo modelo que `TemplateField`), `mode` (`text` | `image`) y `blocks[]`. Cada bloque tiene `type` y propiedades propias; los textos admiten marcadores `{{campo}}`.

```
 plantilla.json ──► TemplateCatalog ──► LayoutEngine ──► MonoBitmap
  (integrada o         (valida, indexa)   (bloque a bloque,
   de usuario)                             Stack vertical)
```

- *Alternativa descartada*: Liquid/Scriban. Más potente, pero ejecuta lógica no acotada, es difícil de previsualizar y de validar y amplía la superficie de ataque de la API.
- *Alternativa descartada*: mantener plantillas en C# con más parámetros. No resuelve que el usuario no pueda crear las suyas.

### D2. Un bloque = una clase pequeña con un contrato común
`ILayoutBlock.Render(context) → MonoBitmap`, donde `context` lleva el ancho, los valores de los campos, el reloj y el contador. El motor renderiza cada bloque y los une con `Stack` (que ya existe). Los bloques actuales (`Qr`, `Barcode`, `Todo`, `Label`, `Sticker`) se reescriben sobre `TextRenderer`, `Caption` y `Stack` existentes, no desde cero. Un registro `type → factoría` evita el `switch`.

Bloques: `text`, `qr`, `barcode`, `image`, `line`, `spacer`, `columns`, `list` (una fila por línea de un campo multilínea, con casilla, viñeta o número), y los valores automáticos (`{{now:...}}`, `{{counter}}`) que son marcadores y no bloques.

### D3. Las integradas son JSON incrustados como recursos
Las plantillas integradas (`qr`, `barcode`, `todo`, `label`, `sticker` y las nuevas: `shopping`, `wifi`, `contact`, `cable`, `receipt`, `bookmark`, `countdown`) se guardan como recursos incrustados en `MiniPrinter.Imaging` y se cargan con el mismo parser que las de usuario. Así el motor se prueba con las integradas y el comportamiento de las 5 actuales se verifica comparando el raster antes y después (tests de regresión con los valores por defecto).

Las plantillas con lógica de validación específica (EAN‑13 y su dígito de control, límites de QR) la mantienen en los bloques `qr` y `barcode`, no en la plantilla.

### D4. Catálogo y resolución
`TemplateCatalog` en `MiniPrinter.Imaging` combina integradas y de usuario (`%LocalAppData%\MiniPrinter\templates\*.json`, o la carpeta indicada por parámetro para los tests). Reglas:
- Una plantilla de usuario con el nombre de una integrada la **sustituye** (permite personalizar `label` sin tocar el instalador); borrarla restaura la integrada.
- Cada plantilla se valida al cargar; una inválida se omite y se registra el error, sin impedir cargar las demás.
- El catálogo se relee al cambiar la carpeta (`FileSystemWatcher` con *debounce*) y a petición; la CLI lo lee en cada ejecución.

`%LocalAppData%` es del usuario que ejecuta la bandeja y la CLI, pero el **servicio corre como servicio de Windows** (otra cuenta). Por eso las plantillas de usuario se guardan en `%ProgramData%\MiniPrinter\templates\` (legible por el servicio, la bandeja y la CLI) y no en `%LocalAppData%`. Se ajusta la ruta indicada en la propuesta por esta razón; se mantiene `%LocalAppData%\MiniPrinter\templates.json` solo para los valores recordados de la bandeja.

### D5. Gestión por el servicio
La gestión (alta, edición, borrado) pasa siempre por el servicio, que valida el JSON y escribe en `%ProgramData%`. Rutas en la API de control (loopback, token de control): `GET /api/templates` (existente), `GET /api/templates/{name}` (JSON completo), `PUT /api/templates/{name}`, `DELETE /api/templates/{name}`, `POST /api/templates/validate`. La API de automatización añade las mismas con el token de automatización. La CLI dispone de `template list|show|add|remove` y, como renderiza en local, también puede validar sin servicio.

### D6. Contador y fecha
- `{{now}}` y `{{now:formato}}` (formato .NET, p. ej. `dd/MM/yyyy HH:mm`) usan el reloj del equipo.
- `{{counter}}` y `{{counter:nombre}}` leen y **incrementan** un contador persistente (`%ProgramData%\MiniPrinter\counters.json`, escritura atómica). El contador solo avanza al **imprimir**, no en la vista previa (que muestra el valor siguiente sin consumirlo). El almacén se protege con un `SemaphoreSlim` en el servicio; la CLI en modo local usa un archivo con bloqueo.
- *Alternativa descartada*: derivar el número del identificador del trabajo. Se reinicia con el servicio y no sirve de número de tique.

### D7. Logo y recursos de la plantilla
Una plantilla puede referenciar imágenes por nombre (`{"type":"image","source":"logo.png"}`) guardadas en `%ProgramData%\MiniPrinter\templates\assets\<plantilla>\`. Se suben con `PUT /api/templates/{name}/assets/{file}` (límite de 1 MB por imagen, PNG/JPEG). El logo se rasteriza con tramado Atkinson como `sticker` y se cachea por nombre y fecha de modificación.

### D8. Copias y lotes
`POST …/print/template/{name}` acepta `copies` (1–50) y `rows` (array de objetos de campos, máximo 200). Cada fila se renderiza y los resultados se **encolan como un único trabajo** de varias páginas con un hueco corto entre etiquetas (reutiliza `ContinuousPages`/`PageGapMm`), de modo que cancelar el trabajo cancela el lote y el contador de la cola no se llena con 200 trabajos. Con `copies` se repite el mismo bitmap. Si una fila falla al renderizar, **no se imprime nada** y el error indica el número de fila.

La bandeja carga un CSV (cabecera = nombres de campo; separador `,` o `;`, UTF‑8) y lo envía como `rows`; ofrece una vista previa de la primera fila.

### D9. Bandeja
- **Vista previa en vivo**: cada cambio en un campo reinicia un temporizador de 400 ms; al vencer se pide la vista previa y se **descartan las respuestas antiguas** (número de secuencia) para que una respuesta lenta no pise a una más reciente. Las vistas previas en vivo no consumen contador.
- **Favoritas**: un conjunto de valores guardado bajo un nombre para una plantilla (`%LocalAppData%\MiniPrinter\favorites.json`), con botones Guardar, Cargar y Borrar. Sustituye al "último valor" como única memoria, que se mantiene como el favorito implícito.
- El formulario pasa a generarse también para los nuevos tipos de campo (`Number`, `Date`, `Boolean`) además de los existentes.

### D10. Ajustes de las plantillas actuales como propiedades de bloque
Las opciones nuevas son propiedades con valor por defecto igual al comportamiento actual (por eso la salida no cambia): `label` (`size`, `align`, `border`), `qr` (`ecc` L/M/Q/H, `module`), `todo` (`marker` box/bullet/number, `size`), `barcode` (`format` incluye `code39` y `upca`, `height`). Se exponen en la plantilla integrada como campos opcionales con valor por defecto, por lo que aparecen en la bandeja, la API y la CLI sin código adicional.

## Risks / Trade-offs

- **Regresión visual en las 5 plantillas** → tests de comparación píxel a píxel con los valores por defecto contra el raster generado por el código actual antes de borrar el `switch` (guardar los PNG de referencia en el repositorio).
- **JSON inválido o malicioso (imágenes enormes, miles de bloques, recursión)** → límites duros (64 KB de JSON, 100 bloques, 1 MB por imagen, ancho y alto máximos de la página resultante) y validación en carga y en la API; los bloques no se anidan salvo `columns` (un nivel).
- **Permisos de la carpeta compartida** → `%ProgramData%\MiniPrinter\templates` se crea con permisos de lectura para todos los usuarios y escritura para administradores y el servicio; la bandeja escribe siempre a través del servicio, nunca directamente. Una plantilla sembrada por otro usuario en la carpeta es un dato, no código, y está acotada por los límites anteriores.
- **El contador pierde o repite números ante cierre brusco** → escritura atómica (archivo temporal + reemplazo) y avance **antes** de encolar; es preferible saltarse un número que repetirlo.
- **Vista previa en vivo satura el servicio** → *debounce* de 400 ms, un único *request* en vuelo y descarte de respuestas obsoletas.
- **Lotes grandes ocupan la cola y el papel** → máximo 200 filas, cancelación del trabajo completo y confirmación en la bandeja al superar 20 etiquetas.
- **Ruta de datos distinta a la propuesta (`%LocalAppData%` → `%ProgramData%`)** → se documenta en la propuesta y en las specs; el instalador conserva la carpeta al desinstalar.

## Migration Plan

1. Introducir el motor y los JSON integrados **junto** al código actual y comparar la salida de ambos.
2. Cambiar `TemplateRenderer.Render` a delegar en el motor; borrar el `switch` y las funciones de dibujo antiguas una vez verdes los tests de regresión.
3. Añadir gestión (API/CLI), contador, lotes y bandeja.
4. Sin migración de datos: `templates.json` de la bandeja sigue siendo válido (se añade `favorites.json`). Para revertir basta con quitar las plantillas de usuario; las integradas no dependen de ellas.

## Open Questions (resueltas)

- **Sustituir integradas**: sí. Una plantilla de usuario con el nombre de una integrada la sustituye y la bandeja lo avisa; al borrarla vuelve la integrada.
- **Campo `Date`**: no. Basta texto (`aaaa-mm-dd`) y `{{now}}`; `countdown` valida la fecha con el filtro `days`.
- **Tamaño máximo**: 1 m de papel (8000 filas) por plantilla; más largo se rechaza.
- **Recarga**: el catálogo compara la firma (nombre, fecha y tamaño) de los JSON en cada acceso, en vez de un `FileSystemWatcher`.
- **Lotes**: cada etiqueta es una página del trabajo; el espacio entre etiquetas es el avance de página normal (o el hueco de `ContinuousPages`).
- **CSV**: el lector vive en `MiniPrinter.Control` para que lo compartan la bandeja y la CLI sin arrastrar ImageSharp.
- **Contador sin permisos**: la CLI sin privilegios no puede escribir `counters.json` (carpeta de solo lectura para usuarios); muestra un error claro y la bandeja o la API (servicio) sí numeran.
