# template-batch-printing Specification

## Purpose
TBD - created by archiving change user-templates. Update Purpose after archive.
## Requirements
### Requirement: Copias
La impresión de una plantilla SHALL aceptar `copies` (1 a 50; por defecto 1) y SHALL imprimir ese número de copias idénticas como un único trabajo de la cola.

#### Scenario: Tres copias
- **WHEN** se imprime una plantilla con `copies` = 3
- **THEN** se encola un solo trabajo que imprime tres copias seguidas

#### Scenario: Demasiadas copias
- **WHEN** se piden 51 copias
- **THEN** la petición se rechaza indicando el máximo

#### Scenario: Cancelación
- **WHEN** se cancela el trabajo durante la segunda copia
- **THEN** no se imprimen las copias restantes

### Requirement: Lotes por filas
La impresión de una plantilla SHALL aceptar `rows`, una lista de hasta 200 conjuntos de campos, e imprimir una etiqueta por fila como un único trabajo, con un hueco corto entre etiquetas. Los campos que falten en una fila SHALL tomar los valores de la petición base o los de la plantilla por defecto.

#### Scenario: Lote de etiquetas
- **WHEN** se imprime `label` con tres filas de títulos distintos
- **THEN** se imprimen tres etiquetas, una por fila, en un solo trabajo

#### Scenario: Fila inválida
- **WHEN** la fila 2 de un lote de 3 no cumple los campos obligatorios
- **THEN** no se imprime ninguna etiqueta y el error indica el número de fila

#### Scenario: Demasiadas filas
- **WHEN** el lote tiene 201 filas
- **THEN** la petición se rechaza indicando el máximo

#### Scenario: Copias y filas a la vez
- **WHEN** se indican `copies` = 2 y tres filas
- **THEN** cada etiqueta se imprime dos veces seguidas

### Requirement: Numeración en lotes
En un lote o con varias copias, `{{counter}}` SHALL avanzar una vez por etiqueta impresa y no por copia, salvo que se pida lo contrario, de modo que cada etiqueta distinta tenga un número consecutivo.

#### Scenario: Lote numerado
- **WHEN** se imprimen tres filas con `{{counter}}` empezando en 10
- **THEN** las etiquetas llevan 10, 11 y 12 y el contador queda en 13

### Requirement: Lote desde CSV en la bandeja
La bandeja SHALL permitir cargar un archivo CSV (UTF‑8, separador coma o punto y coma, con una fila de cabecera con los nombres de campo) para imprimir una etiqueta por fila, mostrando el número de etiquetas y la vista previa de la primera, y pidiendo confirmación si hay más de 20.

#### Scenario: Carga de un CSV
- **WHEN** el usuario carga un CSV con cabecera `title,line1` y cinco filas
- **THEN** la bandeja muestra "5 etiquetas" y la vista previa de la primera

#### Scenario: Columna desconocida
- **WHEN** el CSV tiene una columna que no es un campo de la plantilla
- **THEN** la bandeja avisa de que se ignora esa columna

#### Scenario: Confirmación de lotes grandes
- **WHEN** el CSV tiene 30 filas
- **THEN** la bandeja pide confirmación antes de imprimir

### Requirement: Lotes desde la API y la CLI
La API (`copies`, `rows`) y la CLI (`--copies N`, `--csv archivo`) SHALL ofrecer las mismas copias y lotes, con los mismos límites.

#### Scenario: Desde la CLI
- **WHEN** se ejecuta `miniprinter template label --csv etiquetas.csv --rfcomm <mac>`
- **THEN** se imprime una etiqueta por fila del CSV

