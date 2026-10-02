## Why

Las plantillas ya son JSON editable (`user-templates`), pero crear o ajustar una exige escribir el JSON a mano, conocer los tipos de bloque y sus rangos y subir las imágenes en un orden incómodo. La vista previa del servicio ya es exacta (el mismo motor que imprime), así que un editor visual de flujo es el paso natural: que cualquiera pueda diseñar una etiqueta o un tique viendo el resultado real mientras lo hace.

## What Changes

- **Botones en la pestaña *Plantillas*** de la bandeja: **Crear**, **Editar** y **Eliminar**. Crear y Editar abren una ventana nueva (el editor); Eliminar pide confirmación.
  - Crear: plantilla en blanco o duplicando una existente.
  - Editar: si la plantilla es integrada, se guarda como copia de usuario que la sustituye (con aviso) o con un nombre nuevo.
  - Eliminar: solo plantillas de usuario; si una plantilla de usuario sustituye a una integrada, Eliminar restaura la integrada; las integradas sin versión de usuario no se pueden eliminar y el botón se desactiva.
- **Ventana del editor** (editor de flujo, sin lienzo libre ni bloques anidados):
  - Lista de bloques con añadir, borrar, duplicar, reordenar arrastrando y deshacer/rehacer.
  - Vista previa a tamaño real que se actualiza sola (400 ms) y donde un clic selecciona el bloque.
  - Panel de propiedades con controles generados desde el esquema de bloques, y botón para insertar `{{campo}}`, `{{now}}` y `{{counter}}`.
  - Editor de campos, datos de prueba, propiedades de la plantilla (nombre, título, descripción, modo, hueco, marco) y pestaña de JSON en crudo sincronizada.
  - Validación en línea con el mensaje del servicio junto al bloque o la propiedad afectados, aviso al borrar un campo en uso y aviso al cerrar con cambios sin guardar.
- **Servicio/API** (también en `/api/v1`):
  - La vista previa informa de las filas que ocupa cada bloque (cabecera `X-Template-Blocks`).
  - `POST /templates/preview`: vista previa de una plantilla sin guardar (JSON + datos de prueba), tolerante a campos obligatorios vacíos.
  - `GET /templates/schema`: tipos de bloque, propiedades, tipos de valor, rangos y opciones, para que la interfaz no duplique reglas.
  - Borradores con imágenes (`drafts`): subir un logo antes de guardar la plantilla, sin el orden actual, y sin tocar la plantilla guardada si se cancela.
  - Errores de validación estructurados (`block`, `property`).
- El formato JSON de las plantillas **no cambia**: lo que crea el editor funciona por API y CLI, y al revés.

## Capabilities

### New Capabilities
- `template-editor`: ventana del editor de plantillas de la bandeja y su comportamiento.
- `template-editor-api`: vista previa de borradores, filas por bloque, esquema de bloques, imágenes de borrador y errores estructurados.

### Modified Capabilities
- `template-management`: botones Crear, Editar y Eliminar en la bandeja y refresco de la lista tras guardar o eliminar.
- `print-templates`: la pestaña *Plantillas* gana los botones de gestión, y las vistas previas informan de las filas de cada bloque.

## Impact

- **Código**:
  - `MiniPrinter.Imaging`: el motor devuelve las filas por bloque, un modo tolerante para vista previa, errores con bloque y propiedad, y el esquema de bloques generado desde el registro.
  - `MiniPrinter.Service`: nuevos endpoints en `TemplateEndpoints` (preview de borrador, schema, drafts) y limpieza de borradores.
  - `MiniPrinter.Control`: contratos y cliente.
  - `MiniPrinter.Tray`: botones en `TemplatesPanel` y ventana `TemplateEditorWindow` (XAML, arrastrar y soltar, deshacer/rehacer).
- **Dependencias**: ninguna nueva (se usa `System.Text.Json.Nodes` en la bandeja).
- **Datos**: `%ProgramData%\MiniPrinter\drafts\<id>\` para imágenes de borrador (se limpian a las 24 h y al guardar o cancelar).
- **Seguridad**: los endpoints nuevos usan los mismos tokens y límites que la gestión actual (64 KB por plantilla, 1 MB por imagen); el nombre de un borrador es un identificador generado, nunca una ruta.
- **Compatibilidad**: sin cambios de formato; las vistas previas existentes siguen devolviendo el PNG como hasta ahora (la cabecera nueva es adicional).
- **Fuera de alcance**: lienzo libre, anidar bloques y un bloque de dos columnas con contenido arbitrario.
