# template-editor Specification

## Purpose
TBD - created by archiving change template-editor. Update Purpose after archive.
## Requirements
### Requirement: Ventana del editor
Crear y Editar SHALL abrir una ventana nueva y modal con el editor de la plantilla, formada por la lista de bloques, la vista previa a tamaño real y el panel de propiedades, además de las propiedades de la plantilla, los campos, los datos de prueba y una pestaña con el JSON en crudo.

#### Scenario: Abrir el editor para editar
- **WHEN** el usuario elige una plantilla de usuario y pulsa Editar
- **THEN** se abre la ventana con sus bloques, sus campos y la vista previa

#### Scenario: Abrir el editor para crear
- **WHEN** el usuario pulsa Crear y elige "En blanco" con el nombre "recibo"
- **THEN** se abre la ventana con una plantilla vacía llamada `recibo` y un bloque de texto inicial

### Requirement: Lista de bloques
El editor SHALL permitir añadir un bloque de cualquier tipo del esquema, borrarlo, duplicarlo y reordenarlo arrastrando, con botones de subir y bajar como alternativa. Al llegar a 100 bloques SHALL desactivar Añadir.

#### Scenario: Reordenar arrastrando
- **WHEN** el usuario arrastra el bloque 3 sobre la posición 1
- **THEN** pasa a ser el primero y la vista previa se actualiza

#### Scenario: Duplicar
- **WHEN** el usuario duplica un bloque con sus propiedades
- **THEN** aparece una copia justo debajo con las mismas propiedades

#### Scenario: Límite de bloques
- **WHEN** la plantilla ya tiene 100 bloques
- **THEN** el botón Añadir está desactivado

### Requirement: Vista previa en vivo con selección por clic
El editor SHALL mostrar el resultado exacto a tamaño real, actualizado 400 ms después del último cambio y mostrando siempre la respuesta de la petición más reciente. Un clic sobre la vista previa SHALL seleccionar el bloque que ocupa esa posición y el bloque seleccionado SHALL resaltarse.

#### Scenario: Cambiar una propiedad
- **WHEN** el usuario cambia el tamaño de un texto
- **THEN** la vista previa se actualiza sin pulsar nada

#### Scenario: Clic en la vista previa
- **WHEN** el usuario hace clic sobre el código QR de la vista previa
- **THEN** se selecciona el bloque `qr` en la lista y se muestran sus propiedades

#### Scenario: Respuestas fuera de orden
- **WHEN** llega una respuesta lenta después de una más reciente
- **THEN** se descarta

### Requirement: Propiedades generadas desde el esquema
El panel de propiedades SHALL crear un control por propiedad según el esquema del servicio: casilla para booleanos, lista desplegable para opciones, número con su rango y cuadro de texto (multilínea cuando corresponda) para el resto, e incluir la propiedad común `when`.

#### Scenario: Control de un enum
- **WHEN** se selecciona un bloque `line`
- **THEN** su propiedad `style` aparece como lista desplegable con `solid`, `dotted` y `dashed`

#### Scenario: Rango numérico
- **WHEN** el usuario escribe un tamaño fuera de rango
- **THEN** el control lo marca como inválido y el mensaje del servicio aparece junto a él

### Requirement: Insertar marcadores
En cualquier propiedad de texto el editor SHALL ofrecer insertar `{{campo}}` de los campos declarados, `{{now}}` y `{{counter}}` en la posición del cursor, y los filtros del esquema.

#### Scenario: Insertar un campo
- **WHEN** el usuario coloca el cursor en el valor de un texto y elige el campo `cliente`
- **THEN** se inserta `{{cliente}}` en esa posición

### Requirement: Campos de la plantilla
El editor SHALL permitir añadir, editar y borrar los campos de la plantilla (nombre, etiqueta, tipo, obligatorio, valor por defecto y opciones). Al borrar un campo cuyo marcador se use en algún bloque SHALL avisar indicando los bloques afectados y pedir confirmación.

#### Scenario: Borrar un campo en uso
- **WHEN** el usuario borra `cliente` y los bloques 2 y 4 usan `{{cliente}}`
- **THEN** se avisa de que afecta a los bloques 2 y 4 y se pide confirmación

#### Scenario: Nombre de campo repetido
- **WHEN** el usuario añade un campo con un nombre que ya existe
- **THEN** se rechaza indicando que está duplicado

### Requirement: Datos de prueba
El editor SHALL ofrecer un formulario con los campos declarados para rellenarlos solo con fines de vista previa, sin guardarlos en la plantilla, y SHALL dar a cada campo un valor inicial útil (su valor por defecto o un ejemplo según el tipo).

#### Scenario: Probar con datos
- **WHEN** el usuario escribe "Ana" en el dato de prueba `cliente`
- **THEN** la vista previa muestra "Ana" donde esté `{{cliente}}`

#### Scenario: Los datos de prueba no se guardan
- **WHEN** se guarda la plantilla
- **THEN** el JSON guardado no contiene los datos de prueba

### Requirement: Propiedades de la plantilla
El editor SHALL permitir cambiar el título, la descripción, el modo (`text` o `image`), el hueco entre bloques y el marco de la plantilla. El nombre de una plantilla existente NO SHALL poder cambiarse; para usar otro nombre se SHALL ofrecer *Guardar como…*.

#### Scenario: Hueco entre bloques
- **WHEN** el usuario cambia el hueco a 20
- **THEN** la vista previa separa más los bloques

#### Scenario: Guardar como
- **WHEN** el usuario elige *Guardar como…* con el nombre "recibo2"
- **THEN** se crea una plantilla nueva `recibo2` y la original no cambia

### Requirement: JSON en crudo sincronizado
El editor SHALL incluir una pestaña con el JSON de la plantilla, editable. Si el texto es válido SHALL sustituir al modelo y refrescar todos los paneles; si no, SHALL mostrar el error y conservar el último modelo válido sin descartar lo escrito.

#### Scenario: Editar el JSON
- **WHEN** el usuario cambia un valor en la pestaña JSON y el JSON es válido
- **THEN** la lista de bloques, las propiedades y la vista previa reflejan el cambio

#### Scenario: JSON roto
- **WHEN** el JSON escrito no es válido
- **THEN** se muestra el error de sintaxis y el resto del editor conserva el último estado válido

#### Scenario: Cambios desde la interfaz
- **WHEN** el usuario cambia una propiedad en el panel
- **THEN** la pestaña JSON muestra el JSON actualizado

### Requirement: Deshacer y rehacer
El editor SHALL ofrecer deshacer y rehacer (con `Ctrl+Z` y `Ctrl+Y`) para los cambios del modelo, con al menos 100 pasos, agrupando como un solo paso lo tecleado de forma continua.

#### Scenario: Deshacer un borrado
- **WHEN** el usuario borra un bloque y pulsa deshacer
- **THEN** el bloque vuelve a su posición con sus propiedades

#### Scenario: Rehacer
- **WHEN** el usuario deshace y luego rehace
- **THEN** el cambio se aplica de nuevo

### Requirement: Validación en línea
El editor SHALL mostrar los errores del servicio junto al bloque y la propiedad afectados (marcando el bloque en la lista y el control en el panel) y, si el error no pertenece a un bloque, como error general junto a la vista previa, sin ventanas emergentes.

#### Scenario: Error de un bloque
- **WHEN** el servicio responde que el bloque 3 tiene `size` fuera de rango
- **THEN** el bloque 3 aparece marcado y su control `size` muestra el mensaje

#### Scenario: Error general
- **WHEN** el nombre de la plantilla no es válido
- **THEN** el mensaje aparece junto a la vista previa

### Requirement: Guardar y cerrar
Guardar SHALL enviar la plantilla al servicio junto con las imágenes del borrador; si el servicio la rechaza, el error SHALL mostrarse y la ventana seguir abierta. Al cerrar con cambios sin guardar SHALL preguntar si guardar, descartar o cancelar, y al descartar o cerrar SHALL borrar el borrador del servicio.

#### Scenario: Guardar correctamente
- **WHEN** el usuario guarda una plantilla válida
- **THEN** la ventana se cierra y la pestaña *Plantillas* muestra y selecciona esa plantilla

#### Scenario: Guardar con error
- **WHEN** el servicio rechaza la plantilla
- **THEN** la ventana sigue abierta con el error en línea y no se pierde nada

#### Scenario: Cerrar con cambios
- **WHEN** el usuario cierra la ventana con cambios sin guardar
- **THEN** se pregunta si guardar, descartar o cancelar

#### Scenario: Descartar
- **WHEN** el usuario elige descartar
- **THEN** la ventana se cierra, la plantilla guardada no cambia y el borrador se borra

### Requirement: Imágenes en el editor
Un bloque `image` SHALL permitir elegir un archivo PNG o JPEG, que se sube al borrador y queda disponible como `source` de ese bloque, y permitir quitarlo.

#### Scenario: Añadir un logo
- **WHEN** el usuario elige "logo.png" en un bloque `image`
- **THEN** la vista previa muestra el logo sin guardar aún la plantilla

