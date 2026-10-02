# template-blocks Specification

## Purpose
TBD - created by archiving change user-templates. Update Purpose after archive.
## Requirements
### Requirement: Bloques disponibles
El motor de plantillas SHALL ofrecer los bloques `text`, `qr`, `barcode`, `image`, `line`, `spacer`, `columns` y `list`, apilados verticalmente al ancho del cabezal (384 px) y en 1 bit sin tramado, salvo `image`, que usa tramado.

#### Scenario: Apilado
- **WHEN** una plantilla tiene un `text`, una `line` y un `qr`
- **THEN** se imprimen en ese orden, de arriba abajo, con un hueco entre ellos

### Requirement: Bloque de texto
El bloque `text` SHALL admitir `size` (puntos), `bold`, `align` (`left`, `center`, `right`) y ajuste de línea por palabras al ancho disponible.

#### Scenario: Texto centrado y en negrita
- **WHEN** un bloque `text` tiene `bold` y `align` = `center`
- **THEN** el texto se imprime en negrita y centrado

#### Scenario: Texto largo
- **WHEN** el texto no cabe en una línea
- **THEN** se parte por palabras en varias líneas sin cortarse por el borde

### Requirement: Línea y espaciador
El bloque `line` SHALL dibujar una raya horizontal continua o punteada (`style`) de grosor configurable, y el bloque `spacer` SHALL añadir una altura en blanco indicada en milímetros.

#### Scenario: Línea punteada
- **WHEN** un bloque `line` tiene `style` = `dotted`
- **THEN** se imprime una raya de puntos a todo el ancho útil

#### Scenario: Espaciador
- **WHEN** un bloque `spacer` tiene `mm` = 5
- **THEN** se añaden 40 filas en blanco

### Requirement: Columnas con relleno
El bloque `columns` SHALL colocar un texto a la izquierda y otro a la derecha en la misma fila y, con `leader` = `dots`, rellenar el espacio entre ambos con puntos. Si los dos textos no caben, el izquierdo SHALL partirse en varias líneas y el derecho quedarse en la última.

#### Scenario: Línea de tique
- **WHEN** un bloque `columns` tiene izquierda "Café" y derecha "1,50" con `leader` = `dots`
- **THEN** se imprime "Café ........ 1,50" con el precio alineado a la derecha

#### Scenario: Texto izquierdo largo
- **WHEN** el texto izquierdo no cabe junto al derecho
- **THEN** el izquierdo se parte en varias líneas y el derecho aparece en la última

### Requirement: Lista
El bloque `list` SHALL imprimir una fila por cada línea no vacía de un campo multilínea, con un marcador configurable (`box`, `bullet` o `number`) y tamaño de letra configurable. Una lista sin elementos SHALL rechazarse.

#### Scenario: Casillas
- **WHEN** un bloque `list` con `marker` = `box` recibe "Pan" y "Leche"
- **THEN** se imprimen dos filas, cada una con una casilla vacía

#### Scenario: Numerada
- **WHEN** `marker` = `number` y hay tres elementos
- **THEN** las filas se numeran 1, 2 y 3

#### Scenario: Lista vacía
- **WHEN** el campo de la lista está vacío
- **THEN** la petición se rechaza con el mensaje "La lista no tiene tareas."

### Requirement: Imagen y logo fijo
El bloque `image` SHALL admitir una imagen recibida en un campo (Base64) o un archivo guardado junto a la plantilla (`source`, PNG o JPEG, hasta 1 MB), ajustada al ancho con tramado.

#### Scenario: Logo de la plantilla
- **WHEN** una plantilla tiene un bloque `image` con `source` = "logo.png" y ese archivo existe en los recursos de la plantilla
- **THEN** el logo se imprime sin que el usuario tenga que enviar la imagen

#### Scenario: Logo inexistente
- **WHEN** `source` apunta a un archivo que no existe
- **THEN** la plantilla se rechaza al validarla indicando el archivo

### Requirement: Opciones de QR
El bloque `qr` SHALL admitir `ecc` (`L`, `M`, `Q`, `H`; por defecto `M`) y `module` (puntos por módulo; por defecto el mayor que quepa), manteniendo el mínimo de 4 puntos y la zona de silencio de 4 módulos.

#### Scenario: Corrección alta
- **WHEN** se pide un QR con `ecc` = `H`
- **THEN** el código se genera con ese nivel de corrección

#### Scenario: Módulo por debajo del mínimo
- **WHEN** se pide `module` = 2
- **THEN** la petición se rechaza indicando el mínimo de 4 puntos

### Requirement: Opciones de código de barras
El bloque `barcode` SHALL admitir los formatos `code128`, `code39`, `ean13` y `upca` y una altura configurable en milímetros (por defecto 12), validando los dígitos de control de EAN‑13 y UPC‑A.

#### Scenario: UPC‑A válido
- **WHEN** se pide un UPC‑A de 11 dígitos
- **THEN** se calcula el dígito de control y se imprime el código de 12 dígitos

#### Scenario: Code 39 con carácter no admitido
- **WHEN** se pide Code 39 con un carácter fuera de su alfabeto
- **THEN** la petición se rechaza indicando el carácter

#### Scenario: Altura personalizada
- **WHEN** se indica `height` = 20
- **THEN** las barras miden 20 mm de alto

### Requirement: Opciones de etiqueta
La plantilla `label` SHALL admitir los campos opcionales `size` (puntos del título), `align` (`left`, `center`, `right`) y `border` (sí/no, con un marco alrededor), con valores por defecto iguales al comportamiento anterior.

#### Scenario: Etiqueta con marco
- **WHEN** se imprime `label` con `border` activado
- **THEN** se dibuja un marco alrededor del contenido

#### Scenario: Sin opciones
- **WHEN** se imprime `label` sin los campos nuevos
- **THEN** el resultado es idéntico al anterior a este cambio

### Requirement: Plantillas integradas nuevas
El sistema SHALL incluir las plantillas `shopping` (lista de la compra con cantidades), `wifi` (QR de acceso Wi‑Fi con SSID, contraseña y tipo de seguridad), `contact` (QR vCard con nombre, teléfono y correo), `cable` (etiqueta de cable plegable con el mismo texto repetido), `receipt` (tique con líneas, total, fecha y número), `bookmark` (marcador de lectura) y `countdown` (cuenta atrás hasta una fecha).

#### Scenario: Wi‑Fi
- **WHEN** se imprime `wifi` con SSID "Casa" y contraseña "secreto" y seguridad WPA
- **THEN** se imprime un QR con el contenido `WIFI:T:WPA;S:Casa;P:secreto;;` y el nombre de la red debajo

#### Scenario: Caracteres especiales en Wi‑Fi
- **WHEN** la contraseña contiene `;` o `\`
- **THEN** se escapan según el formato de QR de Wi‑Fi

#### Scenario: Cuenta atrás
- **WHEN** se imprime `countdown` con la fecha de dentro de 10 días
- **THEN** se imprime "10 días" y la fecha de destino

#### Scenario: Cuenta atrás con fecha pasada
- **WHEN** la fecha es anterior a hoy
- **THEN** se rechaza indicando que la fecha ya pasó

#### Scenario: Tique
- **WHEN** se imprime `receipt` con dos líneas y un total
- **THEN** se imprimen las líneas con precios alineados, una raya y el total, con la fecha y el número consecutivo

