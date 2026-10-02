## 1. Motor: filas por bloque, modo tolerante y errores estructurados

- [x] 1.1 Añadir `TemplateEngine.RenderDetailed` que devuelve el raster y, por cada bloque con contenido, `{index, type, top, height}` (con el desplazamiento del marco); `Render` delega en él. Tests: texto + línea + QR, bloque omitido por `when`, plantilla con marco
- [x] 1.2 Añadir a `RenderOptions` un modo tolerante (`Lenient`) que no falla por campos obligatorios vacíos; tests de que sí falla sin él y de que el resto de validaciones siguen activas
- [x] 1.3 Extender `TemplateException` con `Block` y `Property` opcionales y rellenarlos en los puntos de validación conocidos (propiedad desconocida, fuera de rango, enum, marcador no declarado, imagen inexistente); el texto de los mensajes no cambia. Tests de cada caso
- [x] 1.4 Añadir `Label` y `Multiline` a `PropDef` (etiquetas en español para todas las propiedades de todos los bloques) y generar el esquema (`BlockSchema`) desde el registro `LayoutBlocks`; test de coherencia: toda propiedad aceptada por un bloque está en el esquema

## 2. Servicio y API (template-editor-api)

- [x] 2.1 `GET /templates/schema` en `TemplateEndpoints` (control y `/api/v1`) con bloques, propiedades comunes, tipos de campo, filtros y límites de la plantilla; test de contenido
- [x] 2.2 Cabecera `X-Template-Blocks` en `POST /templates/{nombre}/preview`; test de que el cuerpo sigue siendo el mismo PNG
- [x] 2.3 `ApiError` con `block` y `property` opcionales y su relleno desde `TemplateException` en los endpoints de plantillas; actualizar `ControlClient`/`ControlApiException`; tests de 400 con y sin bloque
- [x] 2.4 `DraftStore` en el servicio: crear (id aleatorio de 32 hex), subir y borrar imágenes (PNG/JPEG ≤ 1 MB), descartar, límite de 20 borradores y 10 MB cada uno, borrado de los de más de 24 h al iniciar y al crear. Tests de ids no válidos (rutas), límites y limpieza
- [x] 2.5 Endpoints de borradores: `POST /templates/drafts`, `PUT`/`DELETE /templates/drafts/{id}/assets/{archivo}` y `DELETE /templates/drafts/{id}` (control y `/api/v1`)
- [x] 2.6 `POST /templates/preview` (JSON + campos + borrador opcional; tolerante; sin guardar ni consumir contadores; máximo 64 KB) con la cabecera de filas; el catálogo resuelve `source` en el borrador y luego en la plantilla guardada. Tests: borrador con logo, campo obligatorio vacío, contador intacto, JSON de 100 KB
- [x] 2.7 `PUT /templates/{nombre}?draft={id}`: valida contra las imágenes del borrador, copia las usadas y borra el borrador; si falla no copia ni borra. Tests de guardado con logo, con error y de cancelar el borrador
- [x] 2.8 Ampliar `ControlClient` con esquema, borradores, vista previa de borrador (PNG + filas) y guardado con borrador; tests por el cliente

## 3. Bandeja: botones y gestión desde la pestaña

- [x] 3.1 Añadir los botones Crear, Editar y Eliminar a la pestaña *Plantillas* y activarlos según la selección y el origen (`builtin` desactiva Eliminar; `override` lo renombra a "Restaurar integrada")
- [x] 3.2 Diálogo de Crear (en blanco o duplicando la seleccionada) con validación del nombre (válido, no existente, no reservado) y plantillas iniciales
- [x] 3.3 Eliminar con confirmación y recarga de la lista; recarga y selección de la plantilla guardada al volver del editor (`TemplatesPanel.LoadAsync(select)`)

## 4. Editor: ventana, lista de bloques y vista previa

- [x] 4.1 `TemplateEditorWindow` (modal, `Owner` = ventana principal) con la distribución de cuatro zonas y el modelo `JsonObject` canónico como única fuente de verdad; cargar el esquema al abrir
- [x] 4.2 Lista de bloques: añadir (desplegable con los tipos del esquema), borrar, duplicar, botones ↑ ↓ y arrastrar y soltar; desactivar Añadir al llegar a 100 bloques
- [x] 4.3 Vista previa en vivo: *debounce* de 400 ms, número de secuencia para descartar respuestas viejas, error general junto a la vista previa
- [x] 4.4 Clic en la vista previa → selecciona el bloque (con `X-Template-Blocks`) y resaltado del bloque seleccionado con un rectángulo superpuesto

## 5. Editor: propiedades, campos y datos de prueba

- [x] 5.1 Panel de propiedades generado desde el esquema (casilla, desplegable, número con rango, texto multilínea), incluida `when`; cambios al modelo con el *debounce*
- [x] 5.2 Botón "Insertar" para `{{campo}}`, `{{now}}`, `{{counter}}` y filtros en la posición del cursor del cuadro de texto enfocado
- [x] 5.3 Editor de campos (nombre, etiqueta, tipo, obligatorio, valor por defecto, opciones) con nombre duplicado rechazado y aviso al borrar un campo usado (lista los bloques afectados)
- [x] 5.4 Datos de prueba: formulario con los campos declarados, valores iniciales útiles y envío a la vista previa (no se guardan en la plantilla)
- [x] 5.5 Propiedades de plantilla: título, descripción, modo, hueco y marco; nombre fijo en plantillas existentes
- [x] 5.6 Imágenes: elegir archivo en un bloque `image`, subirlo al borrador y quitarlo; crear el borrador al abrir y descartarlo al cerrar

## 6. Editor: JSON en crudo, deshacer, validación y cierre

- [x] 6.1 Pestaña JSON editable sincronizada (parseo con 800 ms de espera o al salir; válido sustituye el modelo, inválido conserva el último modelo y muestra el error)
- [x] 6.2 Deshacer y rehacer (`Ctrl+Z`, `Ctrl+Y`, 100 pasos, agrupando lo tecleado de forma continua con 600 ms)
- [x] 6.3 Validación en línea: marcar el bloque en la lista y el control afectado con `block`/`property`; error general si no hay bloque
- [x] 6.4 Guardar y *Guardar como…* (con `?draft=`), errores en línea sin cerrar, aviso de comentarios que se pierden al reserializar y aviso al editar una integrada
- [x] 6.5 Cerrar con cambios sin guardar: preguntar Guardar / Descartar / Cancelar y borrar el borrador al descartar

## 7. Cierre

- [x] 7.1 Tests de la bandeja que sean viables sin interfaz (lógica del modelo: deshacer/rehacer, campos usados, reserializado canónico) y comprobar que compila toda la solución
- [x] 7.2 Actualizar el `README` (sección *Plantillas*: botones, editor, esquema y borradores en la API) y el historial de cambios
- [x] 7.3 Resolver las preguntas abiertas del diseño (importar y exportar JSON, historial de versiones) y reflejarlas en las specs
- [x] 7.4 Comprobación manual con la impresora real: crear una plantilla con logo desde el editor, editar una integrada y guardar como copia, restaurar una integrada y eliminar una de usuario
