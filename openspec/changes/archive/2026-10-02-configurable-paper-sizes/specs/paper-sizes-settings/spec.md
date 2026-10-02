## ADDED Requirements

### Requirement: Apartado "Papel que se muestra a Windows" en Ajustes
La pestaña *Ajustes* de la bandeja SHALL incluir un apartado "Papel que se muestra a Windows" con la lista de tamaños (casilla de activo, nombre, medidas y marca del tamaño por defecto) y los botones Añadir, Editar, Borrar y Restaurar valores de fábrica.

#### Scenario: Ver los tamaños
- **WHEN** el usuario abre *Ajustes*
- **THEN** ve los siete tamaños de fábrica con su casilla y el de 48 × 210 marcado como por defecto

#### Scenario: Activar y desactivar
- **WHEN** el usuario desmarca el tamaño 48 × 297 y guarda
- **THEN** ese tamaño deja de ofrecerse a Windows

### Requirement: Tamaños propios en la bandeja
El usuario SHALL poder añadir un tamaño propio indicando nombre, ancho (30–57 mm) y largo (10–1000 mm), con validación en línea, y SHALL poder editarlo o borrarlo. El diálogo SHALL mostrar los márgenes laterales que se anunciarán cuando el ancho supere los 48 mm. Los preajustes SHALL poder activarse o desactivarse pero no editarse ni borrarse.

#### Scenario: Añadir un tamaño
- **WHEN** el usuario añade "Etiqueta" de 40 × 60 mm
- **THEN** aparece en la lista, activo, y se anunciará a Windows al guardar

#### Scenario: Ancho fuera de rango
- **WHEN** el usuario escribe un ancho de 60 mm
- **THEN** el diálogo lo marca como no válido indicando el máximo de 57 mm y no deja aceptar

#### Scenario: Margen de un tamaño ancho
- **WHEN** el usuario escribe un ancho de 57 mm
- **THEN** el diálogo indica que se anunciarán márgenes laterales de 4,5 mm

#### Scenario: Preajuste no editable
- **WHEN** el usuario selecciona un tamaño de fábrica
- **THEN** Editar y Borrar están desactivados

### Requirement: Reglas de la lista en la interfaz
La bandeja NO SHALL permitir desmarcar el último tamaño activo ni borrar o desactivar el tamaño por defecto sin elegir otro. SHALL permitir elegir cuál de los activos es el por defecto.

#### Scenario: Último tamaño activo
- **WHEN** solo queda un tamaño activo
- **THEN** su casilla no se puede desmarcar

#### Scenario: Borrar el tamaño por defecto
- **WHEN** el usuario intenta borrar el tamaño propio que es el por defecto
- **THEN** se le pide elegir antes otro tamaño por defecto

### Requirement: Confirmación antes de recrear la impresora
Al guardar los ajustes con la lista de tamaños activos, el tamaño por defecto o el nombre de la impresora cambiados, la bandeja SHALL mostrar una confirmación que explique que se recreará la impresora de Windows y se perderán las preferencias de impresión, con las opciones Continuar y Guardar sin recrear. Con Guardar sin recrear, los ajustes se guardan pero Windows conserva los tamaños antiguos.

#### Scenario: Continuar
- **WHEN** el usuario cambia los tamaños, guarda y elige Continuar
- **THEN** se guardan los ajustes y se recrea la impresora de Windows

#### Scenario: Guardar sin recrear
- **WHEN** el usuario elige Guardar sin recrear
- **THEN** los ajustes se guardan y la impresora de Windows no se toca

#### Scenario: Otros ajustes
- **WHEN** el usuario cambia otro ajuste (por ejemplo, la oscuridad)
- **THEN** no se pide confirmación ni se recrea nada

### Requirement: Progreso y resultado de la recreación
La bandeja SHALL mostrar el progreso de la recreación y su resultado (correcto o el motivo del fallo), y SHALL incluir un botón "Recrear impresora de Windows" para pedirla en cualquier momento, con la misma confirmación.

#### Scenario: Resultado correcto
- **WHEN** la recreación termina bien
- **THEN** la bandeja indica que la impresora de Windows se ha actualizado

#### Scenario: Fallo
- **WHEN** la recreación falla
- **THEN** la bandeja muestra el motivo, indica que la impresora anterior sigue funcionando y deja el botón disponible para reintentar

#### Scenario: Reparar una cola borrada
- **WHEN** la cola se borró a mano y el usuario pulsa "Recrear impresora de Windows"
- **THEN** la cola se vuelve a crear

### Requirement: Conservar la impresora predeterminada
Antes de recrear la impresora, la bandeja SHALL comprobar si era la impresora predeterminada del usuario y, si lo era, SHALL volver a marcarla como predeterminada al terminar. Si no lo era, NO SHALL cambiar la predeterminada.

#### Scenario: Era la predeterminada
- **WHEN** la impresora era la predeterminada y se recrea
- **THEN** al terminar vuelve a ser la predeterminada

#### Scenario: No lo era
- **WHEN** otra impresora era la predeterminada
- **THEN** sigue siéndolo después de recrear la cola
