# windows-installer Specification

## Purpose
TBD - created by archiving change installer-and-icon. Update Purpose after archive.
## Requirements
### Requirement: Instalador con permisos de administrador
El sistema SHALL distribuirse como un único ejecutable `MiniPrinter-Setup-<versión>.exe` generado con Inno Setup, que solicita permisos de administrador (UAC) al iniciarse y muestra el icono y la versión de MiniPrinter.

#### Scenario: Ejecutar el instalador
- **WHEN** el usuario ejecuta `MiniPrinter-Setup-0.4.0.exe`
- **THEN** Windows muestra el aviso UAC y, tras aceptarlo, el asistente de instalación con el icono de MiniPrinter y la versión 0.4.0

### Requirement: Requisitos previos
El instalador SHALL comprobar si están instalados los runtimes de .NET 8 x64 necesarios (Microsoft.AspNetCore.App y Microsoft.WindowsDesktop.App) y, si falta alguno, descargarlo e instalarlo antes de continuar, informando al usuario.

#### Scenario: Runtime ausente
- **WHEN** el equipo no tiene el runtime de Windows Desktop de .NET 8
- **THEN** el instalador lo descarga e instala y después continúa con la instalación de MiniPrinter

#### Scenario: Runtimes presentes
- **WHEN** el equipo ya tiene ambos runtimes
- **THEN** el instalador no descarga nada adicional

### Requirement: Instalación completa
El instalador SHALL copiar la aplicación a `%ProgramFiles%\MiniPrinter`, preparar `%ProgramData%\MiniPrinter` con los permisos actuales, registrar y arrancar el servicio "MiniPrinter" (inicio automático, reinicio ante fallos), crear la cola "X5h Thermal Printer" con el Microsoft IPP Class Driver si no existe, registrar el arranque de la bandeja al iniciar sesión, crear el acceso "Enviar a → MiniPrinter" y abrir la bandeja como usuario normal (sin privilegios) al terminar.

#### Scenario: Instalación nueva
- **WHEN** se instala en un equipo sin MiniPrinter
- **THEN** el servicio queda en ejecución, la cola aparece en "Impresoras y escáneres" y la bandeja se abre sin privilegios de administrador

### Requirement: Accesos del menú Inicio
El instalador SHALL crear la carpeta "MiniPrinter" en el menú Inicio con los accesos "MiniPrinter" (abre la bandeja con el panel) y "Desinstalar MiniPrinter", y SHALL ofrecer como tarea opcional un acceso en el escritorio.

#### Scenario: Menú Inicio
- **WHEN** termina la instalación
- **THEN** el menú Inicio contiene la carpeta "MiniPrinter" con "MiniPrinter" y "Desinstalar MiniPrinter", ambos con el icono de la aplicación

#### Scenario: Abrir desde el menú Inicio
- **WHEN** el usuario pulsa "MiniPrinter" en el menú Inicio
- **THEN** se abre el panel (si la bandeja ya estaba abierta, se muestra su panel sin abrir una segunda instancia)

### Requirement: Actualización sobre una instalación existente
Al ejecutarse sobre una instalación anterior, el instalador SHALL cerrar la bandeja abierta, detener el servicio, sustituir los archivos, conservar `%ProgramData%\MiniPrinter` (ajustes, impresora seleccionada, tokens), no duplicar la cola de impresión, volver a arrancar el servicio y reabrir la bandeja. SHALL admitir los modos silenciosos de Inno Setup (`/SILENT`, `/VERYSILENT`) para las actualizaciones automáticas.

#### Scenario: Actualizar
- **WHEN** se ejecuta el instalador de 0.4.0 con 0.3.0 instalada y la bandeja abierta
- **THEN** la bandeja se cierra, se actualizan los archivos, el servicio vuelve a arrancar, la impresora seleccionada se conserva y la bandeja se reabre

### Requirement: Desinstalación
El desinstalador SHALL detener y eliminar el servicio, la cola de impresión, la regla de firewall, el arranque de la bandeja, el acceso "Enviar a", los accesos del menú Inicio y los archivos de programa, y SHALL preguntar si conservar la configuración de `%ProgramData%\MiniPrinter`.

#### Scenario: Desinstalar sin conservar configuración
- **WHEN** el usuario desinstala desde "Desinstalar MiniPrinter" y elige no conservar la configuración
- **THEN** no queda servicio, cola, accesos, archivos de programa ni `%ProgramData%\MiniPrinter`

#### Scenario: Desinstalar conservando configuración
- **WHEN** el usuario elige conservar la configuración
- **THEN** se elimina todo salvo `%ProgramData%\MiniPrinter`, que una instalación posterior reutiliza

