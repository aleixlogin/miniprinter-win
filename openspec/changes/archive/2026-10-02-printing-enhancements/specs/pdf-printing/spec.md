## ADDED Requirements

### Requirement: Aceptar PDF
El sistema SHALL aceptar documentos `application/pdf` en IPP (anunciándolo en `document-format-supported` y reconociendo la cabecera `%PDF-` cuando el formato llega como `application/octet-stream`), en la CLI (`miniprinter print archivo.pdf`) y en la API de automatización.

#### Scenario: PDF desde un móvil
- **WHEN** un cliente IPP envía un PDF de dos páginas
- **THEN** se imprimen las dos páginas y el trabajo termina `completed`

#### Scenario: PDF sin formato declarado
- **WHEN** llega un documento `application/octet-stream` que empieza por `%PDF-`
- **THEN** se trata como PDF

### Requirement: Rasterizado de PDF
El sistema SHALL rasterizar cada página del PDF con PDFium en escala de grises a 203 dpi, ajustando el ancho de la página al ancho imprimible con la misma lógica que el resto de documentos (recorte de márgenes laterales en blanco sin ampliar por encima del tamaño real), y procesar las páginas de una en una para no cargar el documento entero en memoria.

#### Scenario: PDF tamaño A4
- **WHEN** se imprime un PDF A4 con márgenes de 25 mm
- **THEN** el contenido se ajusta a los 48 mm de ancho, reduciéndose solo lo necesario

#### Scenario: PDF grande
- **WHEN** se imprime un PDF de 50 páginas
- **THEN** el uso de memoria no crece con el número de páginas y se pueden cancelar las páginas pendientes

### Requirement: Selección de páginas
El sistema SHALL admitir el atributo IPP `page-ranges` y la opción `--pages` de la CLI para imprimir solo algunas páginas.

#### Scenario: Rango de páginas
- **WHEN** se imprime un PDF de 10 páginas con `page-ranges` 2-3
- **THEN** solo se imprimen las páginas 2 y 3

### Requirement: PDF inválido o protegido
El sistema SHALL abortar el trabajo con un mensaje claro si el PDF está dañado o protegido con contraseña, sin afectar a los trabajos siguientes.

#### Scenario: PDF con contraseña
- **WHEN** se envía un PDF protegido con contraseña
- **THEN** el trabajo queda `aborted` con el mensaje "PDF protegido con contraseña" y la cola sigue con el siguiente trabajo
