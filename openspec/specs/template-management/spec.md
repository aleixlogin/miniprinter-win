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

