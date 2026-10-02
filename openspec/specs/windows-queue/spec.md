# windows-queue Specification

## Purpose
TBD - created by archiving change configurable-paper-sizes. Update Purpose after archive.
## Requirements
### Requirement: Recrear la cola de impresión de Windows
El servicio SHALL poder recrear la cola de impresión de Windows de la X5h (con el "Microsoft IPP Class Driver" apuntando a su propio endpoint IPP) para que Windows lea la lista de tamaños actual. SHALL crear primero una cola nueva con un nombre temporal, comprobar que existe, borrar la cola antigua y renombrar la nueva, de modo que si falla la creación la cola antigua siga funcionando.

#### Scenario: Recreación correcta
- **WHEN** se pide recrear la cola y todo funciona
- **THEN** la cola con el nombre de la impresora queda creada con los tamaños actuales y no queda ninguna cola temporal

#### Scenario: Falla la creación de la cola nueva
- **WHEN** `Add-Printer` falla al crear la cola temporal
- **THEN** la cola antigua se conserva sin cambios y el estado indica el fallo con su motivo

#### Scenario: Cola temporal sobrante
- **WHEN** existe una cola temporal de un intento anterior
- **THEN** se borra antes de crear la nueva

### Requirement: Esperar a que el listener ya anuncie la lista nueva
Antes de crear la cola nueva, el servicio SHALL esperar a que su listener IPP esté sirviendo la lista de tamaños actual, para que Windows lea la lista correcta al crearla.

#### Scenario: Listener reiniciándose
- **WHEN** se pide recrear la cola mientras el listener está aplicando un cambio de ajustes
- **THEN** la creación de la cola empieza cuando el listener ya anuncia la lista nueva

### Requirement: No borrar si hay trabajos pendientes
Si la cola de Windows tiene trabajos pendientes, el servicio SHALL abortar sin borrar ni crear nada e indicar cuántos hay.

#### Scenario: Trabajos pendientes
- **WHEN** la cola de Windows tiene 2 trabajos pendientes
- **THEN** el estado es fallo con el mensaje de que hay 2 trabajos pendientes y la cola no se toca

### Requirement: Puerto compartido
Al borrar la cola antigua, el servicio SHALL borrar su puerto solo si ninguna otra cola lo usa.

#### Scenario: Otra cola usa el puerto
- **WHEN** la cola nueva usa el mismo puerto IPP que la antigua
- **THEN** el puerto no se borra

### Requirement: Cambio de nombre de la impresora
Si se indica el nombre anterior, el servicio SHALL borrar la cola que tuviera ese nombre y dejar la nueva con el nombre actual de la impresora; si la cola anterior no existe, SHALL limitarse a crear la nueva.

#### Scenario: Renombrar la impresora
- **WHEN** el nombre cambia de "X5h Thermal Printer" a "Etiquetas" y se recrea la cola con el nombre anterior
- **THEN** la cola "X5h Thermal Printer" desaparece y existe la cola "Etiquetas"

#### Scenario: Cola anterior borrada a mano
- **WHEN** no existe ninguna cola con el nombre anterior
- **THEN** se crea la cola nueva y el resultado es correcto

### Requirement: Estado y una sola tarea
El servicio SHALL exponer en `GET /api/windows-queue` si existe la cola, su nombre y el estado de la tarea (`Idle`, `Running`, `Succeeded` o `Failed`, con mensaje y paso), y SHALL aceptar `POST /api/windows-queue/recreate` (con `previousName` opcional) respondiendo `202` y ejecutando la tarea en segundo plano. Solo SHALL haber una tarea a la vez.

#### Scenario: Consultar el estado
- **WHEN** se consulta el estado mientras se recrea la cola
- **THEN** el estado es `Running` con el paso actual

#### Scenario: Segunda petición
- **WHEN** se pide recrear la cola mientras ya hay una tarea en marcha
- **THEN** se responde `409` sin iniciar otra

#### Scenario: Solo desde el panel
- **WHEN** se pide `/api/v1/windows-queue/recreate` con el token de automatización
- **THEN** no existe (404): solo está en la API de control

### Requirement: Entrada segura al ejecutar PowerShell
El servicio SHALL pasar el nombre de la impresora y la URL IPP al script mediante variables de entorno, no concatenados en el texto del script, y SHALL rechazar nombres con comillas dobles o caracteres de control o de más de 60 caracteres.

#### Scenario: Nombre con comillas
- **WHEN** el nombre de la impresora contiene `"` o un salto de línea
- **THEN** se rechaza antes de ejecutar nada

#### Scenario: Nombre con apóstrofo
- **WHEN** el nombre es "Etiquetas d'Ana"
- **THEN** la cola se crea con ese nombre sin romper el script

### Requirement: Sin privilegios
Si el servicio no tiene privilegios para gestionar impresoras, la tarea SHALL terminar como fallo con un mensaje que lo explique, sin tocar la cola existente.

#### Scenario: Servicio sin administrador
- **WHEN** `Add-Printer` responde que se deniega el acceso
- **THEN** el estado es fallo con un mensaje sobre los permisos y la cola antigua sigue intacta

### Requirement: Identidad nueva en cada creación de cola
Windows no acepta una segunda cola para una impresora con el mismo `printer-uuid`. Antes de crear cada cola nueva, el servicio SHALL asignar a la impresora un UUID nuevo, guardarlo y esperar a que el listener IPP lo anuncie. El UUID SHALL conservarse al guardar ajustes desde la bandeja.

#### Scenario: Crear la cola nueva junto a la antigua
- **WHEN** se recrea la cola mientras la antigua sigue existiendo
- **THEN** la cola nueva se crea sin el error «La impresora especificada ya existe» porque el servicio anuncia un UUID distinto

#### Scenario: Cada pasada, un UUID distinto
- **WHEN** la recreación necesita varias pasadas
- **THEN** cada pasada usa un UUID diferente

#### Scenario: Ajustes antiguos en la bandeja
- **WHEN** la bandeja guarda unos ajustes leídos antes de recrear la cola
- **THEN** el UUID actual del servicio no se sobrescribe

### Requirement: Comprobar la lista de tamaños que ve Windows
Tras recrear la cola, el servicio SHALL leer los tamaños de papel que Windows ofrece para ella (las opciones `PageMediaSize` de sus capacidades de impresión) y compararlos con los tamaños anunciados. Mientras no coincidan, SHALL repetir la recreación, hasta un máximo de 3 pasadas, reemplazando la cola recién creada. Si tras la última pasada no coinciden, SHALL terminar correctamente indicando qué tamaños faltan o sobran y que Windows puede tardar unos minutos en actualizarse. Si las capacidades no se pueden leer, SHALL terminar correctamente sin repetir.

#### Scenario: Windows ve la lista antigua
- **WHEN** tras la primera pasada Windows ofrece los tamaños antiguos y tras la segunda los nuevos
- **THEN** la tarea termina correctamente después de dos pasadas y lo indica en el mensaje

#### Scenario: Windows no se actualiza
- **WHEN** tras tres pasadas Windows sigue ofreciendo una lista distinta
- **THEN** la tarea termina correctamente con un mensaje que enumera los tamaños que faltan o sobran

#### Scenario: Orden y mayúsculas
- **WHEN** Windows ofrece los mismos tamaños en otro orden
- **THEN** se considera que coinciden y no se repite

#### Scenario: No se pueden leer las capacidades
- **WHEN** la lectura de las capacidades de impresión falla
- **THEN** la tarea termina correctamente, sin repetir, indicando que no se pudo comprobar la lista

### Requirement: Petición automática de permisos de administrador
Cuando la recreación falla porque el servicio no tiene permisos para gestionar impresoras, el estado SHALL indicar que se necesita elevación y la bandeja SHALL repetir el trabajo automáticamente en un proceso elevado (`miniprinter queue-recreate`), de modo que Windows muestre la petición de permisos (UAC). El proceso elevado SHALL pedir al servicio una identidad nueva antes de cada cola que cree y SHALL devolver su resultado a la bandeja. Si el usuario rechaza la petición, la impresora de Windows SHALL quedar como estaba.

#### Scenario: El servicio no tiene permisos
- **WHEN** `Add-Printer` responde que se deniega el acceso
- **THEN** el estado de la tarea indica que necesita elevación y la bandeja lanza el ayudante con la petición de permisos

#### Scenario: El usuario acepta
- **WHEN** el usuario acepta la petición de permisos
- **THEN** el ayudante recrea la cola, la bandeja muestra el resultado y restaura la impresora predeterminada si lo era

#### Scenario: El usuario rechaza
- **WHEN** el usuario cancela la petición de permisos
- **THEN** la bandeja informa de que se canceló y la cola anterior sigue funcionando

#### Scenario: Otros errores
- **WHEN** la recreación falla por un motivo que no son los permisos
- **THEN** no se pide elevación

