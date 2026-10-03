# tray-localization Specification

## Purpose
TBD - created by archiving change gui-foundation. Update Purpose after archive.
## Requirements
### Requirement: Textos de la interfaz en recursos
Todos los textos visibles de la bandeja (etiquetas, botones, mensajes, menús, diálogos y avisos) SHALL estar en un archivo de recursos con claves estables, y no escritos en el código ni en el XAML, de modo que puedan traducirse sin tocar las pantallas. El idioma por defecto SHALL ser el español.

#### Scenario: Ningún texto suelto
- **WHEN** se revisan los XAML y el código de la bandeja
- **THEN** no hay textos visibles literales, salvo una lista corta de excepciones (nombre del producto, formatos)

#### Scenario: Cambiar un texto
- **WHEN** se cambia el valor de una clave en el archivo de recursos
- **THEN** el texto cambia en todas las pantallas donde se usa

#### Scenario: Clave que falta
- **WHEN** una pantalla pide una clave que no existe
- **THEN** una prueba automática falla antes de publicar y, en ejecución, se muestra el nombre de la clave en vez de dejar el hueco vacío

### Requirement: Estados del servicio en español
La bandeja SHALL mostrar los estados y mensajes que devuelve el servicio (por ejemplo `Completed`, `ProcessingStopped`, `Printer did not answer`, `Printer needs attention: OutOfPaper`) traducidos al idioma de la interfaz mediante una tabla; un texto que no esté en la tabla SHALL mostrarse tal cual, sin perderlo.

#### Scenario: Trabajo completado
- **WHEN** el servicio informa de un trabajo en estado `Completed` con el mensaje `Printed`
- **THEN** la lista de trabajos muestra «Completado» e «Impreso»

#### Scenario: Impresora sin papel
- **WHEN** el servicio informa `ProcessingStopped` con `Printer needs attention: OutOfPaper`
- **THEN** la lista muestra «En espera» y «Sin papel»

#### Scenario: Mensaje desconocido
- **WHEN** el servicio devuelve un mensaje que la tabla no conoce
- **THEN** se muestra el mensaje original

