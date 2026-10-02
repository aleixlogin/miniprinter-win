# print-templates Specification

## Purpose
TBD - created by archiving change printing-enhancements. Update Purpose after archive.
## Requirements
### Requirement: Catálogo de plantillas
El sistema SHALL ofrecer estas plantillas, generadas por el servicio directamente a 384 px y 1 bit (sin tramado):
- `qr`: código QR con texto opcional debajo,
- `barcode`: código de barras Code 128 o EAN-13 con su texto,
- `todo`: título y lista de tareas con casillas,
- `label`: etiqueta con título grande y una o dos líneas de texto,
- `sticker`: imagen ajustada al ancho con texto opcional.

#### Scenario: Lista de tareas
- **WHEN** se imprime la plantilla `todo` con título "Compra" y los elementos "Pan" y "Leche"
- **THEN** se imprime el título y dos líneas, cada una con una casilla vacía

#### Scenario: Plantilla sin campos obligatorios
- **WHEN** se pide la plantilla `qr` sin el campo `data`
- **THEN** se rechaza con un error que indica el campo que falta

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
La bandeja SHALL incluir una pestaña "Plantillas" con un formulario por plantilla, vista previa a tamaño real y botón Imprimir, y SHALL recordar los últimos valores usados.

#### Scenario: Vista previa
- **WHEN** el usuario rellena la plantilla `label`
- **THEN** la vista previa muestra el resultado exacto antes de imprimir

### Requirement: Plantillas desde la API y la CLI
Las plantillas SHALL poder imprimirse con `POST /api/v1/print/template/{nombre}` y con `miniprinter template <nombre> --campo valor`.

#### Scenario: Desde la CLI
- **WHEN** se ejecuta `miniprinter template qr --data "hola" --rfcomm <mac>`
- **THEN** se imprime el código QR

