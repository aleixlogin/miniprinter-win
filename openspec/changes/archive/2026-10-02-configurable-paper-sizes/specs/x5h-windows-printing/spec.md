## MODIFIED Requirements

### Requirement: Impresora estándar de Windows
El sistema SHALL exponer la impresora térmica como una cola de impresión de Windows instalada con el "Microsoft IPP Class Driver", sin drivers de terceros, de forma que cualquier aplicación pueda imprimir en ella desde el diálogo estándar. Los tamaños de papel que se ofrecen SHALL ser los que el usuario haya activado en los ajustes.

#### Scenario: Imprimir desde una aplicación
- **WHEN** el usuario imprime desde una aplicación (Bloc de notas, Edge, Fotos o la página de prueba de Windows) en la cola "X5h Thermal Printer"
- **THEN** el trabajo llega al servicio, se imprime en la X5h y termina en estado `completed`

#### Scenario: Tamaños de papel ofrecidos
- **WHEN** Windows consulta los atributos de la impresora
- **THEN** ofrece los tamaños activos en los ajustes a 203 dpi, con el tamaño por defecto elegido; sin ajustes guardados, los tamaños de 48 mm (48×210 por defecto) y los virtuales de 80 mm de siempre
