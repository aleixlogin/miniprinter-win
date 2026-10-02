# continuous-paper Specification

## Purpose
TBD - created by archiving change printing-enhancements. Update Purpose after archive.
## Requirements
### Requirement: Tamaño de rollo largo
El sistema SHALL ofrecer por IPP un tamaño de papel "rollo" de 48 mm de ancho y 1000 mm de largo, para que las aplicaciones maqueten tiras largas en una sola página; el recorte de filas en blanco SHALL eliminar el papel no usado.

#### Scenario: Tique largo
- **WHEN** una aplicación imprime 40 cm de contenido en el tamaño rollo
- **THEN** se imprime una única tira de unos 40 cm, sin cortes de página intermedios

#### Scenario: Tamaño visible en Windows
- **WHEN** Windows consulta los tamaños de papel de la impresora
- **THEN** el tamaño de rollo aparece junto a los de 48 mm existentes

### Requirement: Unir páginas en una tira continua
El sistema SHALL ofrecer el ajuste "Páginas continuas" (desactivado por defecto). Con el ajuste activo, las páginas de un mismo trabajo se imprimen seguidas: se recortan los blancos al principio y al final de cada página, se separan con un hueco configurable (4 mm por defecto) y la secuencia de fin de página (`BD · A1 ×N · BD · A3`) solo se envía tras la última página.

#### Scenario: Documento de tres páginas
- **WHEN** se imprime un documento de tres páginas con "Páginas continuas" activo
- **THEN** el trabajo contiene una única secuencia de fin de página, al final, y las páginas quedan separadas por el hueco configurado

#### Scenario: Ajuste desactivado
- **WHEN** "Páginas continuas" está desactivado
- **THEN** cada página termina con su propia secuencia de fin de página, como hasta ahora

### Requirement: Cancelación en modo continuo
El sistema SHALL enviar la secuencia de fin de página al cancelar un trabajo continuo, aunque no se haya llegado a la última página.

#### Scenario: Cancelar a mitad
- **WHEN** se cancela un trabajo continuo durante la segunda de tres páginas
- **THEN** se deja de enviar filas, se envía la secuencia de fin de página y el trabajo queda cancelado

