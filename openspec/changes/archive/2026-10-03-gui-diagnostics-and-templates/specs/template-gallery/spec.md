## ADDED Requirements

### Requirement: Galería de plantillas con miniaturas
La pestaña *Plantillas* SHALL mostrar las plantillas como una cuadrícula de tarjetas con una miniatura de la plantilla dibujada con valores de ejemplo, su título y su origen (integrada, de usuario o que sustituye a una integrada). Un interruptor SHALL alternar entre la galería y la lista, y la vista elegida SHALL recordarse.

#### Scenario: Ver las plantillas
- **WHEN** el usuario abre *Plantillas* en la vista de galería
- **THEN** ve una tarjeta por plantilla con su miniatura, título y origen

#### Scenario: Elegir una plantilla
- **WHEN** el usuario hace clic en una tarjeta
- **THEN** se abre su formulario con la vista previa en vivo, como al elegirla en la lista

#### Scenario: Recordar la vista
- **WHEN** el usuario elige la lista, cierra el panel y lo vuelve a abrir
- **THEN** *Plantillas* se abre en la lista

#### Scenario: Miniaturas que tardan
- **WHEN** las miniaturas aún no han llegado
- **THEN** cada tarjeta muestra un marcador y se rellena sola al llegar, sin bloquear el panel

### Requirement: Miniaturas con valores de ejemplo
El servicio SHALL generar la miniatura de una plantilla dibujándola con los valores por defecto de sus campos (o, si no los tienen, su etiqueta), tolerando los campos obligatorios vacíos y mostrando un marcador gris en los campos de imagen sin valor. Las miniaturas SHALL actualizarse cuando la plantilla se guarde o se borre.

#### Scenario: Plantilla integrada
- **WHEN** se pide la miniatura de la plantilla `label`
- **THEN** se devuelve un PNG con la etiqueta dibujada con valores de ejemplo

#### Scenario: Plantilla editada
- **WHEN** el usuario guarda una nueva versión de una plantilla suya
- **THEN** la miniatura siguiente refleja el cambio

#### Scenario: Campo de imagen
- **WHEN** la plantilla tiene un campo de imagen sin valor por defecto
- **THEN** la miniatura muestra un marcador gris en su sitio

#### Scenario: Plantilla desconocida
- **WHEN** se pide la miniatura de una plantilla que no existe
- **THEN** el servicio responde 404

### Requirement: Buscar y recientes
Un cuadro de búsqueda SHALL filtrar las tarjetas por título y descripción, y las plantillas usadas recientemente SHALL aparecer primero, con las demás por orden alfabético.

#### Scenario: Buscar
- **WHEN** el usuario escribe «wifi»
- **THEN** se muestran solo las plantillas cuyo título o descripción lo contienen

#### Scenario: Las recientes primero
- **WHEN** el usuario imprime la plantilla `qr` y vuelve a la galería
- **THEN** `qr` aparece la primera
