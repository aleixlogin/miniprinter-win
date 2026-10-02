# quick-print Specification

## Purpose
TBD - created by archiving change printing-enhancements. Update Purpose after archive.
## Requirements
### Requirement: Renderizado de texto
El sistema SHALL convertir texto plano en un raster de 384 px de ancho con ajuste de línea por palabras, tamaño de letra configurable (10 pt por defecto, de 6 a 48 pt), tipografía configurable y soporte de caracteres Unicode (tildes, ñ, emoji básicos con fuente de reserva).

#### Scenario: Línea más larga que el papel
- **WHEN** se renderiza una línea que no cabe en 384 px
- **THEN** se parte por palabras en varias líneas sin cortar caracteres

#### Scenario: Caracteres en español
- **WHEN** se renderiza "Página ñandú ¿qué?"
- **THEN** todos los caracteres aparecen correctamente

### Requirement: Imprimir el portapapeles
La bandeja SHALL ofrecer "Imprimir portapapeles" en el menú del icono: si el portapapeles contiene una imagen, se imprime como imagen; si contiene texto, se imprime con el renderizado de texto; si está vacío o tiene otro formato, se informa sin imprimir.

#### Scenario: Imagen copiada
- **WHEN** el portapapeles contiene una captura de pantalla y el usuario elige "Imprimir portapapeles"
- **THEN** se crea un trabajo con esa imagen

#### Scenario: Portapapeles vacío
- **WHEN** el portapapeles no contiene texto ni imagen
- **THEN** se muestra una notificación "No hay nada que imprimir en el portapapeles" y no se crea ningún trabajo

### Requirement: Imprimir archivos soltados o enviados
El sistema SHALL imprimir archivos soltados sobre la ventana del panel (Windows no admite soltar archivos sobre un icono de la bandeja) y archivos enviados con la entrada "MiniPrinter" del menú "Enviar a" del Explorador, que el instalador SHALL crear. Se aceptan PNG, JPEG, PWG Raster, PDF (si `pdf-printing` está disponible) y TXT.

#### Scenario: Soltar una imagen en el panel
- **WHEN** el usuario suelta un PNG sobre la ventana del panel
- **THEN** se crea un trabajo con esa imagen

#### Scenario: Enviar a desde el Explorador
- **WHEN** el usuario elige "Enviar a > MiniPrinter" sobre dos archivos
- **THEN** se crea un trabajo por cada archivo, en el orden seleccionado

#### Scenario: Formato no admitido
- **WHEN** se suelta un archivo de un formato no admitido
- **THEN** se informa del formato no admitido y no se crea el trabajo

### Requirement: Nota rápida con atajo global
La bandeja SHALL registrar un atajo de teclado global configurable (Ctrl+Alt+P por defecto) que abre una ventana de "Nota rápida" con un cuadro de texto, selector de tamaño de letra, vista previa a 384 px e impresión con Ctrl+Enter.

#### Scenario: Escribir e imprimir
- **WHEN** el usuario pulsa Ctrl+Alt+P, escribe "Comprar leche" y pulsa Ctrl+Enter
- **THEN** se imprime la nota y la ventana se cierra

#### Scenario: Atajo ocupado
- **WHEN** otro programa ya tiene registrado el atajo configurado
- **THEN** la bandeja avisa de que no pudo registrarlo y permite elegir otro en los ajustes

