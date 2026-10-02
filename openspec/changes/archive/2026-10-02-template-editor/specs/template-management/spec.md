## ADDED Requirements

### Requirement: Botones Crear, Editar y Eliminar en la bandeja
La pestaña *Plantillas* SHALL incluir los botones Crear, Editar y Eliminar. Crear SHALL ofrecer una plantilla en blanco o duplicar la seleccionada, pidiendo un nombre válido, que no exista y que no sea reservado. Editar SHALL abrir la plantilla seleccionada en el editor. Eliminar SHALL pedir confirmación antes de borrar.

#### Scenario: Crear en blanco
- **WHEN** el usuario pulsa Crear, elige "En blanco" y escribe el nombre "etiqueta2"
- **THEN** se abre el editor con una plantilla nueva llamada `etiqueta2`

#### Scenario: Crear duplicando
- **WHEN** el usuario selecciona `label`, pulsa Crear, elige "Duplicar la seleccionada" y escribe "mi-etiqueta"
- **THEN** se abre el editor con una copia de `label` llamada `mi-etiqueta`

#### Scenario: Nombre ya existente
- **WHEN** el usuario escribe un nombre que ya existe
- **THEN** se rechaza indicando que ya existe, sin abrir el editor

### Requirement: Editar una plantilla integrada
Al editar una plantilla integrada, el editor SHALL avisar de que guardarla con el mismo nombre crea una versión de usuario que sustituye a la integrada, y SHALL ofrecer guardarla con un nombre nuevo.

#### Scenario: Sustituir una integrada
- **WHEN** el usuario edita `label` y guarda con el mismo nombre
- **THEN** se crea una plantilla de usuario `label` que sustituye a la integrada, y la pestaña la marca como sustituida

#### Scenario: Copia con otro nombre
- **WHEN** el usuario edita `label` y usa *Guardar como…* con "mi-etiqueta"
- **THEN** `label` sigue siendo la integrada y aparece la nueva plantilla `mi-etiqueta`

### Requirement: Eliminar según el origen
Eliminar SHALL borrar una plantilla de usuario y sus imágenes. Para una plantilla de usuario que sustituye a una integrada, el botón SHALL llamarse "Restaurar integrada" y volver a usar la integrada. Para una integrada sin versión de usuario, el botón SHALL estar desactivado.

#### Scenario: Borrar una plantilla de usuario
- **WHEN** el usuario elige `recibo` (de usuario), pulsa Eliminar y confirma
- **THEN** la plantilla desaparece de la lista

#### Scenario: Restaurar una integrada
- **WHEN** el usuario elige `label` (que sustituye a la integrada), pulsa "Restaurar integrada" y confirma
- **THEN** la lista vuelve a mostrar la plantilla integrada `label`

#### Scenario: Integrada sin versión de usuario
- **WHEN** el usuario selecciona `qr`
- **THEN** el botón Eliminar está desactivado

#### Scenario: Cancelar la confirmación
- **WHEN** el usuario cancela el aviso de Eliminar
- **THEN** no se borra nada

### Requirement: Refresco de la lista tras gestionar
Tras guardar en el editor, o tras eliminar o restaurar una plantilla, la pestaña *Plantillas* SHALL recargar la lista del servicio y seleccionar la plantilla guardada, o la más cercana si se eliminó.

#### Scenario: Seleccionar la plantilla guardada
- **WHEN** el usuario guarda la plantilla `recibo` en el editor
- **THEN** la pestaña muestra `recibo` seleccionada con su formulario
