## Why

Con el instalador (change `installer-and-icon`) MiniPrinter se puede distribuir, pero cada versión nueva obliga al usuario a enterarse, descargarla e instalarla a mano. El código ya está en un repositorio público de GitHub (`aleixlogin/miniprinter-win`), así que las versiones pueden publicarse como GitHub Releases y la propia aplicación puede avisar y actualizarse, siempre con la confirmación del usuario.

## What Changes

- **Comprobación de actualizaciones en la bandeja** (no en el servicio): al arrancar y cada 24 h consulta la última release de GitHub; si hay una versión mayor, notifica y muestra las novedades.
- **Actualización con confirmación**: "Actualizar" descarga el instalador a una carpeta temporal, verifica su SHA-256 contra `SHA256SUMS` y la firma de ese archivo con la clave pública incrustada en la app, y ejecuta el instalador; es el propio instalador quien pide permisos (UAC), cierra la bandeja y el servicio, actualiza y los vuelve a abrir. "Más tarde" y "Omitir esta versión" disponibles.
- **Opciones en la bandeja**: comprobar automáticamente (activado por defecto), "Buscar actualizaciones ahora" en el menú del icono, versión actual en el panel.
- **Canal de publicación**: GitHub Actions publica, al crear una etiqueta `vX.Y.Z`, una release con el instalador, `SHA256SUMS` y `SHA256SUMS.sig` (firma ECDSA P-256 con clave privada guardada en los secretos del repositorio), tras compilar y pasar los tests.
- **Primera publicación**: conectar el repositorio local con el remoto y subir las ramas.

## Capabilities

### New Capabilities
- `auto-update`: comprobación, aviso, descarga verificada y ejecución del instalador desde la bandeja con confirmación del usuario.
- `release-pipeline`: versionado por etiquetas y publicación automática de releases firmadas en GitHub.

### Modified Capabilities
<!-- Ninguna. -->

## Impact

- `MiniPrinter.Tray`: comprobador de actualizaciones, ventana de novedades, opciones y menú.
- Nuevo proyecto pequeño o carpeta `MiniPrinter.Updates` (lógica testeable: comparación de versiones, verificación de hash y firma).
- `.github/workflows/release.yml` y `ci.yml`; `tools/sign-release` para firmar `SHA256SUMS`.
- **Seguridad**: la app solo ejecuta instaladores cuyo hash esté en un `SHA256SUMS` firmado con nuestra clave; sin firma Authenticode, SmartScreen puede avisar al ejecutar el instalador.
- **Repositorio**: el código pasa a ser público en GitHub.
- Depende de `installer-and-icon` (el instalador debe admitir actualización in situ y modo silencioso).
