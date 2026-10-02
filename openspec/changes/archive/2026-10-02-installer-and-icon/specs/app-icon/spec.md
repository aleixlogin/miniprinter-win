## ADDED Requirements

### Requirement: Icono de la aplicación
El proyecto SHALL incluir un archivo `.ico` con los tamaños 16, 20, 24, 32, 40, 48, 64 y 256 px, con el mismo motivo que el icono de la bandeja (impresora térmica con un tique saliendo) dibujado con el detalle adecuado a cada tamaño, y un generador reproducible que lo cree.

#### Scenario: Tamaños disponibles
- **WHEN** se inspecciona el `.ico` generado
- **THEN** contiene imágenes de 16, 20, 24, 32, 40, 48, 64 y 256 px

### Requirement: Icono en ejecutables y ventanas
Los ejecutables `MiniPrinter.Tray.exe`, `MiniPrinter.Service.exe` y `miniprinter.exe` SHALL llevar el icono incrustado, y las ventanas de la bandeja (panel y nota rápida) SHALL mostrarlo en la barra de título y en la barra de tareas.

#### Scenario: Panel abierto
- **WHEN** el usuario abre el panel de MiniPrinter
- **THEN** la barra de título y el botón de la barra de tareas muestran el icono de MiniPrinter en lugar del icono genérico de Windows

#### Scenario: Ejecutable en el Explorador
- **WHEN** se ve `MiniPrinter.Tray.exe` en el Explorador de archivos
- **THEN** se muestra el icono de MiniPrinter

### Requirement: Icono coherente en la bandeja
El icono de la bandeja SHALL usar el mismo dibujo que el icono de la aplicación, conservando el punto de color que indica el estado (conectada, imprimiendo, alarma, desconectada, sin servicio).

#### Scenario: Estado de alarma
- **WHEN** la impresora informa sin papel
- **THEN** el icono de la bandeja muestra el motivo de MiniPrinter con el punto de estado en rojo
