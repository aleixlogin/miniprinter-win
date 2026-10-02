## 1. Repositorio

- [x] 1.1 Conectar `origin` con `https://github.com/aleixlogin/miniprinter-win` y subir la rama de trabajo como `main` (con confirmación del usuario y sus credenciales)

## 2. Firma de releases

- [x] 2.1 `tools/sign-release` con `keygen`, `sign` y `verify` (ECDSA P-256 de .NET, sin dependencias)
- [x] 2.2 Generar el par de claves; el usuario guarda la privada en el secreto `RELEASE_SIGNING_KEY` y en una copia de seguridad; incrustar la pública

## 3. Lógica de actualización

- [x] 3.1 Proyecto `MiniPrinter.Updates`: SemVer de etiquetas, selección del asset, parseo de `SHA256SUMS`, verificación de firma y hash
- [x] 3.2 Tests con claves de prueba y respuestas de la API de ejemplo (versión mayor, igual, pre-release, firma inválida, hash distinto)

## 4. Bandeja

- [x] 4.1 `UpdateChecker`: comprobación al arrancar (+1 min) y cada 24 h, estado en `%LocalAppData%\MiniPrinter\updates.json`
- [x] 4.2 Notificación y ventana de actualización (notas, Actualizar / Más tarde / Omitir esta versión, progreso, aviso si hay trabajos en cola)
- [x] 4.3 Descarga a carpeta temporal, verificación y ejecución del instalador con `/SILENT` (UAC lo pide el instalador; gestionar el rechazo)
- [x] 4.4 Menú "Buscar actualizaciones", versión en el panel y opción "Comprobar actualizaciones automáticamente"

## 5. GitHub Actions

- [x] 5.1 `ci.yml`: compilar y tests en push/PR
- [x] 5.2 `release.yml`: en etiqueta `vX.Y.Z`, tests, instalador, `SHA256SUMS`, firma y release con notas

## 6. Verificación

- [x] 6.1 Publicar `v0.4.0`, instalarla a mano y comprobar que no ofrece actualización
- [x] 6.2 Publicar `v0.4.1` y comprobar el aviso, la descarga verificada, el UAC, la actualización y la reapertura de la bandeja
- [x] 6.3 Probar un instalador manipulado (hash o firma inválidos) y el rechazo del UAC (manipulación verificada con la release 0.4.1 real; el rechazo del UAC queda para la próxima versión, ver design)
- [x] 6.4 Documentar en el README cómo se publican versiones y cómo funciona la actualización
