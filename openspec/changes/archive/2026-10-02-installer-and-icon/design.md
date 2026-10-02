## Context

- El icono de la bandeja se dibuja en `TrayIcon.IconFor` con System.Drawing a 32 px (cuerpo de impresora, tique blanco y punto de estado). No hay ningún `.ico` en el repositorio.
- La instalación actual (`scripts/install.ps1`) publica autocontenido (214 MB), copia a `%ProgramFiles%\MiniPrinter`, fija los permisos de `%ProgramData%\MiniPrinter`, registra el servicio con `New-Service` y `sc failure`, crea la cola con `Add-Printer -IppURL`, añade la bandeja a `HKLM\…\Run`, crea el acceso "Enviar a" y abre la bandeja con `explorer.exe` para que no herede la elevación. `uninstall.ps1` lo deshace.
- La bandeja es de instancia única (mutex `Local\MiniPrinter.Tray`) y abre el panel con `--open`.
- La versión es `0.1.0` en `Directory.Build.props`.

## Goals / Non-Goals

**Goals:**
- Identidad visual coherente en todas las superficies de Windows.
- Un instalador estándar que haga lo mismo que los scripts, con menú Inicio, actualización in situ y desinstalación limpia.
- Que el instalador sirva tal cual para las actualizaciones automáticas (change `auto-update`).

**Non-Goals:**
- Firma de código (Authenticode): sin certificado por ahora; SmartScreen avisará en las primeras descargas.
- MSI / despliegue por directivas de grupo.
- Instalación por usuario sin administrador (el servicio y la cola lo impiden).

## Decisions

### D1. Icono generado por código
Un generador (`tools/IconGen`, consola .NET con ImageSharp.Drawing) dibuja el motivo en vectorial para cada tamaño (16, 20, 24, 32, 40, 48, 64, 256) con grosores ajustados, y empaqueta un `.ico` (entradas PNG para 256 y BMP/PNG para el resto). El resultado se versiona en `assets/miniprinter.ico` para no depender del generador en cada build. La bandeja carga el `.ico` (tamaño 16/32 según DPI) y superpone el punto de estado, en lugar de dibujar el cuerpo a mano.
- *Alternativa descartada*: un diseño externo en SVG. Sin herramientas de diseño en el flujo; el generador mantiene un único origen y permite iterar.

### D2. Inno Setup 6
`installer/MiniPrinter.iss` con `PrivilegesRequired=admin`, `AppId` fijo (GUID), `AppMutex=Local\MiniPrinter.Tray` (cierra/espera la bandeja), `SetupIconFile`, `UninstallDisplayIcon`, `VersionInfoVersion` y `OutputBaseFilename=MiniPrinter-Setup-{#AppVersion}`. La versión llega como `/DAppVersion=` desde el script de build.
- Pasos de sistema en `[Run]`/`[UninstallRun]` y en `[Code]` (Pascal): `sc create/config/failure/start/stop/delete`, `powershell -NoProfile -Command Add-Printer …` (solo si no existe la cola), `icacls` en `%ProgramData%`, `netsh advfirewall … delete rule` al desinstalar.
- `[Icons]`: `{group}\MiniPrinter` → `MiniPrinter.Tray.exe --open`; `{group}\Desinstalar MiniPrinter` → `{uninstallexe}`; `{autodesktop}` opcional (tarea). "Enviar a" con `{usersendto}`.
- Al terminar, `[Run]` abre la bandeja con `Flags: nowait postinstall runasoriginaluser` (sin elevación). En modo silencioso también se relanza.
- Desinstalación: `[Code]` pregunta "¿Conservar la configuración?" (en silencioso, se conserva) y borra `%ProgramData%\MiniPrinter` solo si se responde que no.
- *Alternativa descartada*: WiX/MSI: más trabajo para el mismo resultado; MSIX y Velopack no encajan con un servicio + cola.

### D3. Publicación dependiente del runtime
`dotnet publish -r win-x64 --self-contained false` para los tres proyectos en una carpeta común. El instalador comprueba el registro (`HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.AspNetCore.App` y `…\Microsoft.WindowsDesktop.App`, versión 8.x) y, si falta, descarga el instalador oficial (aka.ms/dotnet/8.0/…) y lo ejecuta en silencio antes de copiar archivos. Instalador de ~15–20 MB.
- *Revisable*: si se prefiere no depender de descargas de Microsoft, se publica autocontenido (~70 MB comprimido) cambiando una propiedad del build.

### D4. Versión única
`Directory.Build.props` mantiene `<Version>` como valor por defecto; el build del instalador lo sobreescribe con `-p:Version=X.Y.Z` (desde la etiqueta git en CI). El instalador y los ensamblados muestran la misma versión.

### D5. Script de build local
`scripts/build-installer.ps1 [-Version X.Y.Z]`: tests → publish → `ISCC.exe /DAppVersion=…` → `artifacts/installer/MiniPrinter-Setup-X.Y.Z.exe`. Localiza `ISCC.exe` en las rutas habituales de Inno Setup 6 o en `PATH`.

## Risks / Trade-offs

- **[SmartScreen avisa al ejecutar un instalador sin firmar]** → documentarlo; firma (SignPath Foundation o certificado) como mejora futura.
- **[La descarga del runtime falla sin conexión]** → mensaje claro con el enlace de descarga; opción de publicar autocontenido.
- **[Instalar sobre una instalación hecha con los scripts]** → el instalador detecta el servicio y la cola existentes y los reutiliza (mismas rutas y nombres).
- **[La bandeja no se cierra a tiempo]** → `AppMutex` + `CloseApplications`; si sigue abierta, el asistente pide cerrarla.
- **[`Add-Printer` falla en la primera instalación porque el servicio aún no escucha]** → esperar al puerto IPP (como hace el script actual) antes de crear la cola.

## Verification (2026-10-02)

Instalador de 9 MB (dependiente del runtime) probado por el usuario: instalación, actualización in situ (también `/VERYSILENT`) y desinstalación. Dos problemas encontrados y corregidos durante la prueba:

- **Restos de la instalación autocontenida anterior**: instalar encima de la versión de los scripts dejaba `hostfxr.dll` y el runtime antiguo en `{app}`; los ejecutables dependientes del runtime buscaban .NET en su propia carpeta ("You must install or update .NET", servicio sin abrir el puerto). Solución: `[InstallDelete]` vacía `{app}` antes de copiar.
- **`AppMutex` pedía cerrar la bandeja a mano incluso en `/VERYSILENT`** (Inno lo comprueba antes de ejecutar `[Code]`). Solución: sin `AppMutex`; la bandeja admite `--exit` (señal entre instancias) y el instalador/desinstalador la cierran limpiamente, con `taskkill` como último recurso.
- Por prudencia, el `.ico` usa entradas BMP (DIB) hasta 64 px y PNG solo en 256 px.

## Migration Plan

1. Desinstalar la versión instalada con `scripts\uninstall.ps1 -KeepConfig` (o directamente instalar encima: el instalador reutiliza servicio y cola).
2. Ejecutar `MiniPrinter-Setup-X.Y.Z.exe`.
3. Los scripts siguen disponibles para desarrollo.

## Open Questions

- ¿Icono en el escritorio marcado por defecto? (propuesta: desmarcado)
