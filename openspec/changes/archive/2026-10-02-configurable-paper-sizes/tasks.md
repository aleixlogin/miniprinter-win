## 1. Modelo y validación de los tamaños (paper-sizes)

- [x] 1.1 Añadir a `ServiceSettings` la lista `PaperSizes` (`PaperSizeSetting`: id, nombre, ancho, largo, activo, preajuste) y `DefaultPaperId`, y la definición de los siete preajustes de fábrica con sus ids (`48x210`, `48x100`, `48x50`, `48x297`, `48x1000`, `80x297`, `80x100`)
- [x] 1.2 Resolver la lista efectiva (`PaperSizes.Effective`): sin lista guardada son los siete preajustes activos y `48x210` por defecto; test de que un `settings.json` antiguo da exactamente la lista actual
- [x] 1.3 Validar en `SettingsStore.Validate`: ancho propio 30–57 y largo 10–1000 enteros, nombre ≤ 40 sin caracteres de control, sin medidas ni ids repetidos, máximo 30 tamaños, preajustes con sus medidas de fábrica, al menos uno activo y por defecto siempre activo; tests de cada regla y de los escenarios de corrección
- [x] 1.4 Generar la clave IPP a partir de las medidas (`om_x5h-<ancho>x<largo>mm_<ancho>x<largo>mm`) y el id `c<ancho>x<largo>` de los propios; "Restaurar valores de fábrica"

## 2. Anuncio por IPP

- [x] 2.1 Construir `IppPrinterDescription.Media` (y el por defecto) desde la lista efectiva en `IppHost.StartAsync`, con el por defecto primero
- [x] 2.2 Añadir márgenes por tamaño a `MediaSize` y anunciarlos en `media-col` y en las listas `media-*-margin-supported`: `(ancho − 48)/2` mm laterales para los propios de más de 48 mm, 0 en el resto; tests de los atributos IPP (57 mm → 450, 48 mm → 0, sin tocar los de 80 mm)
- [x] 2.3 Reiniciar el listener también cuando cambia la lista de tamaños activos o el por defecto (misma espera a que no haya trabajos); test: tras guardar tamaños distintos, `Get-Printer-Attributes` devuelve la lista nueva
- [x] 2.4 Comprobar cómo trata el servicio un trabajo que pide un tamaño ya no ofrecido (no debe fallar) y cubrirlo con un test
- [x] 2.5 Páginas más estrechas que el cabezal: el rasterizador las centra sobre blanco a tamaño real (con resolución conocida) en vez de ampliarlas hasta 48 mm; las imágenes sin resolución se siguen ajustando al ancho; tests

## 3. Servicio `WindowsQueue` (windows-queue)

- [x] 3.1 Definir `IPowerShellRunner` (script + variables de entorno → código de salida y salida) con la implementación real (`powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand`) y una falsa para los tests
- [x] 3.2 Validar el nombre de la impresora (sin `"` ni caracteres de control, ≤ 60) y pasar nombre, nombre anterior y URL por variables de entorno `MP_QUEUE`, `MP_OLD`, `MP_URL`; test con apóstrofo y con comillas
- [x] 3.3 Implementar la secuencia (borrar la temporal sobrante, trabajos pendientes, crear la temporal, comprobarla, borrar la antigua y su puerto solo si nadie lo usa, renombrar) como pasos del script con mensajes; tests con el ejecutor falso: éxito, fallo al crear (la antigua se conserva), trabajos pendientes, temporal sobrante, puerto compartido, cola anterior inexistente, sin privilegios
- [x] 3.4 Esperar a que `IppHost` sirva ya la lista actual antes de crear la cola (`IppHost.WaitForCurrentAsync`); test
- [x] 3.5 Estado de la tarea (`Idle`/`Running`/`Succeeded`/`Failed`, mensaje, paso), una sola tarea a la vez, y su inclusión en `StatusDto`
- [x] 3.6 Endpoints `GET /api/windows-queue` y `POST /api/windows-queue/recreate` (`previousName` opcional; `202`, `409` con tarea en marcha) solo en la API de control; actualizar `ControlClient`/contratos y tests de los endpoints (incluido que no existen en `/api/v1`)
- [x] 3.7 Descubierto con Windows real: no se puede crear una segunda cola para el mismo `printer-uuid`. El servicio guarda un `PrinterUuid` en los ajustes, lo renueva antes de cada cola nueva y espera a que el listener lo anuncie; `PUT /api/settings` lo conserva; `POST /api/windows-queue/prepare` lo expone para el ayudante elevado
- [x] 3.8 Mover el ejecutor de PowerShell y la secuencia a `MiniPrinter.Control` (`QueueRecreator`) y comprobar al final los tamaños que ve Windows (`PageMediaSize` de las capacidades de la cola), repitiendo hasta 3 pasadas mientras la lista esté desfasada; tests con el PowerShell falso (repaso, tope de pasadas, orden, lectura imposible)
- [x] 3.9 Los scripts devuelven códigos de salida explícitos y los errores llegan en texto claro (el envoltorio `try/catch` evita el CLIXML); `PUT /api/settings` con JSON inválido responde 400

## 4. Bandeja: Ajustes y recreación (paper-sizes-settings)

- [x] 4.1 Apartado "Papel que se muestra a Windows" en *Ajustes*: lista con casilla, nombre, medidas y marca de por defecto; elegir el por defecto; Restaurar valores de fábrica
- [x] 4.2 Diálogo Añadir/Editar tamaño (nombre, ancho, largo) con validación en línea y el aviso de márgenes laterales para anchos de más de 48 mm; Editar y Borrar desactivados en los preajustes
- [x] 4.3 Reglas en la interfaz: no desmarcar el último activo, no borrar ni desactivar el por defecto sin elegir otro
- [x] 4.4 Detectar al guardar que la lista de activos, el por defecto o el nombre cambiaron y mostrar la confirmación (Continuar / Guardar sin recrear); otros ajustes no la muestran
- [x] 4.5 Guardar los ajustes y llamar a `POST /api/windows-queue/recreate` (con `previousName` si cambió el nombre), consultar el estado hasta `Succeeded`/`Failed` y mostrar el progreso y el resultado
- [x] 4.6 Restaurar la impresora predeterminada del usuario (`PrinterSettings.IsDefaultPrinter` antes y `SetDefaultPrinter` después, solo si lo era)
- [x] 4.7 Botón "Recrear impresora de Windows" con la misma confirmación
- [x] 4.8 Petición automática de permisos: si la tarea falla por falta de privilegios (`NeedsElevation`), la bandeja lanza `miniprinter queue-recreate` con `runas` (UAC), lee su resultado y lo muestra; si se rechaza la petición, la cola anterior se conserva

## 5. Comprobación real y cierre

- [x] 5.1 Probado en Windows real con una cola de prueba aparte: al cambiar los tamaños y recrear, la lista nueva aparece en las capacidades de impresión de la cola (la primera pasada puede mostrar la lista antigua; la verificación repite hasta que coincide). Los puertos compartidos se respetan por construcción (solo se borra un puerto que ninguna otra cola usa)
- [x] 5.2 Comprobado en Windows real: un tamaño propio de 57 mm anuncia márgenes laterales de 4,5 mm y Windows lo recoge como zona imprimible de 48 mm centrada (`ImageableArea`: origen 4500, extensión 48000 micras)
- [x] 5.3 Comprobado: Windows muestra los tamaños por su medida (`48x100mm`, `57x150mm`…), no por el nombre que les da el usuario en Ajustes; se mantiene el nombre por medidas
- [x] 5.4 Actualizar el `README` (sección de ajustes y de Windows: tamaños de papel, recrear la impresora, qué se pierde) y el historial de cambios
- [x] 5.5 Ejecutar todas las pruebas y resolver las preguntas abiertas del diseño reflejándolas en las specs
