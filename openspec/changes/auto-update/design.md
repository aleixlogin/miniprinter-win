## Context

- Repositorio público en GitHub: `aleixlogin/miniprinter-win` (vacío, rama por defecto `main`). El repositorio local tiene la rama `feature/windows-print-bridge-x5h` con dos commits y sin remoto.
- El instalador del change `installer-and-icon` pide UAC, detecta la bandeja por su mutex, actualiza in situ conservando la configuración, admite `/SILENT` y relanza la bandeja sin elevación.
- La bandeja corre como el usuario (sin admin). Decisión del usuario: **no** añadir componentes con privilegios para actualizar; la app descarga y es el instalador quien eleva.

## Goals / Non-Goals

**Goals:**
- Avisar de versiones nuevas y actualizar con un clic y la confirmación UAC.
- Ejecutar solo instaladores publicados por nosotros.
- Publicar versiones con una etiqueta git.

**Non-Goals:**
- Actualizaciones silenciosas sin confirmación.
- Deltas/parches binarios (el instalador dependiente del runtime pesa ~15–20 MB).
- Firma Authenticode (sin certificado por ahora).
- Canales beta (las pre-releases se ignoran).

## Decisions

### D1. Comprobación en la bandeja, sin servicio con privilegios
`UpdateChecker` (en la bandeja) consulta la API de releases de GitHub (sin token: 60 peticiones/hora por IP, de sobra para una comprobación diaria) con `User-Agent: MiniPrinter/<versión>`. Primera comprobación 1 min tras arrancar, luego cada 24 h. Estado guardado en `%LocalAppData%\MiniPrinter\updates.json` (última comprobación, versión omitida, comprobación automática).

### D2. Verificación: SHA-256 + firma Ed25519 propia
- La release incluye `SHA256SUMS` (formato `sha256sum`) y `SHA256SUMS.sig` (firma Ed25519 en Base64 del contenido exacto de `SHA256SUMS`).
- La bandeja incrusta la clave pública (32 bytes, Base64) y verifica con una implementación Ed25519 (NSec/libsodium o `BouncyCastle.Cryptography`, a evaluar por tamaño; .NET 8 no trae Ed25519 nativo).
- Solo si la firma es válida y el hash del instalador coincide se ejecuta.
- *Alternativa descartada*: confiar solo en HTTPS + hash de la misma release: quien pudiera modificar la release (token robado) podría cambiar ambos. Con la firma necesita además la clave privada.

### D3. Ejecución del instalador
`Process.Start(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART /LOG=…")` con `UseShellExecute = true` para que el manifiesto del instalador (`requireAdministrator`) dispare el UAC. Si el usuario lo rechaza, `Win32Exception` con código 1223 → mensaje "Actualización cancelada". El instalador cierra la bandeja (`AppMutex`/`CloseApplications`), actualiza y la relanza (`runasoriginaluser`), así que la bandeja no tiene que hacer nada más tras lanzarlo.

### D4. Interfaz
- Notificación de la bandeja "MiniPrinter X.Y.Z disponible" → al pulsarla, ventana con las notas (texto Markdown de la release mostrado como texto plano), "Actualizar", "Más tarde", "Omitir esta versión" y una barra de progreso de descarga.
- Menú del icono: "Buscar actualizaciones".
- Ajustes (preferencia local de la bandeja): "Comprobar actualizaciones automáticamente".
- Panel: "Versión X.Y.Z".

### D5. Lógica testeable separada
`MiniPrinter.Updates` (net8.0): comparación SemVer de etiquetas, selección del asset por nombre, parseo de `SHA256SUMS`, verificación de firma y de hash. Tests unitarios con un par de claves de prueba y releases de ejemplo (JSON de la API guardado).

### D6. Pipeline de GitHub Actions
- `ci.yml`: en push/PR, `windows-latest`, `dotnet test`.
- `release.yml`: en etiqueta `v*`: versión = etiqueta sin `v` → tests → `scripts/build-installer.ps1 -Version X.Y.Z` (instalando Inno Setup con `choco install innosetup` si el runner no lo trae) → `SHA256SUMS` → `tools/sign-release` con el secreto `RELEASE_SIGNING_KEY` → `gh release create vX.Y.Z` con los tres archivos y notas generadas a partir de los commits.
- `tools/sign-release`: `keygen` (imprime clave privada y pública) y `sign <archivo>`.

### D7. Publicación inicial del repositorio
Conectar `origin` y subir la rama de trabajo como `main` (el repositorio remoto está vacío). Requiere las credenciales de GitHub del usuario (Git Credential Manager) en el primer push.

## Risks / Trade-offs

- **[SmartScreen avisa al ejecutar el instalador descargado]** → documentado; firma con SignPath Foundation (gratis para OSS) o certificado como mejora futura.
- **[Pérdida de la clave privada]** → las apps instaladas no podrían verificar versiones firmadas con una clave nueva: guardar copia de seguridad fuera de GitHub; una rotación requeriría publicar una versión firmada con la clave antigua que incruste la nueva.
- **[Límite de la API de GitHub]** → una comprobación diaria; si responde 403/429, se reintenta al día siguiente.
- **[El usuario cierra el UAC por error]** → la versión actual sigue funcionando; se puede reintentar desde el menú.
- **[La actualización se lanza con un trabajo imprimiéndose]** → la ventana avisa si la cola tiene trabajos y propone esperar.

## Migration Plan

1. Conectar el remoto y subir el código (`main`).
2. Generar las claves con `tools/sign-release keygen`, guardar la privada en `RELEASE_SIGNING_KEY` (y en una copia de seguridad) e incrustar la pública.
3. Publicar `v0.4.0` (primera versión con el actualizador) y su instalador; instalarla a mano.
4. A partir de ahí, cada etiqueta nueva se ofrece desde la bandeja.

## Open Questions

- Librería Ed25519 final (NSec frente a BouncyCastle): elegir por tamaño y licencia.
