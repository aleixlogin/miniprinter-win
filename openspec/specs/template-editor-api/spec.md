# template-editor-api Specification

## Purpose
TBD - created by archiving change template-editor. Update Purpose after archive.
## Requirements
### Requirement: Esquema de bloques
El servicio SHALL publicar en `GET /api/templates/schema` (y `/api/v1/templates/schema`) el esquema de los bloques generado desde el propio motor: para cada tipo, sus propiedades con nombre, etiqueta, tipo de valor (`text`, `int`, `number`, `bool`, `enum`), rango, valor por defecto, opciones y si el texto es multilínea; además las propiedades comunes (`when`), los tipos de campo, los filtros disponibles y los límites de las propiedades de la plantilla.

#### Scenario: Consultar el esquema
- **WHEN** un cliente con token pide el esquema
- **THEN** recibe todos los tipos de bloque del motor con sus propiedades, rangos y opciones

#### Scenario: Coherencia con el motor
- **WHEN** el motor acepta una propiedad en un bloque
- **THEN** esa propiedad aparece en el esquema de ese bloque

### Requirement: Vista previa de una plantilla sin guardar
El servicio SHALL ofrecer `POST /api/templates/preview` (y `/api/v1/templates/preview`) con un cuerpo `{ "template": {…}, "fields": {…}, "draft": "<id>" }` que valida la plantilla y devuelve el PNG de su resultado sin guardar nada y sin consumir contadores. Los campos obligatorios sin valor NO SHALL ser un error en esta vista previa; el resto de validaciones sí.

#### Scenario: Previsualizar un borrador
- **WHEN** se envía una plantilla válida que no está guardada
- **THEN** se devuelve su PNG y no aparece ningún archivo nuevo en la carpeta de plantillas

#### Scenario: Campo obligatorio vacío
- **WHEN** la plantilla declara `cliente` obligatorio y los datos de prueba no lo traen
- **THEN** la vista previa se genera igualmente, sin el contenido de ese campo

#### Scenario: Plantilla inválida
- **WHEN** el borrador contiene una propiedad fuera de rango
- **THEN** se responde 400 con el mensaje de validación

#### Scenario: Plantilla demasiado grande
- **WHEN** el JSON supera 64 KB
- **THEN** se rechaza indicando el límite

#### Scenario: El contador no avanza
- **WHEN** el borrador usa `{{counter}}` y se previsualiza varias veces
- **THEN** el contador persistente no avanza

### Requirement: Filas que ocupa cada bloque
Las vistas previas de plantillas (guardadas y de borrador) SHALL incluir la cabecera `X-Template-Blocks` con una lista JSON de `{index, type, top, height}`, en filas del raster devuelto, para cada bloque que haya producido contenido, de modo que un cliente pueda relacionar una posición de la imagen con un bloque. El cuerpo de la respuesta SHALL seguir siendo el PNG.

#### Scenario: Bloques en orden
- **WHEN** una plantilla tiene un texto, una línea y un QR
- **THEN** la cabecera lista tres bloques con `top` crecientes y alturas que coinciden con lo dibujado

#### Scenario: Bloque omitido
- **WHEN** un bloque se omite por `when` falso o por texto vacío
- **THEN** no aparece en la lista

#### Scenario: Plantilla con marco
- **WHEN** la plantilla tiene marco
- **THEN** los valores de `top` incluyen el desplazamiento del marco

### Requirement: Errores de validación con bloque y propiedad
Cuando un error de validación o de renderizado se pueda atribuir a un bloque o a una propiedad, la respuesta 400 SHALL incluir `block` (número de bloque a partir de 1) y `property`, además del mensaje `error`.

#### Scenario: Propiedad fuera de rango
- **WHEN** el bloque 3 tiene `size` = 500
- **THEN** la respuesta incluye `"block": 3` y `"property": "size"`

#### Scenario: Error sin bloque
- **WHEN** el JSON no se puede interpretar
- **THEN** la respuesta lleva solo el mensaje, sin `block`

### Requirement: Borradores con imágenes
El servicio SHALL permitir crear un borrador (`POST /api/templates/drafts`, que devuelve un identificador generado por el servicio), subir y borrar sus imágenes (`PUT` y `DELETE /api/templates/drafts/{id}/assets/{archivo}`, PNG o JPEG de hasta 1 MB) y descartarlo (`DELETE /api/templates/drafts/{id}`). Las vistas previas con `draft` SHALL resolver los `source` de los bloques `image` primero en el borrador y luego en las imágenes de la plantilla guardada.

#### Scenario: Logo en una plantilla nueva
- **WHEN** se crea un borrador, se sube "logo.png" y se previsualiza una plantilla con un bloque `image` de `source` "logo.png"
- **THEN** la vista previa incluye el logo sin haber guardado la plantilla

#### Scenario: Identificador no válido
- **WHEN** se usa un identificador de borrador que no existe o que contiene una ruta
- **THEN** se rechaza sin acceder a ningún archivo fuera de la carpeta de borradores

#### Scenario: Imagen demasiado grande
- **WHEN** se sube una imagen de 3 MB a un borrador
- **THEN** se rechaza indicando el límite de 1 MB

### Requirement: Guardar una plantilla con las imágenes de su borrador
`PUT /api/templates/{nombre}?draft={id}` SHALL validar la plantilla teniendo en cuenta las imágenes del borrador, copiar a la plantilla las imágenes que use y borrar el borrador. Si la validación falla no SHALL copiarse nada ni borrarse el borrador.

#### Scenario: Guardar con logo
- **WHEN** se guarda `recibo` con `?draft=<id>` y un bloque `image` que usa "logo.png" del borrador
- **THEN** la plantilla queda guardada con su logo y el borrador desaparece

#### Scenario: Guardar con error
- **WHEN** la plantilla es inválida
- **THEN** se responde 400, el borrador sigue existiendo y la plantilla guardada no cambia

#### Scenario: Cancelar un borrador
- **WHEN** se descarta el borrador
- **THEN** sus imágenes se borran y la plantilla guardada, si existía, conserva sus imágenes

### Requirement: Limpieza y límites de los borradores
El servicio SHALL borrar los borradores de más de 24 horas al iniciar y al crear uno nuevo, y limitar a 20 los borradores simultáneos y a 10 MB cada uno.

#### Scenario: Borrador antiguo
- **WHEN** existe un borrador de hace más de 24 horas y se crea otro
- **THEN** el antiguo se elimina

#### Scenario: Demasiados borradores
- **WHEN** ya hay 20 borradores y se pide otro
- **THEN** se rechaza con un mensaje claro

