## MODIFIED Requirements

### Requirement: Tamaño de rollo largo
El sistema SHALL ofrecer por IPP, mientras esté activo en los ajustes, un tamaño de papel "rollo" de 48 mm de ancho y 1000 mm de largo, para que las aplicaciones maqueten tiras largas en una sola página; el recorte de filas en blanco SHALL eliminar el papel no usado. El rollo es un preajuste que el usuario puede desactivar.

#### Scenario: Tique largo
- **WHEN** una aplicación imprime 40 cm de contenido en el tamaño rollo
- **THEN** se imprime una única tira de unos 40 cm, sin cortes de página intermedios

#### Scenario: Tamaño visible en Windows
- **WHEN** Windows consulta los tamaños de papel de la impresora y el rollo está activo
- **THEN** el tamaño de rollo aparece junto a los de 48 mm existentes

#### Scenario: Rollo desactivado
- **WHEN** el usuario desactiva el tamaño rollo en los ajustes
- **THEN** deja de ofrecerse a Windows
