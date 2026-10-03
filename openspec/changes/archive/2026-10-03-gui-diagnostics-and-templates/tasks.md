## 1. Servicio y contratos (diagnostics-panel, raw-port-gui, template-gallery)

- [x] 1.1 `DiagnosticsService` con las comprobaciones del servicio (enlace y alarmas, cola de Windows, escucha IPP, puerto 9100 y regla de firewall, fuentes) y `GET /api/diagnostics`; una comprobación que no se completa es *desconocido* y todas con un tiempo máximo; tests de cada estado
- [x] 1.2 Registro de los últimos 50 clientes del 9100 en `RawPortHost` (hora, dirección, tipo, bytes, tickets o trabajo, resultado, incluidos rechazados y cortados por un límite) y `GET /api/raw-port/clients`; tests con clientes TCP reales
- [x] 1.3 `POST /api/raw-port/test`: generar un ticket ESC/POS y enviarlo por TCP al propio puerto, con mensajes de error claros (desactivado, ocupado, rechazado); test con el servicio de pruebas
- [x] 1.4 `GET /api/templates/{name}/thumbnail?width=` con valores de ejemplo en modo tolerante, marcador para imágenes sin valor, caché por huella e invalidación al guardar o borrar; 404 si no existe; tests
- [x] 1.5 Contratos (`DiagnosticCheckDto`, `RawClientDto`) y métodos de `ControlClient` (`GetDiagnosticsAsync`, `GetRawClientsAsync`, `SendRawTestAsync`, `GetTemplateThumbnailAsync`)

## 2. Diagnóstico en la bandeja

- [x] 2.1 Comprobaciones de la bandeja: servicio alcanzable, radio Bluetooth encendida (`Windows.Devices.Radios`) e impresora emparejada (reutilizando `BluetoothScanner`)
- [x] 2.2 `DiagnosticsViewModel`: combina servicio y bandeja, ordena, calcula el resumen y los textos «qué hacer» (recursos), con la regla «no se pudo comprobar = desconocido»; tests con una tabla de casos
- [x] 2.3 Informe copiable sin secretos (versiones, Windows, ajustes resumidos, resultados) con test de que no aparecen el token, plantillas ni direcciones de clientes
- [x] 2.4 Diálogo de diagnóstico con semáforos y botones que llevan a la solución; abrirlo desde el menú «⋯» de *Estado* y desde el botón *Diagnosticar* de la tarjeta de estado

## 3. Puerto 9100 en la interfaz (raw-port-gui)

- [x] 3.1 Modelo de vista del bloque: direcciones útiles (loopback o de red según el modo), copiar con elección si hay varias, y disponibilidad de las acciones según esté activado y escuchando; tests
- [x] 3.2 Ventana del código QR (ZXing) con la dirección debajo
- [x] 3.3 *Imprimir ticket de prueba* con el resultado o el motivo del fallo, y lista de los últimos clientes con refresco; añadirlo a la sección *Impresión directa*

## 4. Plantillas (template-gallery, tray-quick-templates)

- [x] 4.1 Galería: modelo de vista (tarjetas, búsqueda, recientes primero, miniaturas pedidas en segundo plano con límite de dos a la vez y marcador), vista de cuadrícula y alternar con la lista recordando la elección en `tray.json`
- [x] 4.2 Lista `RecentTemplates` en `tray.json` (últimas 5, la más reciente primero) actualizada al imprimir desde la pestaña
- [x] 4.3 Submenú *Plantillas* del icono de la bandeja: *Favoritos* (imprimen al instante con aviso y error claro si ya no son válidos; agrupados por plantilla con más de 15) y *Recientes* (abren la pestaña con sus últimos valores, sin imprimir); tests del modelo del menú

## 5. Comprobación y cierre

- [x] 5.1 Ejecutar todas las pruebas; comprobación visual por el usuario con el instalador (diagnóstico con el Bluetooth apagado y con el servicio parado, QR y ticket de prueba, galería, favoritos desde el icono)
- [x] 5.2 README (diagnóstico, puerto 9100 en la interfaz, galería, menú del icono) e historial de cambios
