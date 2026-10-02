# battery-telemetry Specification

## Purpose
TBD - created by archiving change printing-enhancements. Update Purpose after archive.
## Requirements
### Requirement: Registro de lecturas de estado
El sistema SHALL añadir una línea a un registro CSV en `%ProgramData%\MiniPrinter\telemetry\` por cada respuesta `A3` recibida, con marca de tiempo, estado del enlace, byte de alarmas, lectura del sensor de papel y byte de batería. El registro SHALL rotar al superar 5 MB y conservar como máximo 30 días.

#### Scenario: Lectura periódica
- **WHEN** el servicio está conectado y recibe la respuesta `A3` del sondeo de 30 s
- **THEN** se añade una línea con los tres bytes en crudo y la hora

#### Scenario: Rotación
- **WHEN** el registro supera 5 MB
- **THEN** se archiva y se empieza uno nuevo, y se borran los archivos de más de 30 días

### Requirement: Muestreo durante la descarga
El sistema SHALL permitir activar desde la bandeja un "muestreo de batería" que mantiene la conexión abierta y consulta `A3` cada 60 s durante un tiempo elegido (hasta 24 h), sin desconexión por inactividad, para registrar una descarga completa.

#### Scenario: Muestreo activo
- **WHEN** el usuario activa el muestreo durante 8 horas
- **THEN** el servicio no se desconecta por inactividad y registra una lectura por minuto hasta que termina el periodo o el usuario lo detiene

### Requirement: Exportación del registro
La bandeja SHALL ofrecer "Exportar registro de batería" que guarda el CSV donde elija el usuario.

#### Scenario: Exportar
- **WHEN** el usuario pulsa "Exportar registro de batería"
- **THEN** se guarda un CSV con todas las lecturas conservadas

### Requirement: Interpretación del byte de batería
El sistema SHALL interpretar el byte 2 de `A3` según un ajuste "Unidad de batería": Desconocida (por defecto: se muestra el valor en bruto), Porcentaje (valor = %) o Décimas de voltio (se convierte a % con una curva de descarga de litio de 3,3 V = 0 % a 4,2 V = 100 %).

#### Scenario: Unidad desconocida
- **WHEN** la unidad es Desconocida y el byte vale 39
- **THEN** la bandeja muestra "39 (valor en bruto)"

#### Scenario: Décimas de voltio
- **WHEN** la unidad es Décimas de voltio y el byte vale 39
- **THEN** la bandeja muestra 3,9 V y el porcentaje que corresponde según la curva

### Requirement: Aviso de batería baja
Cuando la unidad no es Desconocida, el sistema SHALL avisar con una notificación de la bandeja al bajar la batería del umbral configurado (20 % por defecto), una sola vez por descarga, y SHALL informar `other-warning` en `printer-state-reasons`.

#### Scenario: Cruce del umbral
- **WHEN** la batería pasa del 21 % al 19 %
- **THEN** aparece una única notificación "Batería baja" y IPP incluye `other-warning`

#### Scenario: Recarga
- **WHEN** la batería vuelve a superar el umbral más un 5 %
- **THEN** el aviso se rearma para la siguiente descarga

