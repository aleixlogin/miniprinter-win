## Context

Tras `user-templates`, las plantillas son JSON validado (`TemplateLayout.Parse`), renderizado por `TemplateEngine` y gestionado por `TemplateCatalog` y `TemplateEndpoints` (control en `/api`, automatización en `/api/v1`). La bandeja (`TemplatesPanel`) ya muestra una vista previa exacta pidiendo un PNG al servicio, pero no puede crear ni editar plantillas.

Hechos de partida:
- `LayoutBlocks` es un registro `tipo → BlockType` con un esquema por propiedad (`PropDef`: texto, entero, número, booleano, enum, con rangos). Es la fuente de verdad de lo que acepta cada bloque.
- `TemplateEngine.Render` apila los bloques con `LayoutBlocks.Stack` y un `gap`, y a veces añade un marco (`Frame`) con relleno vertical de 8 filas.
- `TemplateException` solo lleva un mensaje; el bloque y la propiedad van dentro del texto ("Bloque 3 (text): …").
- Las imágenes de un bloque `image` con `source` viven en `templates\assets\<plantilla>\`, y la plantilla se rechaza si el archivo no existe: hoy hay que guardar sin logo, subir y volver a guardar.
- La bandeja solo referencia `MiniPrinter.Control` (no `Imaging`); el servicio es quien escribe en `%ProgramData%`.
- Las vistas previas no consumen contadores.

## Goals / Non-Goals

**Goals:**
- Crear, editar y eliminar plantillas desde la bandeja con una ventana de editor de flujo.
- Una vista previa exacta, en vivo, que permita seleccionar un bloque haciendo clic.
- Que la interfaz no duplique reglas del motor: los controles salen del esquema que publica el servicio.
- Poder previsualizar y subir imágenes de una plantilla sin guardar, sin dejar basura ni tocar la plantilla guardada si se cancela.

**Non-Goals:**
- Lienzo libre, posiciones absolutas, rotación.
- Bloques anidados o un bloque de dos columnas con contenido arbitrario.
- Renombrar plantillas existentes (se usa *Guardar como…*).
- Edición colaborativa o sincronización entre equipos.

## Decisions

### D1. El editor edita el JSON existente, sin formato nuevo
La ventana trabaja sobre un `JsonObject` en memoria (`System.Text.Json.Nodes`) con la misma estructura que `TemplateLayout`, y lo reserializa de forma canónica (sangrado de 2 espacios, propiedades en orden). Así API, CLI y editor son intercambiables y no hay un segundo modelo que mantener.
- *Alternativa descartada*: un modelo de objetos propio en la bandeja (clases tipadas por bloque). Duplicaría el esquema y se desincronizaría al añadir bloques.
- Coste: se pierden los comentarios del JSON de un usuario al guardar desde el editor. Se avisa en la primera edición de una plantilla que los contenga.

### D2. Esquema de bloques publicado por el servicio
`GET /api/templates/schema` devuelve, generado desde el registro `LayoutBlocks`:
```
{ "blocks": [ { "type":"text", "title":"Texto",
                "props":[ {"name":"size","label":"Tamaño (pt)","kind":"number","min":6,"max":48,"default":10},
                          {"name":"align","kind":"enum","values":["left","center","right"],"default":"left"}, … ] }, … ],
  "common": [ {"name":"when","label":"Solo si el campo tiene valor","kind":"text"} ],
  "fieldKinds": ["text","multiline","choice","image","number","boolean"],
  "filters": ["upper","lower","wifi","vcard","days","daysleft"],
  "templateProps": { "gap": {min:0,max:100,default:8}, "modes": ["text","image"] } }
```
`PropDef` gana `Label` y un indicador `Multiline` (para `list.items` y similares) con etiquetas en español. La interfaz crea un control por `kind`: casilla para booleano, desplegable para enum, número con rango, cuadro de texto para el resto. Un bloque nuevo del motor aparece en el editor sin tocar la bandeja.

### D3. Vista previa con filas por bloque
`TemplateEngine` pasa a ofrecer `RenderDetailed` que devuelve el raster y, por cada bloque que ha producido contenido, `{index, type, top, height}` en filas del raster final (suma de alturas y huecos; si hay marco, desplazado 8 filas). Los bloques omitidos (`when` falso o texto vacío) no aparecen.
Las respuestas de vista previa (guardada y de borrador) añaden la cabecera `X-Template-Blocks` con ese arreglo en JSON ASCII (el cuerpo sigue siendo el PNG), así que los clientes existentes no cambian. La bandeja convierte un clic en una fila (1 punto = 1 píxel independiente de dispositivo) y busca el bloque.

### D4. Vista previa de un borrador: `POST /templates/preview`
Cuerpo: `{ "template": { … }, "fields": { … }, "draft": "<id>"? }`. El servicio valida el JSON con las reglas de siempre y lo renderiza sin guardar nada. Es **tolerante**: los campos obligatorios vacíos no son un error (el bloque queda sin contenido), porque mientras se edita es lo normal; las demás validaciones (rangos, marcadores, EAN…) sí fallan con mensaje. Los contadores solo se miran (no se consumen).
Límite de 64 KB y mismos tokens/alcance que el resto. También en `/api/v1/templates/preview`.

### D5. Errores de validación estructurados
`TemplateException` gana `Block` (índice 1-based) y `Property` opcionales, rellenados donde ya se conocen (propiedad desconocida, valor fuera de rango, marcador no declarado, enum no válido, imagen inexistente). La respuesta 400 pasa a `{ "error": "…", "block": 3, "property": "size" }`; `ApiError` añade esos campos opcionales (el texto del mensaje no cambia). La ventana marca el bloque y el control afectados; si no hay `block`, muestra el error general junto a la vista previa.

### D6. Borradores con imágenes
Un borrador es una carpeta `%ProgramData%\MiniPrinter\drafts\<id>\` con un identificador aleatorio de 32 hex generado por el servicio.
- `POST /templates/drafts` → `{ "id": "…" }`.
- `PUT /templates/drafts/{id}/assets/{archivo}` (PNG/JPEG ≤ 1 MB, mismas reglas que las imágenes de plantilla) y `DELETE` del archivo.
- La vista previa con `"draft": id` resuelve un `source` primero en el borrador y luego en las imágenes de la plantilla guardada.
- Al guardar con `PUT /templates/{nombre}?draft={id}` el servicio valida contra las imágenes del borrador, **copia** las usadas a `assets\<nombre>\` y borra el borrador. Si falla la validación no se copia nada.
- `DELETE /templates/drafts/{id}` lo descarta (cancelar el editor). Al iniciar el servicio se borran los borradores de más de 24 h.
- *Alternativa descartada*: subir directamente a `assets\<nombre>\` antes de guardar. Cancelar dejaría imágenes huérfanas o, peor, cambiaría el logo de una plantilla guardada.
- *Alternativa descartada*: imágenes en Base64 dentro del cuerpo de cada vista previa. Retransmitiría hasta 1 MB cada 400 ms.

### D7. Ventana del editor (bandeja)
`TemplateEditorWindow` (WPF, ventana propia con `Owner` = la ventana principal):
```
┌ barra: Guardar · Guardar como… · Deshacer · Rehacer · ⟲ Duplicar bloque ─────────────┐
│ Plantilla │ Bloques        │ Vista previa (tamaño real)  │ Propiedades del bloque   │
│ nombre    │ ⠿ image        │  ┌──────────────┐           │ (controles del esquema)  │
│ título …  │ ⠿ text   ◄sel  │  │ ▒▒ resaltado │ ◄─ clic   │ [Insertar {{campo}} ▾]   │
│ campos    │ + Añadir ▾     │  └──────────────┘           │ ─ Datos de prueba ─      │
│ [JSON]    │                │  error de validación        │                          │
└──────────────────────────────────────────────────────────────────────────────────────┘
```
- **Lista de bloques**: `ListBox` con arrastrar y soltar propio (`DragDrop.DoDragDrop`) y botones ↑ ↓ como alternativa accesible; añadir (desplegable con los tipos del esquema), duplicar y borrar.
- **Deshacer/rehacer**: una pila de instantáneas del JSON (hasta 100), con un nuevo punto tras 600 ms sin teclear para no apuntar cada letra.
- **Vista previa**: reutiliza la lógica de la pestaña (400 ms de *debounce*, número de secuencia para descartar respuestas viejas) y superpone un rectángulo sobre el bloque seleccionado.
- **Datos de prueba**: un formulario generado con los campos declarados (no se guardan en la plantilla), con valores iniciales útiles (por defecto del campo o un ejemplo por tipo).
- **Campos**: tabla editable (nombre, etiqueta, tipo, obligatorio, valor por defecto, opciones); borrar un campo cuyos `{{…}}` aparecen en algún bloque pide confirmación y lista los bloques afectados. Un botón "Insertar" coloca el marcador en el cuadro de texto enfocado.
- **JSON en crudo**: pestaña editable. Al salir de ella (o con 800 ms sin teclear) se parsea: si es válido sustituye el modelo y refresca todo; si no, se muestra el error y se conserva el último modelo válido.
- **Cerrar con cambios**: comparación del JSON canónico con el último guardado; pregunta Guardar / Descartar / Cancelar. Al descartar o cerrar se borra el borrador del servidor.
- **Guardar**: `PUT /templates/{nombre}?draft={id}`; si el servicio rechaza, el error aparece en línea y la ventana sigue abierta. Tras guardar, `TemplatesPanel` recarga la lista y selecciona la plantilla.
- Ventana modal respecto a la lista (`ShowDialog`), para que la pestaña no cambie por debajo.

### D8. Crear, Editar y Eliminar en la pestaña *Plantillas*
- **Crear** ofrece "En blanco" o "Duplicar la plantilla seleccionada", pidiendo el nombre (identificador válido, no existente, no reservado).
- **Editar** abre la plantilla seleccionada. Si `Source == "builtin"`, el título de la ventana y un aviso dicen que al guardar con el mismo nombre se creará una versión de usuario que sustituye a la integrada; *Guardar como…* crea otra con un nombre nuevo. Si es `user` u `override`, guarda en su sitio. El nombre de una plantilla existente no se renombra.
- **Eliminar**: botón desactivado para `builtin`; para `override` el texto pasa a "Restaurar integrada"; confirma con `MessageBox` y llama a `DELETE`. Después se recarga la lista.
- Las plantillas con comentarios o formato propio se editan igual; el aviso de D1 lo deja claro.

### D9. Límites y limpieza
- Mismo máximo de 64 KB y 100 bloques; el editor desactiva "Añadir" al llegar a 100 y avisa cerca de 64 KB.
- `DraftStore` (en el servicio) limita a 20 borradores a la vez y 10 MB por borrador; al superarlo, rechaza con un mensaje claro.
- Borrado por antigüedad al arrancar y al crear uno nuevo.

## Risks / Trade-offs

- **Arrastrar y soltar en WPF es incómodo** → botones ↑↓ de respaldo desde el primer momento y arrastre solo dentro de la lista.
- **Reserializar pierde comentarios o formato propio** → aviso la primera vez que se edita una plantilla con comentarios; se conserva el orden de propiedades.
- **Desincronización entre el modelo y el JSON en crudo** → una sola fuente de verdad (el `JsonObject`), y solo se sustituye si el crudo parsea y valida.
- **La vista previa se satura al teclear** → *debounce* de 400 ms, una petición en vuelo y descarte por número de secuencia; el modo tolerante evita errores por campos vacíos.
- **Borradores huérfanos si la bandeja se cierra a la fuerza** → limpieza a las 24 h al iniciar y al crear otro.
- **El esquema se desfasa del motor** → se genera desde el mismo registro `LayoutBlocks`; un test comprueba que cada propiedad aceptada por un bloque aparece en el esquema.
- **Errores sin bloque** (JSON roto, nombre inválido) → se muestran como error general en la ventana, no junto a un control.

## Migration Plan

1. Motor y servicio: filas por bloque, modo tolerante, errores estructurados, esquema, borradores y endpoints (con tests), sin tocar la bandeja.
2. Bandeja: botones y la ventana del editor, por capas (lista y vista previa → propiedades → campos y datos de prueba → JSON en crudo → deshacer y avisos).
3. Sin migración de datos. Revertir es quitar los botones y los endpoints nuevos; las plantillas guardadas siguen siendo válidas.

## Open Questions (resueltas)

- **Importar y exportar el JSON desde archivo**: no en este change. El editor ya muestra y edita el JSON en crudo (copiar y pegar) y `miniprinter template add|show` cubren archivos; se puede añadir después sin cambiar nada de lo hecho.
- **Historial de versiones por plantilla**: no. Deshacer y rehacer cubren la sesión del editor.
- **Dónde vive el modelo del editor**: en `MiniPrinter.Control` (`TemplateEditorModel`), sin WPF, para probarlo con tests (25 tests: bloques, campos, JSON canónico, deshacer con agrupación).
- **Ventana**: se construye en código (no en XAML) porque los controles de propiedades se generan desde el esquema; se verificó abriéndola contra un servicio de desarrollo y con capturas de pantalla (abrir, seleccionar bloque, error en línea, pestañas, añadir bloque y guardar con borrador).
- **Mensajes de error**: el servicio ya incluye "Bloque N (tipo): …" en el texto, así que la interfaz no lo repite; `block` y `property` solo sirven para marcar el control.
