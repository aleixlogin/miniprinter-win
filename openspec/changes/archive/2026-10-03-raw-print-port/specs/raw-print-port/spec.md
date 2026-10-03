## ADDED Requirements

### Requirement: Puerto TCP de impresión directa
El servicio SHALL poder escuchar en un puerto TCP (por defecto 9100, protocolo RAW/JetDirect) y encolar como trabajos de impresión lo que se reciba por cada conexión. El puerto SHALL estar desactivado por defecto y solo activarse desde los ajustes.

#### Scenario: Desactivado por defecto
- **WHEN** el servicio arranca con los ajustes de fábrica
- **THEN** no hay ningún proceso escuchando en el puerto 9100

#### Scenario: Activarlo
- **WHEN** se activa el puerto en los ajustes
- **THEN** el servicio empieza a aceptar conexiones en ese puerto sin reiniciar el servicio de Windows

#### Scenario: Desactivarlo
- **WHEN** se desactiva el puerto
- **THEN** deja de escuchar y las conexiones nuevas se rechazan

#### Scenario: Puerto ocupado
- **WHEN** otro programa usa el puerto elegido
- **THEN** el estado indica el error y el resto del servicio sigue funcionando

### Requirement: Alcance de red
El puerto SHALL escuchar solo en `127.0.0.1` en el modo "Solo este PC" y en todas las interfaces en el modo "Toda la red local". En modo red local SHALL crear una regla de firewall solo para redes privadas, y SHALL quitarla al desactivar el puerto o cambiar de modo.

#### Scenario: Solo este PC
- **WHEN** el modo de red es "Solo este PC"
- **THEN** una conexión desde otro equipo no llega al puerto

#### Scenario: Red local
- **WHEN** el modo de red es "Toda la red local" y el puerto está activo
- **THEN** otros equipos de la red privada pueden conectarse y existe la regla de firewall

#### Scenario: Cambio de modo
- **WHEN** se cambia el modo de red con el puerto activo
- **THEN** el listener se reinicia con el alcance nuevo cuando no hay trabajos en curso

### Requirement: Detección del contenido
Por cada conexión, el servicio SHALL decidir una sola vez el tipo de contenido por sus primeros bytes: PNG, JPEG, PDF y PWG Raster como documentos; ESC/POS cuando empiece por `ESC`, `GS` o `DLE` o contenga esas órdenes antes del texto; texto plano cuando sea UTF-8 válido sin órdenes de control. Cualquier otro contenido SHALL rechazarse cerrando la conexión sin imprimir.

#### Scenario: Imagen en bruto
- **WHEN** se envía un PNG por el puerto (`nc equipo 9100 < foto.png`)
- **THEN** se imprime como cualquier documento PNG

#### Scenario: PDF
- **WHEN** se envía un PDF que empieza por `%PDF-`
- **THEN** se imprime página a página

#### Scenario: Texto plano
- **WHEN** se envía `cat nota.txt | nc equipo 9100` con texto UTF-8
- **THEN** se imprime con el renderizado de texto

#### Scenario: ESC/POS
- **WHEN** el flujo empieza por `ESC @`
- **THEN** se interpreta como ESC/POS

#### Scenario: Contenido desconocido
- **WHEN** se envían bytes binarios que no son de ningún tipo reconocido
- **THEN** la conexión se cierra y no se encola ningún trabajo

### Requirement: Fin de trabajo
Un trabajo SHALL terminar al cerrar el cliente la conexión o tras 2 segundos sin datos una vez recibido contenido. En ESC/POS, una orden de corte (`GS V`) SHALL terminar el trabajo y lo recibido después SHALL empezar otro.

#### Scenario: Cierre de la conexión
- **WHEN** el cliente envía un documento y cierra
- **THEN** el trabajo se encola con todo lo recibido

#### Scenario: Silencio
- **WHEN** el cliente envía un tique ESC/POS y no cierra ni corta
- **THEN** a los 2 segundos sin datos se encola el trabajo

#### Scenario: Dos tiques en una conexión
- **WHEN** el cliente envía un tique, un corte y otro tique
- **THEN** se encolan dos trabajos

### Requirement: Límites
El servicio SHALL aceptar como máximo 4 conexiones simultáneas, 16 MB por conexión y 30 segundos de inactividad sin completar la lectura; al superar cualquiera, SHALL cerrar la conexión sin imprimir lo incompleto.

#### Scenario: Demasiadas conexiones
- **WHEN** hay 4 conexiones abiertas y llega una quinta
- **THEN** la quinta se cierra

#### Scenario: Demasiado grande
- **WHEN** una conexión envía más de 16 MB
- **THEN** se cierra y no se imprime nada de ella

#### Scenario: Conexión muerta
- **WHEN** un cliente se conecta y no envía nada durante 30 segundos
- **THEN** la conexión se cierra

### Requirement: Integración con la cola
Los trabajos del puerto SHALL pasar por la cola normal (orden, retención sin papel, cancelación y vista previa del último trabajo) y SHALL mostrar como origen `raw@<dirección del cliente>`.

#### Scenario: Origen visible
- **WHEN** un cliente en 192.168.0.30 imprime por el puerto
- **THEN** el trabajo aparece en la cola con el usuario `raw@192.168.0.30`

#### Scenario: Sin papel
- **WHEN** la impresora no tiene papel al llegar un trabajo
- **THEN** el trabajo queda retenido y se imprime al volver el papel, como los demás

#### Scenario: Cancelar
- **WHEN** el usuario cancela el trabajo desde la bandeja
- **THEN** no se imprime

### Requirement: Papel máximo por conexión
La conexión ESC/POS SHALL cerrarse cuando pida más del presupuesto de papel del intérprete (160 000 puntos), imprimiendo solo lo anterior y registrando el motivo.

#### Scenario: Conexión abusiva
- **WHEN** un cliente pide más papel del permitido
- **THEN** la conexión se cierra, se encolan como mucho tantos tiques como quepan en el presupuesto y el servicio sigue atendiendo a los demás
