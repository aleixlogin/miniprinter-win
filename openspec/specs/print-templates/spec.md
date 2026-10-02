# print-templates Specification

## Purpose
TBD - created by archiving change printing-enhancements. Update Purpose after archive.
## Requirements
### Requirement: Catálogo de plantillas
El sistema SHALL ofrecer como mínimo estas plantillas integradas, generadas por el servicio directamente a 384 px y 1 bit (sin tramado) con el motor de layouts, además de las plantillas definidas por el usuario:
- `qr`: código QR con texto opcional debajo,
- `barcode`: código de barras Code 128, Code 39, EAN-13 o UPC-A con su texto,
- `todo`: título y lista de tareas con casillas, viñetas o números,
- `label`: etiqueta con título grande y una o dos líneas de texto, con tamaño, alineación y marco opcionales,
- `sticker`: imagen ajustada al ancho con texto opcional,
- `shopping`, `wifi`, `contact`, `cable`, `receipt`, `bookmark` y `countdown`, definidas en `template-blocks`.

#### Scenario: Lista de tareas
- **WHEN** se imprime la plantilla `todo` con título "Compra" y los elementos "Pan" y "Leche"
- **THEN** se imprime el título y dos líneas, cada una con una casilla vacía

#### Scenario: Plantilla sin campos obligatorios
- **WHEN** se pide la plantilla `qr` sin el campo `data`
- **THEN** se rechaza con un error que indica el campo que falta

#### Scenario: Plantilla de usuario en el catálogo
- **WHEN** existe una plantilla de usuario válida
- **THEN** aparece en el mismo catálogo que las integradas y se imprime con los mismos endpoints y comandos

#### Scenario: Plantilla desconocida
- **WHEN** se pide una plantilla que no existe
- **THEN** se rechaza con un error que lista las plantillas disponibles

### Requirement: Códigos legibles
Los códigos QR SHALL usar módulos de al menos 4 puntos y una zona de silencio de 4 módulos, y los códigos de barras barras de al menos 2 puntos, para que se puedan leer con un móvil. Si el contenido no cabe en 384 px con esos mínimos, la petición SHALL rechazarse.

#### Scenario: QR de una URL
- **WHEN** se imprime un QR con "https://example.com"
- **THEN** el código impreso se puede escanear con la cámara de un móvil

#### Scenario: Contenido demasiado largo
- **WHEN** se pide un QR con un texto que no cabe con módulos de 4 puntos
- **THEN** la petición se rechaza con el mensaje "El contenido es demasiado largo para el ancho del papel"

#### Scenario: EAN-13 inválido
- **WHEN** se pide un EAN-13 con un dígito de control incorrecto
- **THEN** la petición se rechaza indicando el dígito de control esperado

### Requirement: Plantillas desde la bandeja
La bandeja SHALL incluir una pestaña "Plantillas" con un formulario por plantilla, vista previa a tamaño real que se actualiza mientras se escribe, botón Imprimir con número de copias, carga de un CSV para imprimir lotes y favoritos con nombre, y SHALL recordar los últimos valores usados. La pestaña SHALL incluir también los botones Crear, Editar y Eliminar para gestionar las plantillas de usuario con el editor.

#### Scenario: Vista previa
- **WHEN** el usuario rellena la plantilla `label`
- **THEN** la vista previa muestra el resultado exacto antes de imprimir, sin pulsar ningún botón

#### Scenario: Copias
- **WHEN** el usuario indica 3 copias y pulsa Imprimir
- **THEN** se imprimen tres copias en un solo trabajo

#### Scenario: Gestión de plantillas
- **WHEN** el usuario abre la pestaña *Plantillas*
- **THEN** ve los botones Crear, Editar y Eliminar junto al selector de plantillas

### Requirement: Plantillas desde la API y la CLI
Las plantillas SHALL poder imprimirse con `POST /api/v1/print/template/{nombre}` (campos en el cuerpo, más `copies` y `rows` opcionales) y con `miniprinter template <nombre> --campo valor [--copies N] [--csv archivo]`.

#### Scenario: Desde la CLI
- **WHEN** se ejecuta `miniprinter template qr --data "hola" --rfcomm <mac>`
- **THEN** se imprime el código QR

#### Scenario: Con copias desde la API
- **WHEN** se envía `{"data":"hola","copies":2}` a `/api/v1/print/template/qr`
- **THEN** se imprimen dos copias del código QR

### Requirement: Vistas previas con las filas de cada bloque
Las vistas previas de plantillas SHALL informar de las filas que ocupa cada bloque mediante la cabecera `X-Template-Blocks`, sin cambiar el contenido PNG de la respuesta.

#### Scenario: Cliente existente
- **WHEN** un cliente que ignora la cabecera pide la vista previa de `qr`
- **THEN** recibe el mismo PNG que antes

