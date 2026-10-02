# paper-sizes Specification

## Purpose
TBD - created by archiving change configurable-paper-sizes. Update Purpose after archive.
## Requirements
### Requirement: Lista configurable de tamaños de papel
El servicio SHALL mantener en sus ajustes una lista de tamaños de papel (identificador, nombre, ancho y largo en milímetros, activo o no) y un tamaño por defecto, y SHALL ofrecer a Windows por IPP únicamente los tamaños activos, con el tamaño por defecto como `media-default` y `media-ready`.

#### Scenario: Ofrecer solo los tamaños activos
- **WHEN** los ajustes tienen activos `48x100` y `48x210` y desactivados los demás
- **THEN** `media-supported` y `media-col-database` contienen solo esos dos tamaños

#### Scenario: Tamaño por defecto
- **WHEN** el tamaño por defecto es `48x100`
- **THEN** `media-default` y `media-ready` son `om_x5h-48x100mm_48x100mm` y ese tamaño aparece el primero

### Requirement: Preajustes de fábrica y compatibilidad
Los ajustes SHALL incluir como preajustes los siete tamaños actuales (`48x210`, `48x100`, `48x50`, `48x297`, `48x1000` "rollo", `80x297` y `80x100`). Sin lista de tamaños guardada, el servicio SHALL anunciar los siete preajustes activos y `48x210` como por defecto, igual que antes de este cambio. Un preajuste SHALL poder activarse o desactivarse pero no cambiar de medidas ni borrarse.

#### Scenario: Ajustes antiguos
- **WHEN** el servicio arranca con un `settings.json` sin lista de tamaños
- **THEN** anuncia los siete tamaños de siempre con `48x210` por defecto

#### Scenario: Medidas de un preajuste
- **WHEN** un `settings.json` cambia las medidas de `48x100`
- **THEN** el servicio conserva las medidas de fábrica y solo respeta si está activo

#### Scenario: Restaurar valores de fábrica
- **WHEN** se restauran los valores de fábrica
- **THEN** la lista vuelve a los siete preajustes activos y se eliminan los tamaños propios

### Requirement: Tamaños propios
El servicio SHALL aceptar tamaños propios con un nombre de hasta 40 caracteres, un ancho entero de 30 a 57 mm y un largo entero de 10 a 1000 mm. Dos tamaños no SHALL tener las mismas medidas ni el mismo identificador, y habrá como máximo 30 tamaños en total.

#### Scenario: Tamaño propio válido
- **WHEN** se añade un tamaño de 57 × 150 mm llamado "Ticket ancho"
- **THEN** se guarda con el identificador `c57x150` y se anuncia como `om_x5h-57x150mm_57x150mm`

#### Scenario: Ancho demasiado grande
- **WHEN** se intenta guardar un tamaño propio de 60 mm de ancho
- **THEN** el servicio lo corrige o lo rechaza indicando el máximo de 57 mm

#### Scenario: Medidas repetidas
- **WHEN** se añade un tamaño propio de 48 × 100 mm y ya existe ese preajuste
- **THEN** se rechaza indicando que las medidas ya existen

### Requirement: Siempre hay un tamaño activo y uno por defecto
Los ajustes SHALL tener siempre al menos un tamaño activo, y el tamaño por defecto SHALL ser uno de los activos. Si no lo es, el servicio SHALL usar el primer tamaño activo.

#### Scenario: Ninguno activo
- **WHEN** se guardan los ajustes con todos los tamaños desactivados
- **THEN** el servicio activa `48x210` y lo deja por defecto

#### Scenario: Por defecto desactivado
- **WHEN** el tamaño por defecto se desactiva
- **THEN** el primer tamaño activo pasa a ser el por defecto

### Requirement: Márgenes de los tamaños anchos
Para un tamaño propio de más de 48 mm de ancho, el servicio SHALL anunciar los márgenes izquierdo y derecho iguales a la mitad de lo que supera los 48 mm, y los márgenes superior e inferior a 0, de modo que Windows maquete a 48 mm dentro de la página. Los demás tamaños NO SHALL cambiar sus márgenes.

#### Scenario: Margen de un tamaño de 57 mm
- **WHEN** se anuncia un tamaño propio de 57 mm de ancho
- **THEN** sus márgenes izquierdo y derecho son 4,5 mm (450 centésimas)

#### Scenario: Tamaño de 48 mm
- **WHEN** se anuncia un tamaño de 48 mm
- **THEN** sus márgenes laterales son 0

### Requirement: Aplicar los cambios al listener IPP
Cuando cambie la lista de tamaños activos o el por defecto, el servicio SHALL reiniciar el listener IPP con la lista nueva cuando no haya trabajos en curso, sin perder los trabajos en la cola.

#### Scenario: Cambio con un trabajo en curso
- **WHEN** se cambian los tamaños mientras se imprime un trabajo
- **THEN** el trabajo termina y después el listener anuncia la lista nueva

#### Scenario: Consulta después del cambio
- **WHEN** Windows consulta los atributos de la impresora tras el reinicio
- **THEN** recibe la lista de tamaños nueva

### Requirement: Páginas más estrechas que el cabezal
Una página de resolución conocida más estrecha que el cabezal (48 mm a 203 dpi) SHALL imprimirse a tamaño real y centrada, sin ampliarla hasta el ancho del cabezal. Las imágenes sin resolución conocida SHALL seguir ajustándose al ancho del cabezal.

#### Scenario: Etiqueta de 40 mm
- **WHEN** una aplicación imprime una página de 40 mm de ancho a 203 dpi
- **THEN** el contenido se imprime con 40 mm de ancho, centrado en el papel, sin ampliarse

#### Scenario: Imagen sin resolución
- **WHEN** se imprime una imagen estrecha que no declara su resolución
- **THEN** se ajusta al ancho del cabezal como antes

#### Scenario: Página del ancho del cabezal
- **WHEN** la página mide 48 mm de ancho a 203 dpi
- **THEN** se imprime sin cambios

