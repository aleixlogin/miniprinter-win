## ADDED Requirements

### Requirement: Diagnóstico con semáforos
La bandeja SHALL ofrecer una función *Diagnóstico* que muestre una lista de comprobaciones, cada una con su estado (correcto, aviso, error o desconocido), un detalle y, si no está correcta, qué hacer. Las comprobaciones SHALL ser: servicio iniciado, Bluetooth encendido, impresora emparejada, conexión con la impresora, papel y alarmas, cola de Windows creada y apuntando al servicio, escucha IPP, puerto 9100 y fuentes del sistema para CJK, árabe y tailandés.

#### Scenario: Todo correcto
- **WHEN** todo funciona y el usuario abre el diagnóstico
- **THEN** todas las comprobaciones salen correctas

#### Scenario: Bluetooth apagado
- **WHEN** el Bluetooth de Windows está apagado
- **THEN** la comprobación «Bluetooth» sale en error con la indicación de encenderlo, y las que dependen de él no se marcan como error propio

#### Scenario: Impresora sin emparejar
- **WHEN** la impresora elegida no está emparejada en Windows
- **THEN** la comprobación sale en error con la indicación de emparejarla desde la pestaña *Buscar impresoras*

#### Scenario: Servicio detenido
- **WHEN** la bandeja no puede hablar con el servicio
- **THEN** solo «Servicio iniciado» sale en error, con la indicación de iniciarlo, y las demás salen como desconocido

#### Scenario: Cola de Windows borrada
- **WHEN** la impresora de Windows se ha borrado a mano
- **THEN** la comprobación sale en error con la indicación de recrearla en *Ajustes → Papel*

#### Scenario: Puerto 9100 ocupado
- **WHEN** el puerto de impresión directa está activado y otro programa lo usa
- **THEN** la comprobación sale en error con el motivo

#### Scenario: Comprobación que no se puede completar
- **WHEN** una comprobación no puede terminar (por ejemplo, sin permiso para consultar el firewall)
- **THEN** sale como desconocido y nunca como error

### Requirement: Acciones desde el diagnóstico
Cada comprobación con una solución en la propia aplicación SHALL ofrecer un botón que lleve a ella (la sección de *Ajustes* o la pestaña correspondiente), y el diagnóstico SHALL poder abrirse desde el menú «⋯» de *Estado* y desde la acción sugerida de la tarjeta de estado cuando hay un error.

#### Scenario: Ir a la solución
- **WHEN** el usuario pulsa el botón de la comprobación «Cola de Windows»
- **THEN** se abre *Ajustes* en la sección *Papel*

#### Scenario: Desde la tarjeta de estado
- **WHEN** la tarjeta de estado muestra un error de conexión
- **THEN** su botón *Diagnosticar* abre el diagnóstico

### Requirement: Informe copiable sin secretos
El diagnóstico SHALL tener un botón *Copiar informe* que ponga en el portapapeles un texto con la versión de la aplicación y del servicio, la versión de Windows, un resumen de los ajustes (modo de red, puertos, tamaños de papel, si la impresión directa y la API están activas) y el resultado de cada comprobación. El informe NO SHALL incluir el token de ninguna API, nombres de plantillas, direcciones de clientes ni contenido impreso.

#### Scenario: Copiar el informe
- **WHEN** el usuario pulsa *Copiar informe*
- **THEN** el portapapeles contiene el texto y se avisa de que se ha copiado

#### Scenario: Sin secretos
- **WHEN** la API de automatización está activada con un token
- **THEN** el informe dice que está activa, sin incluir el token

### Requirement: Diagnóstico sin efectos
Ejecutar el diagnóstico NO SHALL cambiar ajustes, imprimir ni mover papel, y SHALL terminar en pocos segundos aunque alguna comprobación se quede sin respuesta.

#### Scenario: Comprobación lenta
- **WHEN** una comprobación no responde
- **THEN** se marca como desconocida pasados unos segundos y el resto se muestra
