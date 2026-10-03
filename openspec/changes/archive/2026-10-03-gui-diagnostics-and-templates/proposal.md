## Why

Cuando algo no funciona, la bandeja no ayuda a saber qué: las causas probables están en una tabla del README (Bluetooth apagado, impresora sin emparejar, servicio detenido, cola de Windows sin crear, puertos ocupados, firewall) y hay que ir comprobándolas a mano. Además, el puerto 9100 se activa y no hay forma cómoda de usarlo desde la propia aplicación: hay que ir a buscar la IP, escribir la dirección en el móvil y no se ve quién se ha conectado. Y la pestaña *Plantillas* es una lista desplegable que obliga a elegir una plantilla para saber cómo es, sin atajos para las que se usan cada día.

Este change completa la renovación de la interfaz (después de `gui-foundation` y `gui-settings-and-status`) con tres bloques de ayuda al uso.

## What Changes

- **Diagnóstico**: un botón *Diagnóstico* (en el menú «⋯» de *Estado* y como acción sugerida de la tarjeta de estado cuando algo falla) que muestra una lista de comprobaciones con semáforo (correcto, aviso, error, desconocido), el detalle y qué hacer en cada una: Bluetooth encendido, impresora emparejada, servicio iniciado, conexión con la impresora, papel y alarmas, cola de Windows creada y apuntando al servicio, escucha IPP (8631), puerto 9100 (activo, escuchando, regla de firewall si hay red local) y fuentes del sistema para CJK, árabe y tailandés. Botón *Copiar informe* con un texto sin secretos (versión, ajustes resumidos, resultados) para pegar en una incidencia.
- **Puerto 9100 en la interfaz**: en la sección *Impresión directa*, *Copiar dirección*, *Mostrar código QR* de la dirección para escanearla desde el móvil, *Imprimir ticket de prueba* (un ESC/POS real enviado por el propio puerto, para comprobar el camino completo) y una lista de los **últimos clientes** que se han conectado (hora, dirección, qué enviaron, cuántos tickets y el resultado, incluidos los rechazados o los que superaron un límite).
- **Plantillas en galería**: la lista pasa a una cuadrícula de tarjetas con miniatura (la plantilla dibujada con valores de ejemplo), con alternar entre galería y lista, buscador, y las usadas recientemente primero.
- **Plantillas desde el icono de la bandeja**: un submenú *Plantillas* con *Favoritos* (imprimen al instante la plantilla con sus valores guardados) y *Recientes* (abren la pestaña con la plantilla y sus últimos valores, sin imprimir).

## Capabilities

### New Capabilities
- `diagnostics-panel`: las comprobaciones, su presentación con semáforo y el informe copiable.
- `raw-port-gui`: copiar dirección, código QR, ticket de prueba y registro de clientes del puerto 9100.
- `template-gallery`: la cuadrícula con miniaturas, el buscador y las recientes de la pestaña *Plantillas*.
- `tray-quick-templates`: el submenú de plantillas del icono de la bandeja.

### Modified Capabilities
<!-- Ninguna: los requisitos existentes del puerto 9100 y de las plantillas se cumplen igual; esto añade formas de usarlos. -->

## Impact

- **Código**:
  - `MiniPrinter.Control`: contratos `DiagnosticCheckDto`, `RawClientDto` y los puntos nuevos del cliente (`GetDiagnosticsAsync`, `GetRawClientsAsync`, `SendRawTestAsync`, `GetTemplateThumbnailAsync`).
  - `MiniPrinter.Service`: `ControlApi` (`GET /api/diagnostics`, `GET /api/raw-port/clients`, `POST /api/raw-port/test`, `GET /api/templates/{name}/thumbnail`), un `DiagnosticsService`, el registro de clientes en `RawPortHost` y las miniaturas en `TemplateEndpoints`.
  - `MiniPrinter.Gui`: modelos de vista de diagnóstico, del puerto 9100, de la galería y del menú de plantillas.
  - `MiniPrinter.Tray`: diálogo de diagnóstico, comprobaciones propias (Bluetooth, emparejada, servicio alcanzable), QR (ZXing, ya en el repositorio), galería de plantillas y submenú del icono.
- **Datos**: la bandeja guarda en `tray.json` la lista de plantillas recientes y la vista (galería o lista); el servicio guarda en memoria los últimos 50 clientes del 9100 (no se escriben a disco).
- **Seguridad**: el informe y el registro de clientes no incluyen el token, las plantillas ni el contenido impreso; el registro de clientes enseña direcciones IP de la red local.
- **Depende de** `gui-foundation` y `gui-settings-and-status` (secciones de *Ajustes*, tarjeta de estado y menú «⋯»).
- **Fuera de alcance**: una lista de direcciones permitidas para el 9100 (sería un change de seguridad), anunciar el 9100 por mDNS, y exportar o importar paquetes de plantillas.
