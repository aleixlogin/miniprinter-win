## Why

La mini impresora térmica **X5h-E07A** solo se puede usar desde apps propietarias (Tiny Print) o desde herramientas sueltas como TiMini-Print. Queremos que se comporte como **una impresora normal de Windows**: que aparezca en "Impresoras y escáneres" y que cualquier aplicación (Bloc de notas, navegador, Word, Fotos…) pueda imprimir en ella desde el diálogo estándar, con un panel de control propio para buscarla, conectarla y ver su estado.

La exploración del 2026-10-01 dejó el protocolo resuelto y verificado sobre el hardware real (ver `design.md` → Context), así que el riesgo técnico principal ya está despejado.

## What Changes

- **Servicio de Windows "MiniPrinter"** (.NET 8) que se presenta a Windows como impresora **IPP Everywhere**. Windows la instala con su *Microsoft IPP Class Driver* integrado: no hay que escribir ni firmar un driver de kernel/V4. El servicio traduce cada trabajo recibido al protocolo de la impresora.
- **Alcance de red elegible** desde el panel: *solo este PC* (escucha en loopback) o *red local* (escucha en la LAN y se anuncia por mDNS `_ipp._tcp`, para que otros PCs, iPhone y Android la descubran solos).
- **Codificador del protocolo `tiny`** con la receta del perfil `d1` (modelo `pocket_printer`), al que corresponde la X5h: tramas `51 78 … CRC8 FF`, filas RLE `0xBF` o sin comprimir `0xA2`, energía, velocidad, avance y consulta de estado. Validado byte a byte contra TiMini-Print.
- **Transporte Bluetooth clásico (SPP/RFCOMM)** mediante WinRT, con la impresora identificada por nombre o MAC (no por un número de COM fijo). Puerto COM como alternativa. Bloques de 180 bytes cada 4 ms y control de flujo por notificaciones `AE` de pausa/reanudación.
- **Pipeline de rasterizado**: página PWG Raster/JPEG/PNG → 384 px de ancho, recorte de blanco y dithering (Floyd–Steinberg, Atkinson o umbral).
- **App de escritorio con icono en la bandeja** (WPF) que habla con el servicio: buscar impresoras, emparejar, conectar y desconectar, ver estado (papel, tapa, temperatura, batería) y firmware, gestionar la cola, imprimir una página de prueba y ajustar oscuridad, dithering, avance y alcance de red.
- **API de control local** entre la bandeja y el servicio, que nunca se expone a la red aunque el modo LAN esté activo.
- **Instalador/desinstalador** que registra el servicio, crea la cola "X5h Thermal Printer" e instala la app de bandeja con inicio automático.
- **CLI de diagnóstico** (`probe`, `test-print`, `print`) para validar transporte y protocolo sin pasar por la cola de Windows.

## Impact

- **Código nuevo**: solución .NET 8 en `src/` (Protocol, Transport, Imaging, Ipp, Control, Service, Tray, Cli) y `tests/`.
- **Dependencias**: API WinRT de Bluetooth (`Windows.Devices.Bluetooth.Rfcomm`, `Windows.Devices.Enumeration`, `Windows.Networking.Sockets`; TFM `net8.0-windows10.0.19041.0`), `System.IO.Ports`, ImageSharp para escalado y decodificación, una librería mDNS (p. ej. `Makaretu.Dns.Multicast`) para el modo LAN.
- **Sistema**: un Windows Service, una cola de impresión IPP y una app de bandeja por usuario. La instalación requiere administrador. Es compatible con el modo *Windows protected print* de Windows 11 (solo IPP).
- **Red**: en modo LAN se abre un puerto TCP (IPP) en el firewall para el perfil de red privada. En modo local no se abre nada.
- **Hardware**: mientras el servicio mantiene la conexión, ni la app móvil ni TiMini-Print pueden conectarse. Se mitiga desconectando por inactividad.
- **Referencias y licencias**: la receta `d1` y el catálogo de modelos provienen de [Dejniel/TiMini-Print](https://github.com/Dejniel/TiMini-Print) (Apache-2.0): se portan con atribución en `NOTICE`. [lisp3r/bluetooth-thermal-printer](https://github.com/lisp3r/bluetooth-thermal-printer) (sin licencia, modelo GT01) se usa solo como conocimiento del protocolo, sin copiar código.
