## ADDED Requirements

### Requirement: Impresora estándar de Windows
El sistema SHALL exponer la impresora térmica como una cola de impresión de Windows instalada con el "Microsoft IPP Class Driver", sin drivers de terceros, de forma que cualquier aplicación pueda imprimir en ella desde el diálogo estándar.

#### Scenario: Imprimir desde una aplicación
- **WHEN** el usuario imprime desde una aplicación (Bloc de notas, Edge, Fotos o la página de prueba de Windows) en la cola "X5h Thermal Printer"
- **THEN** el trabajo llega al servicio, se imprime en la X5h y termina en estado `completed`

#### Scenario: Tamaños de papel ofrecidos
- **WHEN** Windows consulta los atributos de la impresora
- **THEN** ofrece tamaños de 48 mm de ancho (48×210 por defecto) a 203 dpi y tamaños virtuales de 80 mm

### Requirement: Protocolo de la X5h
El sistema SHALL codificar los trabajos con la receta del perfil `d1` de la familia `tiny`: tramas `51 78 | cmd | flags | len | payload | crc8 | FF`, cabecera `A4 · AF · BE · BD`, filas `BF` (RLE) cuando ocupan como máximo 48 bytes o `A2` en otro caso, `BD` cada 200 filas y cierre `BD 0C · A1 30 00 ×2 · BD 0C · A3 00`.

#### Scenario: Coincidencia con la referencia
- **WHEN** se genera un trabajo para los rasters de referencia
- **THEN** los bytes coinciden exactamente con los producidos por TiMini-Print para "X5h-E07A"

### Requirement: Estado de la impresora
El sistema SHALL interpretar la respuesta `A3` como `[alarmas] [sensor de papel] [batería]` y reflejar el bit `0x01` como "sin papel" (incluye tapa abierta).

#### Scenario: Sin papel o tapa abierta
- **WHEN** la impresora responde `01 1B 27`
- **THEN** el estado indica sin papel, IPP informa `media-empty-error` y los trabajos quedan retenidos hasta que vuelve a `00`

### Requirement: Transporte Bluetooth y control de flujo
El sistema SHALL conectar por RFCOMM (WinRT) o por el puerto COM asociado, enviar en bloques de 180 bytes cada 4 ms y detener el envío ante la notificación `AE 10` hasta recibir `AE 00`.

#### Scenario: Trabajo largo
- **WHEN** la impresora envía pausas durante un trabajo largo
- **THEN** no se escriben datos durante la pausa y el trabajo se completa sin perder filas

#### Scenario: Impresora ocupada o apagada
- **WHEN** otra aplicación tiene la conexión o la impresora está apagada
- **THEN** el trabajo espera (hasta el tiempo configurado) y se imprime al quedar disponible

### Requirement: Gestión de la conexión
El sistema SHALL conectar bajo demanda y desconectar tras un tiempo de inactividad configurable (60 s por defecto) sin que la impresora deje de anunciarse como disponible.

#### Scenario: Inactividad en modo red local
- **WHEN** pasan 60 s sin trabajos con el modo red local activo
- **THEN** el enlace Bluetooth se libera y IPP sigue respondiendo `printer-state = idle` sin motivos de error

### Requirement: Cola de trabajos
El sistema SHALL procesar los trabajos de uno en uno en orden FIFO, permitir cancelarlos y dejar la impresora en un estado limpio al cancelar.

#### Scenario: Cancelación en curso
- **WHEN** el usuario cancela un trabajo mientras se imprime
- **THEN** se dejan de enviar filas, se envía el cierre de página y el trabajo queda `canceled`

### Requirement: Panel de control
El sistema SHALL ofrecer una app de bandeja que permita buscar y emparejar impresoras, elegir la impresora, ver estado y cola, imprimir una prueba, ver la vista previa del último trabajo y cambiar los ajustes, comunicándose con el servicio solo a través de una API en `127.0.0.1` protegida por token.

#### Scenario: Guardar ajustes no cambia la impresora
- **WHEN** el panel guarda los ajustes con una copia antigua que no incluye la impresora seleccionada
- **THEN** la impresora seleccionada se conserva

### Requirement: Alcance de red elegible
El sistema SHALL permitir elegir entre imprimir solo desde este PC (IPP en loopback) o desde la red local (IPP en todas las interfaces, anuncio mDNS `_ipp._tcp` y regla de firewall en redes privadas), sin exponer nunca la API de control a la red.

#### Scenario: Imprimir desde un móvil
- **WHEN** el modo red local está activo
- **THEN** un móvil de la misma red descubre "X5h Thermal Printer" e imprime en ella
