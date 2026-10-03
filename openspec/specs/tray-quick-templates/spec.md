# tray-quick-templates Specification

## Purpose
TBD - created by archiving change gui-diagnostics-and-templates. Update Purpose after archive.
## Requirements
### Requirement: Favoritos desde el icono de la bandeja
El menú del icono de la bandeja SHALL tener un submenú *Plantillas* con una lista *Favoritos* (cada entrada con la plantilla y el nombre del favorito). Elegir un favorito SHALL imprimir la plantilla al instante con los valores guardados, igual que desde la pestaña *Plantillas*, y avisar con un globo de que se está imprimiendo.

#### Scenario: Imprimir un favorito
- **WHEN** el usuario elige «Etiqueta — Frágil» en *Plantillas → Favoritos*
- **THEN** se imprime la plantilla `label` con los valores de ese favorito y aparece un globo «Imprimiendo …»

#### Scenario: Cancelar
- **WHEN** el usuario se arrepiente tras imprimir un favorito
- **THEN** puede cancelar el trabajo desde *Estado* mientras no haya terminado

#### Scenario: Favorito inválido
- **WHEN** el favorito ya no es válido (por ejemplo, la plantilla cambió y falta un campo obligatorio)
- **THEN** no se imprime, y el globo explica el motivo

#### Scenario: Plantilla borrada
- **WHEN** una plantilla se borra
- **THEN** sus favoritos desaparecen del menú

#### Scenario: Sin favoritos
- **WHEN** no hay ningún favorito
- **THEN** el submenú dice «Sin favoritos» y apunta a la pestaña *Plantillas*

### Requirement: Plantillas recientes
El submenú *Plantillas* SHALL tener una lista *Recientes* con las últimas 5 plantillas impresas desde la pestaña *Plantillas* (la más reciente primero). Elegir una SHALL abrir el panel en *Plantillas* con esa plantilla y sus últimos valores cargados, sin imprimir.

#### Scenario: Abrir una reciente
- **WHEN** el usuario elige una plantilla en *Recientes*
- **THEN** se abre el panel en *Plantillas* con esa plantilla y los últimos valores que usó, y no se imprime nada

#### Scenario: Orden
- **WHEN** el usuario imprime `qr`, luego `label` y abre el menú
- **THEN** *Recientes* muestra `label` y después `qr`

#### Scenario: Máximo de cinco
- **WHEN** se han impreso más de cinco plantillas distintas
- **THEN** *Recientes* muestra solo las cinco últimas

### Requirement: Menú con muchos favoritos
Cuando haya más de 15 favoritos, el submenú *Favoritos* SHALL agruparlos por plantilla.

#### Scenario: Muchos favoritos
- **WHEN** hay 20 favoritos de cuatro plantillas
- **THEN** el submenú muestra cuatro grupos con sus favoritos dentro

