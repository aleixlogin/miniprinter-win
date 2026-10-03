## ADDED Requirements

### Requirement: Copiar la dirección del puerto 9100
La sección *Impresión directa* SHALL ofrecer *Copiar dirección*, que copie `dirección:puerto` de una dirección útil (una de la red local cuando el modo es «Toda la red local», y la de loopback en «Solo este PC»), dejando elegir si hay varias.

#### Scenario: Red local con una dirección
- **WHEN** el modo es «Toda la red local», el puerto escucha y el equipo tiene una dirección 192.168.1.50
- **THEN** *Copiar dirección* copia `192.168.1.50:9100`

#### Scenario: Varias direcciones
- **WHEN** el equipo tiene dos direcciones de red
- **THEN** se ofrece elegir cuál copiar

#### Scenario: Solo este PC
- **WHEN** el modo es «Solo este PC»
- **THEN** se ofrece `127.0.0.1:9100` y se avisa de que desde otro equipo hay que activar la red local

#### Scenario: Puerto desactivado
- **WHEN** la impresión directa está desactivada
- **THEN** las acciones de copiar y de mostrar el código QR no están disponibles y se explica por qué

### Requirement: Código QR de la dirección
La sección SHALL ofrecer *Mostrar código QR*, que abra una ventana con el código QR de la dirección elegida y la dirección escrita debajo, para escanearla desde el móvil.

#### Scenario: Mostrar el QR
- **WHEN** el usuario pulsa *Mostrar código QR* con el puerto escuchando en la red local
- **THEN** se muestra un QR que decodifica `dirección:puerto` y el texto de la dirección debajo

### Requirement: Ticket de prueba ESC/POS
La sección SHALL ofrecer *Imprimir ticket de prueba*, que envíe un ticket ESC/POS real al propio puerto por TCP, de modo que compruebe la escucha, la detección del contenido, el intérprete y la cola. Si no se puede conectar, SHALL decir el motivo (puerto desactivado, ocupado o rechazado).

#### Scenario: Prueba correcta
- **WHEN** el puerto escucha y el usuario pulsa *Imprimir ticket de prueba*
- **THEN** se imprime un ticket con cabecera, estilos, columnas, un QR y el corte, y el trabajo aparece con origen Puerto 9100

#### Scenario: Puerto desactivado
- **WHEN** la impresión directa está desactivada
- **THEN** el botón no está disponible y se explica que hay que activarla y aplicarla

#### Scenario: Puerto ocupado
- **WHEN** el puerto está ocupado por otro programa
- **THEN** la prueba indica que no se pudo conectar y por qué

### Requirement: Registro de los últimos clientes
La sección SHALL mostrar los últimos 50 clientes que se han conectado al puerto 9100 desde que arrancó el servicio, con la hora, la dirección, lo que enviaron (ESC/POS, PNG, JPEG, PDF, PWG, texto o desconocido), su tamaño, cuántos tickets o qué trabajo generaron y el resultado (en cola, rechazado, cortado por un límite, error). Los datos SHALL conservarse solo en memoria y no incluir el contenido enviado.

#### Scenario: Un cliente imprime
- **WHEN** un cliente en 192.168.1.30 imprime un ticket ESC/POS
- **THEN** aparece una fila con su hora, `192.168.1.30`, «ESC/POS», sus bytes y «En cola»

#### Scenario: Cliente rechazado
- **WHEN** un cliente envía contenido que no se reconoce
- **THEN** aparece una fila con «Desconocido» y «Rechazado»

#### Scenario: Superó un límite
- **WHEN** una conexión se corta por tamaño, tiempo o papel
- **THEN** la fila muestra «Cortado por un límite» con cuál

#### Scenario: Reinicio del servicio
- **WHEN** el servicio se reinicia
- **THEN** la lista empieza vacía

#### Scenario: Más de 50 clientes
- **WHEN** se conectan más de 50 clientes
- **THEN** se muestran solo los 50 más recientes
