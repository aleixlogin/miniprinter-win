## Context

`MainWindow.xaml` tiene la pestaña *Ajustes* como un `ScrollViewer` con nueve bloques (Trabajos, Impresión, Batería, Conexión y red, Papel que se muestra a Windows, Impresión directa, Actualizaciones, Impresión rápida, API de automatización) y `OnSaveSettings` construye un `ServiceSettings` entero con todos los controles y lo envía con `PUT /api/settings`; si cambian los tamaños de papel o el nombre, antes de guardar abre el diálogo de recrear la impresora. `PaperSettingsPanel.cs` (321 líneas) construye el bloque de papel en código. La pestaña *Estado* tiene siete filas de etiqueta y valor, siete botones y un `ListView` con cinco columnas ligado a `JobDto`.

En el servicio, `JobQueue` conserva en memoria los últimos 20 trabajos (`HistorySize`) y en disco solo el último (`%ProgramData%\MiniPrinter\last-job`, páginas PNG y `job.txt`), que alimenta `GET /api/jobs/last/pages/{n}`. El usuario del trabajo (`JobInfo.UserName`) no sirve para saber el origen: la API de control usa `Environment.UserName` (la cuenta del servicio), IPP trae el usuario de Windows y el puerto 9100 `raw@ip`.

## Goals / Non-Goals

**Goals:**
- Guardar y descartar por sección, sin sobrescribir lo que se haya cambiado desde otro sitio.
- Un estado que se entienda de un vistazo y diga qué hacer.
- Una lista de trabajos en español, con origen, vista previa por trabajo y reimpresión.
- Toda la lógica nueva en modelos de vista probados.

**Non-Goals:**
- Cambiar qué ajustes existen o sus efectos.
- Diagnóstico, galería de plantillas y mejoras del puerto 9100 (otro change).
- Un historial de trabajos permanente o exportable.

## Decisions

### D1. Secciones de *Ajustes*
Un `SettingsViewModel` con una colección de `SettingsSectionViewModel` y una sección seleccionada. Cada sección tiene su plantilla de vista (una `UserControl` por sección) y expone: campos, `IsDirty`, `Errors` por campo, `ApplyAsync()` y `Discard()`. Reparto de los controles actuales:

| Sección | Contenido |
|---|---|
| Impresión | oscuridad, modo, tramado, avance final, páginas continuas, hueco, fuente y tamaño del texto, reintentos, mantener activa, desconexión por inactividad, **historial de trabajos** (nuevo) |
| Papel | nombre en Windows, tamaños que se ofrecen, recrear la impresora (hoy `PaperSettingsPanel`) |
| Red | alcance (solo este PC / red local), puerto IPP |
| Impresión directa | casilla, puerto, estado y aviso del 9100 |
| Automatización | API de automatización, token |
| Batería | unidad, aviso, muestreo |
| Aspecto | tema y tamaño del texto (de `gui-foundation`) |
| General | actualizaciones automáticas, atajo de la nota rápida |

### D2. Guardado por sección sin pisar a las demás
`ApplyAsync` hace *leer–cambiar–escribir*: pide los ajustes actuales al servicio (`GET /api/settings`), sustituye solo los campos de su sección y los envía con `PUT /api/settings` (que ya conserva `Printer` y `PrinterUuid`). Así, dos secciones con cambios sin guardar no se pisan y un cambio hecho por la CLI, la API o el servicio no se pierde. Se mantiene la copia que mostró cada sección para detectar «cambios en el servicio mientras editabas» (se avisa y se ofrece recargar).
- **Papel**: *Aplicar* conserva el diálogo de recrear la impresora (hoy en `OnSaveSettings`); *Red*: la confirmación de «Toda la red local» y el aviso del puerto 9100 siguen en su sección. Son los únicos aplicados con confirmación.
- *Alternativa descartada*: guardar al instante cada cambio. Algunos ajustes reinician el escucha de red o la cola de Windows y necesitan confirmación y agrupar campos (puerto + alcance).

### D3. Cambios sin guardar
`IsDirty` compara los valores actuales de la sección con los últimos cargados. Se muestra un punto en el elemento del menú lateral y una barra inferior de la sección con «Cambios sin guardar» y los botones. Cambiar de sección **no** pide confirmación (los cambios se conservan en memoria); cerrar el panel o la ventana con cambios pendientes sí pregunta (Aplicar, Descartar, Cancelar).

### D4. Validación junto a cada campo
Cada campo declara su regla (rango del puerto 1024–65535, enteros, tamaños de papel válidos con `PaperCatalog.Problem`, atajo de teclado con formato) y su mensaje sale bajo el control. *Aplicar* queda desactivado mientras haya errores. La validación se prueba en el modelo, sin ventanas.

### D5. Búsqueda
Un índice de palabras clave por ajuste (etiqueta y sinónimos, en el recurso de textos). La caja filtra el menú lateral a las secciones que contienen coincidencias y resalta los ajustes que coinciden dentro de la sección. Es solo un filtro: no cambia ajustes.

### D6. Estado derivado
Un `StatusViewModel` calcula un único estado visible a partir de `StatusDto`, por prioridad:

```
 servicio no disponible → sin impresora elegida → alarma (sin papel, calor, batería baja)
 → error de enlace → conectando/reconectando → imprimiendo → conectada y lista → desconectada (se conecta al imprimir)
```
Cada estado tiene icono, color (pinceles del tema), una frase y una acción sugerida (*Buscar impresoras*, *Conectar*, *Abrir ajustes*…). Es una función pura, probada con una tabla de casos. La batería sale como barra con porcentaje cuando se conoce y como valor en bruto si no. Las acciones secundarias (*Avanzar papel*, *Muestrear batería*, *Exportar registro*, *Vista previa del último*) van en un menú «⋯» y los datos de bajo uso (firmware, URL, servicio, versión) en un expansor *Detalles*.

### D7. Lista de trabajos
Las filas son modelos de vista (`JobRowViewModel`) con estado y mensaje traducidos (tabla de `gui-foundation`), icono, origen y acciones disponibles. Doble clic abre la vista previa del trabajo; *Reimprimir* aparece si el servicio dice `canReprint`; *Cancelar* si no ha terminado. La selección se conserva entre refrescos de estado.

### D8. Origen de los trabajos (servicio)
`JobInfo` gana `Source` (`Windows`, `Panel`, `Api`, `Raw`) y `Origin` (texto libre: usuario de Windows, dirección IP del cliente). Cada punto de entrada lo pone al crear el trabajo: `IppPrinterService` (`Windows`, con el usuario IPP), `ControlApi` (`Panel`), `AutomationApi` (`Api`, con la IP del cliente), `RawPortHost` (`Raw`, con la IP). `JobDto` los expone como `source` y `origin`. No se deriva de `UserName`, que no es fiable.

### D9. Historial con imágenes (servicio)
`JobQueue` guarda, al terminar o cancelar un trabajo con páginas rasterizadas, las páginas en `%ProgramData%\MiniPrinter\jobs\<id>\page-N.png` (1 bit) y una `job.json` con lo necesario para reimprimir (nombre, oscuridad, modo de texto). Reglas:
- Se conservan los últimos `JobHistoryKeep` trabajos (por defecto 10, de 0 a 50; 0 conserva solo el último, que ya se guarda en `last-job` para la vista previa actual) y como máximo 50 MB en total (se borran los más antiguos primero).
- Los trabajos con más de 40 páginas no se guardan (la vista previa dice «demasiado grande»).
- Al arrancar se limpian los directorios que no correspondan a un trabajo conocido y se recortan al límite.
- El instalador/desinstalador borra `jobs\`.
- `GET /api/jobs/{id}/pages/{n}` devuelve la página (PNG) o 404; `POST /api/jobs/{id}/reprint` encola las mismas páginas como un trabajo nuevo («Reimpresión de …», origen `Panel`, con la oscuridad y el modo del original) o 404/409 si no hay imágenes. `GET /api/jobs/last/pages/{n}` sigue funcionando (alias del último guardado).
- *Alternativa descartada*: guardar el documento original y volver a rasterizar. Más espacio, depende del formato y puede salir distinto; las páginas ya rasterizadas son exactamente lo que se imprimió.

### D10. Privacidad de lo guardado
Las imágenes son lo impreso (tiques, etiquetas, a veces datos personales). Por eso es configurable, se puede poner a 0, se borra al desinstalar y el directorio hereda los permisos de `%ProgramData%\MiniPrinter` (lectura para los usuarios, escritura para el servicio); la sección *Impresión* lo dice en el texto del ajuste. La API de control ya solo escucha en `127.0.0.1` y exige token.

## Risks / Trade-offs

- **Leer–cambiar–escribir no es atómico** → dos clientes a la vez pueden pisarse; se acepta (un usuario, un panel) y se detecta el cambio en el servicio para avisar.
- **Rehacer Ajustes toca muchos controles** → se migra sección por sección, con el modelo probado antes que la vista; los ajustes existentes mantienen sus nombres en `ServiceSettings`.
- **Guardar imágenes crece en disco** → tope de 50 MB y de páginas, limpieza al arrancar y ajuste a 0.
- **El origen `Panel` incluye la nota rápida y el portapapeles** (usan la API de control) → es lo esperado: «Panel» significa «la aplicación de bandeja».
- **El texto de los estados del servicio puede cambiar** → la tabla tiene reserva con el texto original, y una prueba recorre los estados conocidos del servicio.

## Migration Plan

1. Servicio: `JobInfo.Source/Origin`, registro en cada punto de entrada y campos nuevos de `JobDto`; retención de páginas, ajuste `JobHistoryKeep`, `GET /jobs/{id}/pages/{n}`, `POST /jobs/{id}/reprint`, cliente de control; pruebas del servicio.
2. Modelos de vista de *Ajustes* (secciones, cambios, validación, búsqueda, lectura–cambio–escritura) con pruebas; luego sus vistas, sección a sección, empezando por las más sencillas.
3. Modelo de vista de *Estado* (estado derivado, acciones, filas) con pruebas; luego la vista (tarjeta, menú «⋯», detalles, lista).
4. Vista previa y reimpresión por trabajo en la lista.
5. README, comprobación visual por el usuario y pruebas completas.
Sin migración de datos. Revertir: `JobHistoryKeep` a 0 y las pantallas antiguas siguen en el historial de git.

## Open Questions

- ¿Hace falta un botón «Restaurar valores de fábrica» por sección? Se deja fuera; si se pide, es una acción más del modelo de vista de cada sección.
- ¿La búsqueda también debe encontrar acciones (por ejemplo «recrear impresora»)? Se empieza solo con ajustes.
