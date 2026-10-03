# escpos-emulation Specification

## Purpose
TBD - created by archiving change raw-print-port. Update Purpose after archive.
## Requirements
### Requirement: Maquetación por celdas
El intérprete ESC/POS SHALL dibujar el texto en una rejilla de celdas de ancho fijo sobre 384 puntos: 32 columnas de 12 × 24 puntos con la fuente A y 42 columnas con la fuente B, de modo que las columnas de un tique queden alineadas. Cada línea SHALL cerrarse con `LF` o al llenarse, y el tique SHALL ser una sola página continua.

#### Scenario: Tique de dos columnas
- **WHEN** se imprime una línea "Cafe" y "1,50" separadas por espacios hasta ocupar 32 columnas
- **THEN** el precio queda alineado al borde derecho

#### Scenario: Línea larga
- **WHEN** una línea supera las 32 columnas
- **THEN** continúa en la línea siguiente

#### Scenario: Fuente B
- **WHEN** se selecciona la fuente B (`ESC M 1`)
- **THEN** caben 42 columnas por línea

### Requirement: Texto y juegos de caracteres
El intérprete SHALL decodificar el texto con la tabla activa (`ESC t n`: CP437 por defecto y las tablas del perfil por defecto de `python-escpos`, como CP850, CP1252, CP858 o ISO 8859-7) y, si un tramo de texto entre dos órdenes es UTF-8 válido con bytes altos, como UTF-8. Los caracteres que la fuente no tenga SHALL sustituirse por `?`.

#### Scenario: Tabla CP858 con euro
- **WHEN** se selecciona `ESC t 19` y se envía el byte del euro
- **THEN** se imprime «€»

#### Scenario: UTF-8
- **WHEN** el flujo contiene «ñ» y «€» codificados en UTF-8
- **THEN** se imprimen correctamente

#### Scenario: Carácter inexistente
- **WHEN** el texto contiene un carácter que la fuente no puede dibujar
- **THEN** se imprime `?` en su lugar

### Requirement: Estilos y alineación
El intérprete SHALL aplicar negrita (`ESC E`), subrayado (`ESC -`), tamaño de 1 a 8 veces en ancho y alto (`GS !`), inverso (`GS B`) y alineación izquierda, centro o derecha (`ESC a`), y `ESC @` SHALL restablecer todo.

#### Scenario: Tamaño doble
- **WHEN** se envía `GS ! 0x11` y un texto
- **THEN** el texto se imprime al doble de ancho y de alto

#### Scenario: Inverso
- **WHEN** se activa `GS B 1`
- **THEN** el texto sale en blanco sobre fondo negro

#### Scenario: Centrado
- **WHEN** se envía `ESC a 1` y una línea
- **THEN** la línea queda centrada

#### Scenario: Reinicio
- **WHEN** se envía `ESC @` tras activar negrita y tamaño doble
- **THEN** el texto siguiente sale con el estilo por defecto

### Requirement: Avance y corte
`ESC d n` SHALL avanzar `n` líneas y `ESC J n` `n` puntos. `GS V` SHALL marcar el fin del tique, con un avance final de papel, y el avance sin contenido al final SHALL recortarse.

#### Scenario: Avance
- **WHEN** se envía `ESC d 3`
- **THEN** se añaden tres líneas en blanco

#### Scenario: Corte
- **WHEN** se envía `GS V 66 3`
- **THEN** el tique termina y se imprime con el avance final

### Requirement: Imágenes
El intérprete SHALL imprimir imágenes raster (`GS v 0`, modos normal y doble) e imágenes de bits (`ESC *`, 8 y 24 puntos) respetando su ancho y su alineación, recortadas a 384 puntos si lo superan.

#### Scenario: Logo raster
- **WHEN** se envía una imagen `GS v 0` de 256 × 100 puntos
- **THEN** se imprime con esa anchura y altura

#### Scenario: Imagen demasiado ancha
- **WHEN** la imagen mide más de 384 puntos
- **THEN** se recorta a 384 sin fallar

### Requirement: Códigos QR y de barras
El intérprete SHALL imprimir códigos QR (`GS ( k`, con módulo, corrección de errores y datos) y de barras (`GS k`: UPC-A, EAN-13, Code 39 y Code 128, en las dos variantes de la orden), con texto legible según `GS H`, altura `GS h` y módulo `GS w`. El resultado SHALL poder leerse con la cámara de un móvil.

#### Scenario: QR
- **WHEN** se envían las órdenes de QR con el texto "https://example.com" y la orden de imprimir
- **THEN** se imprime un QR que decodifica ese texto

#### Scenario: EAN-13
- **WHEN** se envía `GS k 2` con 12 dígitos
- **THEN** se imprime el EAN-13 con su dígito de control

#### Scenario: Code 128
- **WHEN** se envía `GS k 73` con una longitud y un texto
- **THEN** se imprime un Code 128 legible

#### Scenario: QR demasiado grande
- **WHEN** el módulo pedido no cabe en 384 puntos
- **THEN** se reduce hasta que quepa, con un mínimo de 3 puntos por módulo, y si no cabe se omite sin fallar

### Requirement: Respuestas de estado
El servicio SHALL responder por la misma conexión a `DLE EOT n` (n = 1 a 4) y `GS r 1` con un byte de estado calculado del estado actual de la impresora (en línea, tapa abierta o sin papel), sin esperar a la cola de trabajos.

#### Scenario: Con papel
- **WHEN** el cliente envía `DLE EOT 4` y hay papel
- **THEN** recibe `0x12`

#### Scenario: Sin papel
- **WHEN** el cliente envía `DLE EOT 4` y la impresora no tiene papel
- **THEN** recibe `0x72`

#### Scenario: Estado de papel por `GS r`
- **WHEN** el cliente envía `GS r 1` sin papel
- **THEN** recibe `0x0C`

#### Scenario: Impresora desconectada
- **WHEN** no hay estado disponible
- **THEN** responde `0x12` sin esperar

### Requirement: Órdenes desconocidas
Una orden ESC/POS desconocida NO SHALL interpretarse como texto: se descarta su prefijo y se sigue con el resto, registrándola una sola vez en el registro.

#### Scenario: Orden no soportada
- **WHEN** el flujo contiene una orden que el intérprete no conoce entre dos líneas de texto
- **THEN** las dos líneas se imprimen y la orden no aparece como caracteres

#### Scenario: Órdenes que se ignoran
- **WHEN** el flujo contiene `ESC p` (gaveta)
- **THEN** se ignora sin afectar al tique

### Requirement: Fixtures reales
El intérprete SHALL verificarse con tiques ESC/POS reales generados por una biblioteca externa (`python-escpos`) y guardados como archivos de bytes con su raster de referencia.

#### Scenario: Tique de ejemplo
- **WHEN** se interpreta la fixture de un tique con cabecera, líneas, total, QR y corte
- **THEN** el raster coincide con el PNG de referencia

### Requirement: Otros códigos 2D
El intérprete SHALL imprimir también PDF417, Aztec y Data Matrix con las funciones `GS ( k` de almacenar datos, tamaño de módulo e imprimir (`cn` 48, 53 y 54), con una zona de silencio de dos módulos, reduciendo el módulo hasta 2 puntos si no cabe en 384 y omitiendo el código (sin fallar) si aun así no cabe o los datos no se pueden codificar.

#### Scenario: Data Matrix
- **WHEN** se almacena "MINIPRINTER-0042" con `cn` 54 y se envía la orden de imprimir
- **THEN** se imprime un Data Matrix que decodifica ese texto

#### Scenario: PDF417 y Aztec
- **WHEN** se almacena un texto con `cn` 48 o 53 y se imprime
- **THEN** el código impreso decodifica ese texto

#### Scenario: Código imposible
- **WHEN** los datos son demasiado largos para el código pedido
- **THEN** se omite y el resto del tique se imprime

### Requirement: Presupuesto de papel por conexión
El intérprete SHALL aceptar como máximo 160 000 puntos de papel (unos 20 m, avances incluidos) por conexión; al superarlo SHALL publicar lo ya maquetado e ignorar el resto del flujo, salvo las consultas de estado. Una orden con imagen de más de 4 MB declarados SHALL descartarse sin almacenarla, y las imágenes más anchas que el cabezal SHALL recortarse al leerlas.

#### Scenario: Caracteres enormes en masa
- **WHEN** un cliente envía megabytes de texto a tamaño 8×8
- **THEN** el servicio imprime como mucho el presupuesto, cierra la conexión y sigue funcionando

#### Scenario: Avances sin fin
- **WHEN** un cliente envía millones de `ESC d 255`
- **THEN** no se asigna memoria por cada avance y el flujo se procesa en menos de un segundo

#### Scenario: Imagen que declara gigabytes
- **WHEN** una orden `GS v 0` declara 65535 × 65535 bytes
- **THEN** se descarta sin desbordar ni reservar memoria

### Requirement: Caracteres de ancho completo
El intérprete SHALL dibujar los caracteres de ancho completo (ideogramas, kana, Hangul, formas de ancho completo y emoji) en dos celdas, con una fuente del sistema elegida según el texto (japonesa si el tramo tiene kana, coreana si tiene Hangul, china en otro caso) y los símbolos y emoji con una fuente de símbolos. Un carácter que ninguna fuente tenga SHALL imprimirse como `?` de una celda. Los katakana de ancho medio SHALL ocupar una celda.

#### Scenario: Japonés
- **WHEN** se envía "こんにちは世界" en UTF-8
- **THEN** se imprime con siete caracteres de dos celdas, sin signos de interrogación

#### Scenario: Línea con celdas mixtas
- **WHEN** una línea mezcla letras latinas y caracteres de ancho completo
- **THEN** cada carácter ancho avanza 24 puntos, la línea continúa en la siguiente al llenarse y las columnas latinas no se desalinean

#### Scenario: Carácter inexistente
- **WHEN** el carácter no está en ninguna fuente
- **THEN** se imprime `?` en una celda

### Requirement: Modo kanji
`FS &` SHALL activar el modo kanji y `FS .` desactivarlo (también `ESC @`); con el modo activo los bytes SHALL decodificarse con la página de códigos CJK del intérprete (Shift-JIS por defecto, configurable a GBK, Big5 o EUC-KR). La tabla `ESC t 1` SHALL ser CP932.

#### Scenario: Shift-JIS en modo kanji
- **WHEN** se envía `FS &`, los bytes Shift-JIS de «日本語» y `FS .`
- **THEN** se imprime «日本語»

#### Scenario: Katakana por la tabla 1
- **WHEN** se selecciona `ESC t 1` y se envían los bytes de «ｱｲｳ»
- **THEN** se imprimen los tres katakana de ancho medio

#### Scenario: Salir del modo kanji
- **WHEN** se envía `FS .` y después texto ASCII
- **THEN** el texto se imprime como ASCII

### Requirement: Composición y caracteres de ancho cero
El intérprete SHALL normalizar cada tramo de texto a NFC para componer los caracteres combinados, y los caracteres de ancho cero SHALL no ocupar celda.

#### Scenario: Letra con acento combinado
- **WHEN** se envía "e" seguido del acento agudo combinado
- **THEN** se imprime una sola «é»

#### Scenario: Espacio de ancho cero
- **WHEN** el texto contiene `U+200B` entre dos letras
- **THEN** las dos letras quedan en celdas contiguas

### Requirement: Hebreo en orden visual
El intérprete SHALL poner los tramos de hebreo, y los espacios y signos entre sus palabras, en orden visual (de derecha a izquierda) antes de dibujarlos celda a celda, sin tocar el texto latino que los rodea.

#### Scenario: Palabra hebrea
- **WHEN** se envía "שלום" en UTF-8
- **THEN** se imprime con la letra final a la izquierda, en orden de lectura de derecha a izquierda

#### Scenario: Texto mezclado
- **WHEN** una línea mezcla palabras latinas y hebreas
- **THEN** el texto latino conserva su posición y cada tramo hebreo se invierte por separado

### Requirement: Árabe y tailandés con formas contextuales
Un tramo continuo de árabe o de tailandés SHALL dibujarse como un solo bloque, de modo que las letras del árabe se unan según su posición (con las ligaduras como lam-alef) y se lean de derecha a izquierda, y las vocales y tonos del tailandés queden sobre o bajo su consonante. El bloque SHALL ocupar un número entero de celdas y partirse por palabras si no cabe en el cabezal.

#### Scenario: Árabe unido
- **WHEN** se envía "مرحبا بالعالم" en UTF-8
- **THEN** se imprimen las palabras con sus letras unidas y de derecha a izquierda, no como letras sueltas

#### Scenario: Tailandés con marcas
- **WHEN** se envía "สวัสดี" en UTF-8
- **THEN** la vocal y el tono se imprimen sobre su consonante

#### Scenario: Línea larga en árabe
- **WHEN** un texto árabe es más ancho que 384 puntos
- **THEN** se parte por palabras y cada línea queda alineada a la derecha

#### Scenario: Texto latino alrededor
- **WHEN** una línea mezcla "AR:" y un tramo árabe
- **THEN** el texto latino conserva su posición en celdas fijas y el bloque árabe empieza justo detrás

