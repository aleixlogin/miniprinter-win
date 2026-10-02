## Context

`IppPrinterDescription.Media` es una lista fija de siete `MediaSize` (nombre IPP, ancho y largo en centésimas de milímetro). `IppPrinterService.PrinterAttributes` la publica en `media-supported`, `media-col-database` y `media-size-supported`, usa `Media[0]` como `media-default` y `media-ready`, y anuncia los márgenes a 0. `IppHost.StartAsync` crea la descripción solo con el nombre y la ubicación, y reinicia el listener cuando cambian `NetworkMode`, el puerto o el nombre (esperando a que no haya trabajos en curso).

La cola de Windows se crea en el instalador con `Add-Printer -IppURL http://127.0.0.1:<puerto>/ipp/print` (Microsoft IPP Class Driver) y se borra al desinstalar con `Remove-Printer` y `Remove-PrinterPort`. El driver de clase lee los tamaños al crear la cola y los conserva, por lo que cambiar la lista del servicio no se refleja en una cola ya creada.

El servicio corre como administrador (SYSTEM); la bandeja corre como el usuario y no puede borrar impresoras del equipo. El estado de "impresora predeterminada" es por usuario.

## Goals / Non-Goals

**Goals:**
- Elegir qué tamaños de papel se ofrecen a Windows, con uno por defecto y tamaños propios (ancho máximo 57 mm).
- Que el cambio llegue de verdad a Windows, recreando la cola sin dejar al usuario sin impresora si algo falla.
- Que actualizar desde una versión anterior no cambie nada hasta que el usuario toque la lista.

**Non-Goals:**
- Tipos de medio distintos de `continuous` (etiquetas con huecos, otros soportes).
- Nombres amables para los tamaños en Windows (se muestran los derivados de las medidas; ver preguntas abiertas).
- Ancho superior a 57 mm en los tamaños propios, o varias fuentes de papel.
- Cambiar la impresión de los tamaños de 80 mm de fábrica.

## Decisions

### D1. Modelo en los ajustes
`ServiceSettings` gana `PaperSizes` (lista) y `DefaultPaperId`. Cada `PaperSizeSetting` lleva `Id` (identificador estable), `Name` (etiqueta del usuario), `WidthMm`, `LengthMm`, `Enabled` y `Preset` (verdadero para los de fábrica; el id y las medidas de un preajuste no se editan, solo se activan o desactivan).
- **Sin lista guardada** (`null` o vacía) equivale a los siete preajustes activos, con `48x210` por defecto: es exactamente el comportamiento actual, de modo que `settings.json` antiguos no cambian nada.
- Los ids de los preajustes son `48x210`, `48x100`, `48x50`, `48x297`, `48x1000` (rollo), `80x297` y `80x100`; los propios usan `c<ancho>x<largo>` (p. ej. `c57x150`).
- *Alternativa descartada*: guardar solo las claves IPP activas. No permite tamaños propios ni etiquetas.

### D2. Validación (`SettingsStore.Validate`)
- Ancho propio entero de **30 a 57 mm**, largo propio entero de **10 a 1000 mm**; nombre de hasta 40 caracteres, sin caracteres de control.
- No pueden repetirse las medidas (ancho × largo) ni los ids; como máximo 30 tamaños.
- Al menos un tamaño activo; si el por defecto no existe o está desactivado, pasa a ser el primero activo.
- Un preajuste conserva sus medidas aunque el JSON diga otras. Un ajuste inválido se corrige, no se rechaza el archivo.

### D3. Anuncio por IPP
`IppHost` construye `IppPrinterDescription.Media` a partir de los tamaños activos, ordenando el por defecto primero. La clave IPP se genera con las medidas, como hasta ahora: `om_x5h-<ancho>x<largo>mm_<ancho>x<largo>mm` (así lo que Windows muestra sigue siendo la medida y no depende del nombre del usuario).
- `media-default` y `media-ready` son el tamaño por defecto.
- **Márgenes**: para un tamaño **propio** de más de 48 mm, `media-col` lleva `media-left-margin` y `media-right-margin` iguales a `(ancho − 48) / 2` mm (en centésimas), y los márgenes superior e inferior a 0; los demás tamaños no cambian. Es lo que permite que Windows maquete a 48 mm dentro de una página de 57 mm. Las listas `media-*-margin-supported` incluyen esos valores.
- Un tamaño de ancho menor que 48 mm se anuncia sin márgenes laterales.
- `IppHost` reinicia el listener también cuando cambia la lista de tamaños (misma regla: sin trabajos en curso).

### D4. Servicio `WindowsQueue`
Un servicio nuevo en el servicio de Windows que recrea la cola ejecutando `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand …`. Secuencia (idempotente):
```
 0. esperar a que el listener sirva ya la lista nueva
 1. si hay trabajos pendientes en la cola de Windows → abortar con mensaje (no se borra nada)
 2. borrar una cola temporal sobrante "<nombre> (nueva)"  (de un intento anterior)
 3. crear "<nombre> (nueva)" con Add-Printer -IppURL …                 ← si falla: terminar, la antigua sigue
 4. comprobar que la nueva existe y responde
 5. borrar la cola antigua (nombre anterior) y su puerto SI NINGUNA otra cola lo usa
 6. renombrar la nueva a <nombre>
```
- **Entrada por variables de entorno** (`MP_QUEUE`, `MP_OLD`, `MP_URL`), nunca concatenada en el script: el nombre de la impresora lo escribe el usuario. El nombre se valida además (sin comillas dobles ni caracteres de control, máximo 60).
- **Abstracción para tests**: un `IPowerShellRunner` con `RunAsync(script, env)` devuelve código de salida y salida; los tests usan uno falso que simula cada paso, incluidos los fallos.
- **Puerto compartido**: dos colas con la misma URL pueden compartir el puerto de IPP; por eso el paso 5 solo lo borra si ya nadie lo usa.
- **Estado de la tarea** (`Idle`, `Running`, `Succeeded`, `Failed` con mensaje y el paso) en memoria, expuesto en `GET /api/windows-queue` y dentro del `StatusDto`; una sola tarea a la vez (una segunda petición devuelve `409`).
- Se ejecuta en segundo plano; el cliente consulta el estado. No bloquea el IPP, que atiende la consulta de `Add-Printer`.
- *Alternativa descartada*: borrar la antigua antes de crear la nueva. Si `Add-Printer` falla, el usuario se queda sin impresora.
- *Alternativa descartada*: que lo haga la bandeja. Un usuario normal no puede borrar impresoras del equipo.

### D5. Endpoints de la cola (API de control, solo loopback y con token)
- `GET /api/windows-queue` → `{ exists, name, state, message, step }`.
- `POST /api/windows-queue/recreate` con `{ "previousName": "…" }` opcional → `202`; `409` si ya hay una tarea o el servicio no puede ejecutar PowerShell con privilegios.
No se exponen en la API de automatización.

### D6. Bandeja: sección de *Ajustes*
Una sección "Papel que se muestra a Windows" con una lista (casilla, nombre y medidas, marca del por defecto), botones **Añadir…**, **Editar…** y **Borrar** (solo para propios; los preajustes solo se activan o desactivan) y **Restaurar valores de fábrica**.
- El diálogo de tamaño pide nombre, ancho y largo con validación en línea (rangos de D2) y muestra las medidas con los márgenes que se anunciarán.
- No se puede quitar la marca de activo del último tamaño activo ni borrar el que es por defecto sin elegir otro.
- Al **guardar** con la lista (o el nombre) cambiada, se muestra una confirmación: "Se volverá a crear la impresora de Windows. Se perderán las preferencias de impresión de esa impresora y los trabajos pendientes deben terminar antes." con Continuar y Guardar sin recrear (los ajustes se guardan, pero Windows sigue con los tamaños antiguos hasta recrear).
- El botón **Recrear impresora de Windows** hace lo mismo cuando se quiera; muestra el progreso y el resultado.
- **Impresora predeterminada**: antes de recrear, la bandeja comprueba con `PrinterSettings.IsDefaultPrinter` si la cola lo era para este usuario, y al terminar la vuelve a marcar con `SetDefaultPrinter` (la bandeja corre como el usuario). Si no lo era, no toca nada.

### D7. Orden de operaciones al guardar
```
 tray: confirmar ──► PUT /api/settings  ──► servicio reinicia el listener (cuando no hay trabajos)
        └─► POST /api/windows-queue/recreate ──► el servicio espera a que el listener ya sirva la lista nueva ──► D4
        └─► consultar GET /api/windows-queue hasta Succeeded/Failed ──► restaurar la predeterminada
```
El servicio es quien espera al listener (no la bandeja), para que no importe cuándo llega la petición.

## Risks / Trade-offs

- **Puerto o caché de Windows**: si la cola nueva reutiliza el puerto de la antigua, el driver podría mostrar los tamaños antiguos → una prueba real al principio; si ocurre, la alternativa es borrar antes la antigua y el puerto y luego crear (con un breve hueco sin impresora) y avisarlo en la confirmación.
- **Preferencias del usuario perdidas** → confirmación explícita con el aviso, y la opción de guardar sin recrear.
- **Trabajos pendientes en la cola de Windows** → se abortan con mensaje antes de borrar nada.
- **El servicio no es administrador** (ejecución en desarrollo) → `Add-Printer` falla; se informa del motivo y no se toca nada.
- **Márgenes ignorados por Windows** → si el driver no los respeta, un tamaño de más de 48 mm se reducirá un poco; es el comportamiento actual de los de 80 mm y no rompe nada. Se comprobará en una impresora real.
- **Páginas más estrechas que 48 mm** → hay que comprobar que el rasterizador no las amplía hasta los 384 puntos (se debe imprimir a tamaño real, centrado); es una tarea de verificación.
- **Cambio de nombre y cola antigua** → se pasa `previousName` para borrar la cola con el nombre anterior; si no existe (borrada a mano) simplemente se crea la nueva.
- **Inyección en PowerShell** → variables de entorno y validación del nombre; el script es una constante.

## Migration Plan

1. Modelo, validación y anuncio por IPP con los siete preajustes como valor por defecto (nada cambia para nadie).
2. Servicio `WindowsQueue` y sus endpoints, con tests contra un ejecutor falso.
3. Sección de *Ajustes* y flujo de confirmación y recreación.
4. Comprobación real en Windows (puerto compartido, márgenes, nombres, páginas estrechas).
Sin migración de datos. Revertir es no tocar los ajustes de papel: el servicio vuelve a anunciar los siete tamaños.

## Open Questions

- ¿Cómo muestra Windows los nombres de los tamaños (clave generada) y se puede mejorar con una etiqueta? Se comprobará en la prueba real; si no, se mantiene el nombre por medidas.
- ¿Debe haber un ancho mínimo menor que 30 mm? Se fija 30 mm por ahora.

## Decisiones tomadas con Windows real (apéndice)

- **UUID nuevo en cada creación (D4 revisado)**: Windows rechaza («La impresora especificada ya existe») crear una segunda cola para una impresora con el mismo `printer-uuid`, con cualquier nombre y puerto. El orden seguro (crear la nueva junto a la antigua) solo es posible si el servicio anuncia un UUID distinto, así que `ServiceSettings.PrinterUuid` se renueva antes de cada cola nueva y el servicio espera a que el listener lo anuncie. La bandeja no lo sobrescribe al guardar ajustes.
- **Lista de tamaños desfasada una generación**: el driver de Windows genera `ipp_print_capabilities.xml` (lo que muestra *Preferencias de impresión*) con los datos de la creación anterior, mientras que `pdc.xml` y los atributos IPP ya traen los nuevos. Reiniciar el spooler, borrar y crear con el mismo nombre o esperar más de un minuto no lo arreglan; una creación más sí. Por eso tras recrear se leen las opciones `PageMediaSize` de las capacidades de la cola y se repite la recreación (hasta 3 pasadas) mientras no coincidan con lo anunciado. Si no coinciden tras la última pasada, la tarea termina bien con un mensaje claro.
- **Errores de PowerShell**: los scripts devuelven códigos de salida explícitos (un `Get-Printer` sin resultado dejaba `$?` en falso) y se ejecutan dentro de un `try/catch` que escribe el error por la salida estándar (por stderr llegaba como CLIXML).
- **Permisos y UAC (D6 ampliado)**: el servicio corre como administrador, pero si falla por falta de privilegios la tarea lo indica (`NeedsElevation`) y la bandeja repite el trabajo con `miniprinter queue-recreate` elevado (`runas`, petición automática). El ayudante pide al servicio una identidad nueva antes de cada cola (`POST /api/windows-queue/prepare`) y devuelve su resultado en un archivo JSON; si el usuario rechaza la petición, la cola anterior se conserva.
- **Márgenes**: Windows respeta los márgenes laterales anunciados (zona imprimible de 48 mm dentro de una página de 57 mm).
- **Nombres**: Windows muestra el tamaño por su medida, no por la etiqueta del usuario.
