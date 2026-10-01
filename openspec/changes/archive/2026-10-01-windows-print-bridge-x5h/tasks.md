## 1. Base del proyecto

- [x] 1.1 Inicializar repositorio git con `.gitignore` de .NET, `LICENSE` propio y `NOTICE` con la atribución a TiMini-Print (Apache-2.0)
- [x] 1.2 Crear `MiniPrinter.sln` con los proyectos `Protocol`, `Transport`, `Imaging`, `Ipp`, `Control`, `Service`, `Tray` (WPF), `Cli` y los de test xUnit (`net8.0-windows10.0.19041.0` donde haga falta WinRT)
- [x] 1.3 Portar el catálogo de modelos de la familia `tiny` a `models.json`/`profiles.json` (como mínimo `pocket_printer`→`d1` y `x6h`), con detección por prefijo que distinga mayúsculas y test `X5h-E07A`→`d1`, `X5H-ABCD`→`x6h`
- [x] 1.4 Generar con `tools/debug_protocol_job.py` de TiMini (venv aislado) los fixtures de referencia en `tests/fixtures/`: texto "Hola X5h", una imagen de prueba en modo imagen, una página en blanco y una imagen de más de 200 filas

## 2. Protocolo

- [x] 2.1 Implementar CRC-8 (polinomio 0x07) con tests: `E0 2E`→`89`, `30 00`→`F9`, `33`→`99`, `00 0E 28`→`0E`, payload de `A8` real→`6F`
- [x] 2.2 Implementar `Frame.Encode` y los comandos tipados (`A4`, `AF`, `BE`, `BD`, `A1`, `A0`, `A3`, `A8`, `BB`) con tests byte a byte
- [x] 2.3 Implementar el encoder de filas: RLE `BF` (`color<<7 | racha`, rachas ≤127, reglas de fila blanca/fila con tinta) y `A2` (LSB = píxel izquierdo), eligiendo `BF` si ocupa ≤ 48 bytes
- [x] 2.4 Implementar la receta de trabajo `d1` (cabecera, filas con `BD` cada 200, cierre `BD 0C · A1×2 · BD 0C · A3`) y comparar byte a byte con todos los fixtures de 1.4
- [x] 2.5 Implementar el decodificador de tramas entrantes (tolerante a tramas partidas o concatenadas, valida CRC y `FF`) y los parsers de `A3` (alarmas + batería en crudo), `A8` (firmware + bytes en crudo), `BB` y `AE` (pausa/reanudar)

## 3. Transporte

- [x] 3.1 Definir `ITransport` (conectar, escribir, flujo de tramas entrantes, desconectar, estado) y el escritor troceado (180 B / 4 ms) que respeta pausas `AE`, con tests sobre un transporte simulado
- [x] 3.2 Implementar `SerialTransport` (`System.IO.Ports`) con resolución del COM saliente a partir de la MAC (WMI `Win32_PnPEntity`) y traducción de `ERROR_DUP_NAME` a "impresora ocupada por otra aplicación"
- [x] 3.3 Implementar `RfcommTransport` (WinRT `RfcommDeviceService` SPP + `StreamSocket`) por id de dispositivo o MAC
- [x] 3.4 Crear la CLI `miniprinter probe` (`A3`/`A8`/`BB` decodificados) por ambos transportes y comprobar que da `00 0E 28` / firmware `3.0.5` en la X5h
- [x] 3.5 Comprobar si `RfcommTransport` funciona ejecutándose como servicio (sesión 0); anotar el resultado en `design.md` y elegir el transporte por defecto
- [x] 3.6 Implementar conexión bajo demanda, reintentos con espera exponencial, desconexión por inactividad (60 s) y sondeo de `A3` cada 30 s mientras está conectada

## 4. Experimentos con el hardware

- [x] 4.1 `probe` sin papel, con la tapa abierta y tras imprimir mucho seguido: registrar el byte de alarmas de `A3` y confirmar o corregir el mapa de bits de D9
- [x] 4.2 `probe` con la impresora cargando y con poca batería: confirmar si `0E 28` es la tensión en mV y definir la conversión a porcentaje
- [x] 4.3 Imprimir con `miniprinter test-print` una regla de calibración (marcas cada 8 px) y un degradado: confirmar 384 px útiles, orden de bits y oscuridad/energía adecuadas
- [x] 4.4 Imprimir un trabajo largo (más de 1000 filas) registrando las tramas `AE` recibidas: confirmar el control de flujo y ajustar bloque y retardo
- [x] 4.5 Actualizar `design.md` (tabla de mensajes y Open Questions) con los resultados

## 5. Rasterizado

- [x] 5.1 Implementar `PwgRasterReader` (`RaS2`, cabeceras de página, PackBits por línea) para `sgray_8` y `srgb_8`, con test sobre un fichero PWG generado
- [x] 5.2 Añadir decodificación de JPEG/PNG (ImageSharp)
- [x] 5.3 Implementar el escalado proporcional a 384 px y el recorte de filas blancas iniciales y finales con margen configurable
- [x] 5.4 Implementar dithering Atkinson, Floyd–Steinberg y umbral, con ajuste de brillo y contraste, y tests de proporción de negros
- [x] 5.5 Crear la CLI `miniprinter print <archivo>` (rasterizado + receta + transporte) y probar con una foto y con una página de texto

## 6. Servidor IPP y alcance de red

- [x] 6.1 Evaluar `SharpIppNext` como codec de servidor; si no encaja, implementar un codec IPP propio con tests de ida y vuelta
- [x] 6.2 Implementar `Get-Printer-Attributes` (PWG Raster `sgray_8` 203 dpi, JPEG/PNG, medio de 58 mm, monocromo, una cara, `printer-state` y `printer-state-reasons` derivados del estado de la impresora)
- [x] 6.3 Implementar `Validate-Job`, `Print-Job`, `Create-Job`/`Send-Document`, `Get-Job-Attributes`, `Get-Jobs` y `Cancel-Job`, y `operation-not-supported` para el resto
- [x] 6.4 Implementar `JobQueue` FIFO (un worker, historial de 20, retención si `A3` reporta alarma, cancelación entre filas con cierre limpio)
- [x] 6.5 Implementar el modo de red elegible: listener en loopback o LAN, anuncio mDNS `_ipp._tcp` con TXT, regla de firewall en el perfil privado y cambio de modo aplicado con la cola vacía
- [x] 6.6 Validar con `ipptool` (atributos e `ipp-everywhere.test` básico) y añadir la impresora en Windows por URL IPP para confirmar que se instala con "Microsoft IPP Class Driver"
- [x] 6.7 En modo LAN, comprobar que otro equipo o un móvil descubren la impresora e imprimen

## 7. Servicio y API de control

- [x] 7.1 Montar el host del Windows Service (`UseWindowsService`, Kestrel con IPP en 8631 y API en `127.0.0.1:8632`), configuración y logs en `%ProgramData%\MiniPrinter`
- [x] 7.2 Implementar el gestor de impresora (impresora elegida, perfil, transporte, estado, batería, firmware) compartido por IPP y la API
- [x] 7.3 Implementar la API de control (`status`, `printer`, `connect`, `disconnect`, `test-print`, `feed`, `settings`, `jobs`, `events` por SSE) con contratos en `MiniPrinter.Control` y autenticación por token de fichero con ACL
- [x] 7.4 Tests de integración de la API con un transporte simulado

## 8. App de bandeja (WPF)

- [x] 8.1 Icono en la bandeja con estado (conectada, desconectada, imprimiendo, alarma) y menú rápido (conectar/desconectar, prueba, abrir panel, salir)
- [x] 8.2 Ventana "Buscar impresoras": `DeviceWatcher` de dispositivos Bluetooth cercanos y emparejados, etiquetados con el catálogo, botón emparejar (`PairAsync`) y botón usar esta impresora
- [x] 8.3 Panel de estado: papel, tapa, temperatura, batería, firmware y cola de trabajos (cancelar) en vivo por SSE
- [x] 8.4 Ajustes: oscuridad, dithering por defecto, avance final, tiempo de desconexión por inactividad y alcance de red (solo este PC / red local)
- [x] 8.5 Notificaciones de Windows para alarmas (sin papel, tapa abierta, batería baja, impresora ocupada por otra app)
- [x] 8.6 Asistente de primer arranque: buscar → emparejar → elegir → prueba de impresión

## 9. Instalación e integración con Windows

- [x] 9.1 Escribir `scripts/install.ps1` (administrador): publicar y copiar, crear `%ProgramData%`, registrar y arrancar el servicio, crear la cola "X5h Thermal Printer" con el método validado en 6.6 y registrar la bandeja en el inicio de sesión
- [x] 9.2 Escribir `scripts/uninstall.ps1` (cola, puerto, firewall, servicio, inicio de la bandeja; opción `-KeepConfig`)
- [x] 9.3 Prueba de extremo a extremo: imprimir desde Bloc de notas, Edge y Fotos; cancelar un trabajo en curso; imprimir sin papel o con la impresora apagada; imprimir con TiMini-Print abierto (ocupada); cambiar a modo LAN e imprimir desde otro dispositivo
- [x] 9.4 Redactar `README.md` con requisitos, instalación, uso del panel, modos de red, solución de problemas y créditos
