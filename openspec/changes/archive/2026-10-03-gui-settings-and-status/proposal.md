## Why

Las dos pantallas que más se usan están peor de lo que podrían:

- **Ajustes** es una columna larga con nueve secciones y un único *Guardar* al final. Cambiar una casilla de papel y otra de red obliga a guardar todo junto, y el cambio de papel o de nombre dispara la pregunta de recrear la impresora de Windows aunque solo quisieras tocar el atajo de la nota rápida. Los errores salen en una sola línea de estado.
- **Estado** reparte siete filas de etiqueta y valor, siete botones de uso muy distinto (conectar y muestrear batería juntos) y una lista de trabajos que enseña el estado del servicio tal cual (`Completed`, `ProcessingStopped`, «Printer did not answer»), sin decir de dónde viene cada trabajo (Windows, el panel, la API o el puerto 9100). La vista previa es solo del último trabajo y no hay forma de reimprimir.

Este change rehace las dos pantallas sobre la base de `gui-foundation` (modelos de vista, textos en recursos y tema).

## What Changes

- **Ajustes con navegación lateral** por secciones (Impresión, Papel, Red, Impresión directa, Automatización, Batería, Aspecto, General), **guardado por sección** con botones Aplicar y Descartar, indicador de cambios sin guardar (en la sección y en el menú), aviso al salir con cambios y validación junto a cada campo. Cada guardado lee los ajustes actuales del servicio y cambia solo los campos de su sección. Una **caja de búsqueda** filtra las secciones y los ajustes.
- **Estado como panel**: una tarjeta con el estado en color e icono (Lista, Imprimiendo, Sin papel, Desconectada, Servicio no disponible…), una frase que lo explica, la batería y la acción que toca (*Conectar*, *Buscar impresoras*…). Dos acciones principales a la vista (*Conectar/Desconectar* y *Página de prueba*) y las demás en un menú «⋯». El firmware, las URL y el servicio pasan a un apartado *Detalles* plegable.
- **Lista de trabajos clara**: estados y mensajes en español, icono por estado, columna **Origen** (Windows, Panel, API, Puerto 9100 con la IP), y acciones por trabajo: **doble clic para ver su vista previa** (no solo la del último), **Reimprimir** y **Cancelar**.
- **Historial con vista previa y reimpresión** en el servicio: se guardan las imágenes de los últimos *N* trabajos (por defecto 10, configurable de 0 a 50; 0 conserva solo el último, como hoy) con un tope de espacio, nuevos puntos de la API de control para ver las páginas de un trabajo y reimprimirlo, y el origen de cada trabajo registrado en cada punto de entrada.

## Capabilities

### New Capabilities
- `tray-settings-navigation`: navegación por secciones, guardado por sección, cambios sin guardar, validación y búsqueda de *Ajustes*.
- `tray-status-dashboard`: la tarjeta de estado, las acciones principales y secundarias y los detalles plegables.
- `job-history`: origen de los trabajos, lista traducida, vista previa por trabajo y reimpresión (con la retención en el servicio).

### Modified Capabilities
<!-- Ninguna: los ajustes y el estado siguen existiendo con los mismos efectos; cambia cómo se muestran y se guardan. Los requisitos de las specs existentes (papel, impresión directa, API de automatización…) se cumplen igual desde su nueva sección. -->

## Impact

- **Código**:
  - `MiniPrinter.Gui`: modelos de vista de *Ajustes* (secciones, estado de cambios, validación, búsqueda) y de *Estado* (estado derivado, acciones, lista de trabajos).
  - `MiniPrinter.Tray`: `MainWindow.xaml` (pestañas Estado y Ajustes rehechas), `PaperSettingsPanel.cs` y el diálogo de recrear impresora pasan a la sección *Papel*.
  - `MiniPrinter.Control`: `JobDto` gana `source`, `origin` y `canReprint`; el cliente gana `GetJobPageAsync` y `ReprintJobAsync`.
  - `MiniPrinter.Service`: `JobInfo`/`JobQueue` (origen y retención de imágenes por trabajo), `ControlApi` (`GET /jobs/{id}/pages/{n}`, `POST /jobs/{id}/reprint`), `AutomationApi`, `RawPortHost`, `IppPrinterService` (registran su origen) y el ajuste `JobHistoryKeep`.
- **Datos**: se guardan en `%ProgramData%\MiniPrinter\jobs\<id>\` las páginas ya rasterizadas de los últimos trabajos (contenido de lo impreso: tiques, etiquetas). Por defecto 10 trabajos, con tope de 50 MB; el usuario puede ponerlo a 0 y se borra todo al desinstalar.
- **Compatibilidad**: los ajustes existentes y la API actual no cambian; los campos nuevos de `JobDto` son opcionales para clientes antiguos.
- **Depende de** `gui-foundation` (modelos de vista, recursos de texto y tema).
- **Fuera de alcance**: el diagnóstico, la galería de plantillas y las mejoras del puerto 9100 (`gui-diagnostics-and-templates`).
