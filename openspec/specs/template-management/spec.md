# template-management Specification

## Purpose
TBD - created by archiving change user-templates. Update Purpose after archive.
## Requirements
### Requirement: Gestión de plantillas de usuario
El servicio SHALL permitir listar, leer, crear o reemplazar, validar y borrar plantillas de usuario mediante `GET /api/templates`, `GET /api/templates/{nombre}`, `PUT /api/templates/{nombre}`, `DELETE /api/templates/{nombre}` y `POST /api/templates/validate`. Las plantillas integradas SHALL poder leerse pero no borrarse.

#### Scenario: Crear una plantilla
- **WHEN** se envía un JSON válido con `PUT /api/templates/recibo`
- **THEN** la plantilla se guarda y aparece en el listado

#### Scenario: JSON inválido
- **WHEN** se envía un JSON con un bloque desconocido
- **THEN** se responde 400 con el motivo y no se guarda nada

#### Scenario: Borrar una integrada
- **WHEN** se intenta borrar la plantilla integrada `qr` sin una versión de usuario
- **THEN** se rechaza indicando que es una plantilla integrada

#### Scenario: Validar sin guardar
- **WHEN** se envía un JSON a `POST /api/templates/validate`
- **THEN** se responde con los errores encontrados o con la confirmación, sin guardar

### Requirement: Nombres de plantilla seguros
Los nombres de plantilla SHALL ser identificadores de hasta 40 caracteres (letras, dígitos, guion y guion bajo). El servicio SHALL rechazar cualquier nombre que pueda salir de la carpeta de plantillas.

#### Scenario: Nombre con ruta
- **WHEN** se intenta guardar una plantilla con el nombre "../config"
- **THEN** se rechaza sin escribir ningún archivo

### Requirement: Recursos de la plantilla
El servicio SHALL permitir subir y borrar las imágenes de una plantilla (`PUT` y `DELETE /api/templates/{nombre}/assets/{archivo}`), solo PNG o JPEG de hasta 1 MB, y SHALL borrarlas al borrar la plantilla.

#### Scenario: Subir un logo
- **WHEN** se sube "logo.png" de 200 KB a la plantilla `recibo`
- **THEN** la imagen queda disponible para los bloques `image` de esa plantilla

#### Scenario: Imagen demasiado grande
- **WHEN** se sube una imagen de 3 MB
- **THEN** se rechaza indicando el límite de 1 MB

### Requirement: Gestión desde la CLI
La CLI SHALL ofrecer `miniprinter template list|show|add|remove`, donde `add` valida el archivo antes de guardarlo, y SHALL poder validar sin que el servicio esté en marcha.

#### Scenario: Añadir desde un archivo
- **WHEN** se ejecuta `miniprinter template add recibo.json`
- **THEN** la plantilla se valida y se guarda con el nombre indicado en el JSON

#### Scenario: Validar sin servicio
- **WHEN** se ejecuta `miniprinter template validate recibo.json` con el servicio parado
- **THEN** se muestran los errores o la confirmación

### Requirement: Plantillas favoritas
La bandeja SHALL permitir guardar los valores de un formulario como un favorito con nombre para esa plantilla, cargarlo y borrarlo, y SHALL conservarlos entre sesiones. Los valores del último uso SHALL seguir recordándose.

#### Scenario: Guardar un favorito
- **WHEN** el usuario rellena `label` con "Cajón 3" y pulsa Guardar favorito con el nombre "Cajón 3"
- **THEN** el favorito aparece en la lista de `label`

#### Scenario: Cargar un favorito
- **WHEN** el usuario elige el favorito "Cajón 3"
- **THEN** el formulario se rellena con sus valores y la vista previa se actualiza

#### Scenario: Persistencia
- **WHEN** se reinicia la bandeja
- **THEN** los favoritos siguen disponibles

### Requirement: Vista previa en vivo
La bandeja SHALL actualizar la vista previa automáticamente tras un breve retraso (400 ms) desde el último cambio en un campo, mostrando siempre el resultado de la petición más reciente y sin consumir contadores.

#### Scenario: Escribir en un campo
- **WHEN** el usuario escribe en un campo y se detiene 400 ms
- **THEN** la vista previa se actualiza sin pulsar ningún botón

#### Scenario: Respuestas fuera de orden
- **WHEN** una respuesta lenta llega después de una más reciente
- **THEN** se descarta y no sustituye a la vista previa actual

#### Scenario: Error de validación mientras se escribe
- **WHEN** el contenido actual no es válido (por ejemplo, un EAN‑13 incompleto)
- **THEN** se muestra el mensaje de error junto a la vista previa, sin ventanas emergentes

### Requirement: Botones Crear, Editar y Eliminar en la bandeja
La pestaña *Plantillas* SHALL incluir los botones Crear, Editar y Eliminar. Crear SHALL ofrecer una plantilla en blanco o duplicar la seleccionada, pidiendo un nombre válido, que no exista y que no sea reservado. Editar SHALL abrir la plantilla seleccionada en el editor. Eliminar SHALL pedir confirmación antes de borrar.

#### Scenario: Crear en blanco
- **WHEN** el usuario pulsa Crear, elige "En blanco" y escribe el nombre "etiqueta2"
- **THEN** se abre el editor con una plantilla nueva llamada `etiqueta2`

#### Scenario: Crear duplicando
- **WHEN** el usuario selecciona `label`, pulsa Crear, elige "Duplicar la seleccionada" y escribe "mi-etiqueta"
- **THEN** se abre el editor con una copia de `label` llamada `mi-etiqueta`

#### Scenario: Nombre ya existente
- **WHEN** el usuario escribe un nombre que ya existe
- **THEN** se rechaza indicando que ya existe, sin abrir el editor

### Requirement: Editar una plantilla integrada
Al editar una plantilla integrada, el editor SHALL avisar de que guardarla con el mismo nombre crea una versión de usuario que sustituye a la integrada, y SHALL ofrecer guardarla con un nombre nuevo.

#### Scenario: Sustituir una integrada
- **WHEN** el usuario edita `label` y guarda con el mismo nombre
- **THEN** se crea una plantilla de usuario `label` que sustituye a la integrada, y la pestaña la marca como sustituida

#### Scenario: Copia con otro nombre
- **WHEN** el usuario edita `label` y usa *Guardar como…* con "mi-etiqueta"
- **THEN** `label` sigue siendo la integrada y aparece la nueva plantilla `mi-etiqueta`

### Requirement: Eliminar según el origen
Eliminar SHALL borrar una plantilla de usuario y sus imágenes. Para una plantilla de usuario que sustituye a una integrada, el botón SHALL llamarse "Restaurar integrada" y volver a usar la integrada. Para una integrada sin versión de usuario, el botón SHALL estar desactivado.

#### Scenario: Borrar una plantilla de usuario
- **WHEN** el usuario elige `recibo` (de usuario), pulsa Eliminar y confirma
- **THEN** la plantilla desaparece de la lista

#### Scenario: Restaurar una integrada
- **WHEN** el usuario elige `label` (que sustituye a la integrada), pulsa "Restaurar integrada" y confirma
- **THEN** la lista vuelve a mostrar la plantilla integrada `label`

#### Scenario: Integrada sin versión de usuario
- **WHEN** el usuario selecciona `qr`
- **THEN** el botón Eliminar está desactivado

#### Scenario: Cancelar la confirmación
- **WHEN** el usuario cancela el aviso de Eliminar
- **THEN** no se borra nada

### Requirement: Refresco de la lista tras gestionar
Tras guardar en el editor, o tras eliminar o restaurar una plantilla, la pestaña *Plantillas* SHALL recargar la lista del servicio y seleccionar la plantilla guardada, o la más cercana si se eliminó.

#### Scenario: Seleccionar la plantilla guardada
- **WHEN** el usuario guarda la plantilla `recibo` en el editor
- **THEN** la pestaña muestra `recibo` seleccionada con su formulario

### Requirement: Consultar los campos de una plantilla
El servicio SHALL ofrecer `GET /api/templates/{nombre}/fields` (y `GET /api/v1/templates/{nombre}/fields`) que devuelve los campos de esa plantilla (nombre, etiqueta, tipo, obligatorio, opciones y valor por defecto) y los parámetros de impresión que acepta (`copies`, `rows` y `darkness`, con su descripción y límites). Si la plantilla no existe SHALL responder 404.

#### Scenario: Campos de una plantilla integrada
- **WHEN** se pide `GET /api/v1/templates/label/fields` con el token
- **THEN** la respuesta lista `title` (obligatorio), `line1`, `line2`, `size` (valor por defecto 26), `align` (opciones) y `border`

#### Scenario: Parámetros de impresión
- **WHEN** se consultan los campos de cualquier plantilla
- **THEN** la respuesta incluye los parámetros `copies` (1–50), `rows` (hasta 200 filas) y `darkness` (1–5)

#### Scenario: Plantilla de usuario
- **WHEN** se consultan los campos de una plantilla creada por el usuario
- **THEN** la respuesta refleja los campos de su JSON, con sus tipos y valores por defecto

#### Scenario: Plantilla inexistente
- **WHEN** se pide `/templates/no-existe/fields`
- **THEN** se responde 404 con un mensaje que indica que la plantilla no existe

#### Scenario: Sin token
- **WHEN** se pide la ruta en `/api/v1` sin token válido
- **THEN** se responde 401 y no se revela nada de la plantilla

### Requirement: Campos de una plantilla desde la CLI
`miniprinter template fields <nombre>` SHALL mostrar los campos de la plantilla y los parámetros de impresión sin necesidad de impresora ni de que el servicio esté en marcha.

#### Scenario: Consulta local
- **WHEN** se ejecuta `miniprinter template fields receipt` con el servicio parado
- **THEN** se listan sus campos, marcando los obligatorios, y los parámetros `copies`, `rows` y `darkness`

#### Scenario: Plantilla desconocida
- **WHEN** se pide una plantilla que no existe
- **THEN** la orden termina con un error que lista las plantillas disponibles

