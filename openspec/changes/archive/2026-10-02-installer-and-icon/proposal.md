## Why

MiniPrinter se instala hoy con dos scripts de PowerShell lanzados como administrador y una publicación autocontenida de 214 MB, y la aplicación no tiene icono propio: la ventana, la barra de tareas y los `.exe` muestran el icono genérico de Windows (solo la bandeja dibuja uno por código). Para distribuirla (y para poder actualizarla, ver el change `auto-update`) hace falta un instalador normal de Windows y una identidad visual.

## What Changes

- **Icono de la aplicación**: un `.ico` multi-tamaño (16–256 px) con el mismo dibujo que la bandeja (impresora térmica con el tique saliendo), usado en los `.exe`, la barra de título y la barra de tareas de las ventanas (panel y nota rápida), el instalador, el menú Inicio y "Aplicaciones instaladas". La bandeja conserva el punto de estado de color superpuesto.
- **Instalador con Inno Setup** (`MiniPrinter-Setup-<versión>.exe`) que sustituye a `install.ps1`/`uninstall.ps1`:
  - pide permisos de administrador (UAC) al arrancar;
  - comprueba e instala, si faltan, los runtimes de .NET 8 (ASP.NET Core y Windows Desktop);
  - instala en `%ProgramFiles%\MiniPrinter`, registra y arranca el servicio, crea la cola de impresión y el arranque de la bandeja, y el acceso "Enviar a";
  - crea la carpeta **"MiniPrinter"** en el menú Inicio con los accesos **"MiniPrinter"** (abre el panel) y **"Desinstalar MiniPrinter"**, e icono opcional en el escritorio;
  - actualiza sobre una instalación anterior conservando la configuración y sin duplicar la cola;
  - desinstalador que deshace todo y pregunta si conservar la configuración.
- La versión de la aplicación pasa a tomarse de una única propiedad del build, usada por los ensamblados y por el instalador.

## Capabilities

### New Capabilities
- `app-icon`: icono de la aplicación y su uso en ejecutables, ventanas, instalador y bandeja.
- `windows-installer`: instalador y desinstalador de Windows con Inno Setup, accesos del menú Inicio y actualización sobre una instalación existente.

### Modified Capabilities
<!-- Ninguna: los requisitos de x5h-windows-printing sobre la instalación se cumplen igual; el instalador sustituye a los scripts como forma recomendada. -->

## Impact

- **Nuevos archivos**: `assets/miniprinter.ico` (y generador), `installer/MiniPrinter.iss`, script de compilación del instalador.
- **Proyectos**: `ApplicationIcon` en Tray, Service y CLI; `Icon` en las ventanas WPF; la bandeja usa el mismo dibujo.
- **Publicación**: pasa a ser dependiente del runtime (framework-dependent, win-x64): instalador de ~15–20 MB en lugar de 214 MB autocontenidos.
- **Herramientas**: Inno Setup 6 para compilar el instalador (en local y en CI).
- **Scripts**: `install.ps1`/`uninstall.ps1` se mantienen para desarrollo, documentados como alternativa.
