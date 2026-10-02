## ADDED Requirements

### Requirement: Clasificación de páginas como texto
El sistema SHALL clasificar cada página rasterizada como "texto" o "imagen" con el mismo criterio que el tramado automático: es texto cuando menos del 8 % de los píxeles, tras escalar, son medios tonos.

#### Scenario: Página del Bloc de notas
- **WHEN** se rasteriza una página con texto negro sobre fondo blanco
- **THEN** la página se clasifica como texto

#### Scenario: Fotografía
- **WHEN** se rasteriza una fotografía con degradados
- **THEN** la página se clasifica como imagen

### Requirement: Modo texto de la impresora
El sistema SHALL imprimir las páginas de texto en modo texto: `BE 01`, la energía de texto del perfil para la oscuridad elegida (8000 en `d1`) y la velocidad de texto del perfil. Las páginas de imagen SHALL seguir usando `BE 00` y la energía de imagen (5000 en `d1`).

#### Scenario: Texto en modo automático
- **WHEN** se imprime una página clasificada como texto con el ajuste en Automático
- **THEN** el trabajo enviado contiene `AF 40 1F` y `BE 01`

#### Scenario: Imagen en modo automático
- **WHEN** se imprime una página clasificada como imagen con el ajuste en Automático
- **THEN** el trabajo enviado contiene `AF 88 13` y `BE 00`

#### Scenario: Documento mixto
- **WHEN** un documento de varias páginas mezcla páginas de texto y de imagen
- **THEN** cada página se envía con el modo que le corresponde

### Requirement: Ajuste del modo de impresión
El sistema SHALL ofrecer en los ajustes del servicio y en la bandeja la opción "Modo de impresión" con los valores Automático (por defecto), Siempre imagen y Siempre texto.

#### Scenario: Forzar modo imagen
- **WHEN** el ajuste es Siempre imagen
- **THEN** todas las páginas se envían con `BE 00` y la energía de imagen, sea cual sea su clasificación

#### Scenario: Forzar modo texto
- **WHEN** el ajuste es Siempre texto
- **THEN** todas las páginas se envían con `BE 01` y la energía de texto
