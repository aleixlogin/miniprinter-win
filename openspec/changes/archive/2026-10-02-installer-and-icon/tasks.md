## 1. Icono

- [x] 1.1 Crear `tools/IconGen` que dibuja el motivo de MiniPrinter en 16, 20, 24, 32, 40, 48, 64 y 256 px y genera `assets/miniprinter.ico`; versionar el `.ico` y una vista previa PNG
- [x] 1.2 `ApplicationIcon` en Tray, Service y CLI; `Icon` en `MainWindow` y `QuickNoteWindow`
- [x] 1.3 La bandeja usa el `.ico` como base y superpone el punto de estado; comprobar a 100 % y 150 % de escala

## 2. Versión y publicación

- [x] 2.1 Versión única: `-p:Version` sobreescribe `Directory.Build.props` en ensamblados e instalador
- [x] 2.2 Publicación dependiente del runtime (win-x64) de los tres proyectos en `artifacts/publish`

## 3. Instalador Inno Setup

- [x] 3.1 `installer/MiniPrinter.iss`: metadatos, `AppId`, icono, `PrivilegesRequired=admin`, `AppMutex`, archivos y carpeta de instalación
- [x] 3.2 Comprobación e instalación de los runtimes de .NET 8 (ASP.NET Core y Windows Desktop) si faltan
- [x] 3.3 Pasos de sistema: `%ProgramData%` y permisos, servicio (crear o reutilizar, fallos, arranque), espera del puerto IPP, cola de impresión si no existe, `HKLM\…\Run`, "Enviar a"
- [x] 3.4 Menú Inicio: carpeta "MiniPrinter" con "MiniPrinter" (`--open`) y "Desinstalar MiniPrinter"; tarea opcional de escritorio; relanzar la bandeja sin elevación al terminar (también en silencioso)
- [x] 3.5 Desinstalador: servicio, cola, firewall, arranque, "Enviar a", accesos y archivos; pregunta para conservar `%ProgramData%` (se conserva en modo silencioso)
- [x] 3.6 `scripts/build-installer.ps1 [-Version]`: tests, publish e `ISCC.exe` → `artifacts/installer/MiniPrinter-Setup-X.Y.Z.exe`

## 4. Verificación

- [x] 4.1 Instalar Inno Setup 6 en el equipo de desarrollo y compilar el instalador
- [x] 4.2 Probar: instalación nueva (tras desinstalar la actual con `-KeepConfig`), menú Inicio, icono en título y barra de tareas, impresión desde Windows
- [x] 4.3 Probar actualización in situ (instalar una versión superior encima con la bandeja abierta) y modo `/VERYSILENT`
- [x] 4.4 Probar desinstalación conservando y sin conservar la configuración
- [x] 4.5 Actualizar README (instalación con el instalador, scripts como alternativa de desarrollo)
