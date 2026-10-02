## ADDED Requirements

### Requirement: Repositorio remoto
El repositorio local SHALL estar conectado a `https://github.com/aleixlogin/miniprinter-win` con la rama principal publicada.

#### Scenario: Primera publicación
- **WHEN** se conecta el remoto y se suben las ramas
- **THEN** el código y su historial aparecen en GitHub

### Requirement: Integración continua
Cada push y pull request SHALL compilar la solución y ejecutar todos los tests en un runner de Windows.

#### Scenario: Test fallido
- **WHEN** un push rompe un test
- **THEN** el workflow de CI falla y lo muestra en GitHub

### Requirement: Publicación por etiqueta
Al subir una etiqueta `vX.Y.Z`, un workflow SHALL compilar con la versión `X.Y.Z`, ejecutar los tests, generar `MiniPrinter-Setup-X.Y.Z.exe`, calcular `SHA256SUMS`, firmarlo con la clave Ed25519 guardada en el secreto `RELEASE_SIGNING_KEY` (`SHA256SUMS.sig`) y crear la release de GitHub con esos tres archivos y las notas de la versión.

#### Scenario: Nueva versión
- **WHEN** se sube la etiqueta `v0.5.0`
- **THEN** aparece la release "MiniPrinter 0.5.0" con el instalador, `SHA256SUMS` y `SHA256SUMS.sig`

#### Scenario: Tests fallidos
- **WHEN** los tests fallan durante el workflow de la etiqueta
- **THEN** no se crea ninguna release

### Requirement: Claves de firma
El proyecto SHALL incluir una herramienta para generar el par de claves Ed25519 y firmar `SHA256SUMS`; la clave pública SHALL incrustarse en la bandeja y la privada SHALL guardarse solo en los secretos de GitHub (nunca en el repositorio).

#### Scenario: Clave privada fuera del repositorio
- **WHEN** se revisa el contenido del repositorio
- **THEN** no contiene la clave privada de firma
