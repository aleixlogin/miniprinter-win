## Why

La lista de tamaños de papel que MiniPrinter anuncia a Windows está escrita en el código (7 tamaños fijos, el primero por defecto). Quien imprime etiquetas o tiques de otras medidas no puede ofrecerlas, ni quitar los que no usa. Y aunque se pudiera cambiar la lista, Windows guarda los tamaños al crear la cola de impresión, así que un cambio no se vería sin recrear la impresora.

## What Changes

- **Nuevo apartado en *Ajustes* de la bandeja: "Papel que se muestra a Windows"**: lista de tamaños con casilla (activo o no), un tamaño por defecto, y botones para añadir, editar y borrar tamaños propios. Los siete tamaños actuales son los preajustes.
- **Tamaños propios**: nombre, ancho de 30 a **57 mm** y largo de 10 a 1000 mm (en milímetros enteros). Para los anchos de más de 48 mm (lo que imprime el cabezal) el servicio anuncia márgenes laterales automáticos, para que Windows maquete a 48 mm y no haya que reducir el contenido.
- **El servicio anuncia por IPP la lista elegida** (tamaños, por defecto y "cargado"), la guarda en `settings.json` y reinicia el listener IPP al cambiarla. Sin ajustes guardados se anuncian los siete de hoy, así que nada cambia al actualizar.
- **Recrear la impresora de Windows** cuando cambian los tamaños (o el nombre): el servicio crea una cola nueva con la lista actual, borra la antigua con su puerto y la renombra, sin quedarse nunca sin impresora si algo falla. Antes de hacerlo, la bandeja pide confirmación y avisa de lo que se pierde (preferencias de impresión) y restaura el estado de impresora predeterminada del usuario.
- **Botón "Recrear impresora de Windows"** en *Ajustes*, útil también como reparación si la cola se borró a mano, y que hace que un cambio de nombre de la impresora se refleje en Windows.
- **API de control**: `POST /api/windows-queue/recreate` y consulta del estado de la tarea.

## Capabilities

### New Capabilities
- `paper-sizes`: lista configurable de tamaños de papel (preajustes y propios), su validación y su anuncio por IPP.
- `windows-queue`: recreación segura de la cola de impresión de Windows desde el servicio y su estado.
- `paper-sizes-settings`: apartado de *Ajustes* de la bandeja para elegir tamaños, confirmación y recreación.

### Modified Capabilities
- `x5h-windows-printing`: los tamaños que se ofrecen a Windows dejan de ser una lista fija y pasan a ser la lista configurada.
- `continuous-paper`: el tamaño "rollo" es un preajuste que se puede desactivar.

## Impact

- **Código**:
  - `MiniPrinter.Control`: `ServiceSettings` (lista de tamaños y tamaño por defecto), contratos y cliente.
  - `MiniPrinter.Ipp`: `IppPrinterDescription` y `IppPrinterService` (márgenes por tamaño, tamaño por defecto y cargado).
  - `MiniPrinter.Service`: `SettingsStore` (validación y migración), `IppHost` (reinicio al cambiar la lista), un servicio nuevo `WindowsQueue` que ejecuta PowerShell, y los endpoints de la cola.
  - `MiniPrinter.Tray`: sección de ajustes, diálogos de tamaño y confirmación, y restauración de la impresora predeterminada.
- **Dependencias**: ninguna nueva; se usa `powershell.exe` (ya lo usa el instalador) y la llamada `SetDefaultPrinter` de Windows.
- **Seguridad**: el servicio, que corre como administrador, ejecuta PowerShell con el nombre de la impresora y la URL pasados por variables de entorno (nunca concatenados en el script) y valida el nombre.
- **Compatibilidad**: sin `PaperSizes` en `settings.json` se anuncian los siete tamaños actuales con el mismo orden y el mismo por defecto. Los ajustes antiguos siguen siendo válidos.
- **Efecto visible**: recrear la cola borra las preferencias de impresión del usuario sobre esa impresora; por eso solo ocurre tras una confirmación explícita.
