## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: Vistas previas con las filas de cada bloque
Las vistas previas de plantillas SHALL informar de las filas que ocupa cada bloque mediante la cabecera `X-Template-Blocks`, sin cambiar el contenido PNG de la respuesta.

#### Scenario: Cliente existente
- **WHEN** un cliente que ignora la cabecera pide la vista previa de `qr`
- **THEN** recibe el mismo PNG que antes
