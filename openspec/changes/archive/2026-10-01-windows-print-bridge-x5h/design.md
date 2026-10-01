## Context

### La impresora (verificado el 2026-10-01)

| Dato | Valor |
|---|---|
| Nombre Bluetooth | `X5h-E07A` |
| MAC | `7A:E0:0C:1D:87:AE` |
| Tipo | Bluetooth **clásico**, perfil SPP (Windows le asigna **COM5**). No expone GATT/BLE. |
| Modelo en TiMini-Print | `pocket_printer` → perfil **`d1`**, familia **`tiny`** (app de origen `com.frogtosea.tinyPrint`) |
| Firmware | `3.0.5` (respuesta a `A8`) |

Atención: `X5H-…` (H mayúscula) es otro modelo (perfil `x6h`, energía 9500). La detección distingue mayúsculas.

### Fuentes y cómo se usaron

- **lisp3r/bluetooth-thermal-printer**: ingeniería inversa del **GT01** con iPrint (BLE). Sirvió para entender el formato de trama, el CRC-8 y la compresión RLE: se decodificó completa su captura real (162 tramas, todos los CRC correctos, 150 filas de 384 px). **Su receta de trabajo no vale para la X5h** (usa `A6` de inicio/fin, energía 12000, BLE con MTU 123). La APK de iPrint que incluye no conoce ningún modelo X5. No tiene licencia: no se copia código.
- **Dejniel/TiMini-Print** (Apache-2.0): conoce la X5h. De aquí salen la receta `d1`, el transporte SPP y el control de flujo. Su herramienta `tools/debug_protocol_job.py --bluetooth-name "X5h-E07A"` genera trabajos de referencia sin conectar a la impresora.
- **Prueba sobre el hardware**: se enviaron `A3`, `A8` y `BB` por COM5 y la impresora respondió con tramas válidas (CRC correcto, `flags = 01`).

### Protocolo de la X5h

**Trama**: `51 78 | cmd | flags | len_lo len_hi | payload | crc8(payload) | FF`

- `flags`: `00` del PC a la impresora, `01` de la impresora al PC.
- CRC-8: polinomio 0x07, valor inicial 0, calculado solo sobre el payload.

**Comandos que envía el PC:**

| cmd | Nombre | Payload |
|---|---|---|
| `A4` | Oscuridad | `30 + nivel` (nivel 1..5, por defecto 3 → `33`) |
| `AF` | Energía | uint16 LE: imagen 5000 (`88 13`), texto 8000 (`40 1F`) |
| `BE` | Modo | `00` imagen, `01` texto |
| `BD` | Velocidad / relleno de avance | 1 byte (10 = `0A` al imprimir, 12 = `0C` al final) |
| `A2` | Fila sin comprimir | 48 bytes, 1 bit por píxel, **píxel izquierdo en el bit 0**, 1 = negro |
| `BF` | Fila RLE | bytes `color<<7 \| racha` (racha 1..127) |
| `A1` | Avance de papel | uint16 LE puntos (`30 00` = 48) |
| `A0` | Retroceso de papel | uint16 LE |
| `A3` | Consultar estado | `00` |
| `A8` | Consultar info | `00` |
| `BB` | Consultar ID | `01` |

**Receta de un trabajo** (coincide byte a byte con TiMini; vector de referencia de 29 tramas para el texto "Hola X5h"):

```
A4 33 · AF <energía> · BE <modo> · BD 0A
  por cada fila: BF si el RLE ocupa ≤ 48 bytes, si no A2
                 (+ BD 0A cada 200 filas)
BD 0C · A1 30 00 · A1 30 00 · BD 0C · A3 00
```

Una fila blanca se codifica como `BF 7F 7F 7F 03` (127+127+127+3 = 384). Una fila con tinta pero sin rachas al final emite la racha final; una fila sin tinta emite sus rachas blancas.

**Mensajes que envía la impresora:**

| cmd | Payload observado | Interpretación |
|---|---|---|
| `A3` | `00 0E 27` / `01 1B 27` | `[alarmas] [sensor de papel] [batería]`. **Verificado (2026-10-01):** bit `0x01` = sin papel; la tapa abierta da lo mismo (no hay bit de tapa). Byte 1 = lectura en bruto del sensor óptico de papel (≈14–15 con papel, ≈24–27 sin él; umbral ≈0x17). Byte 2 = indicador de batería (39–40 observado; unidad sin confirmar: % o décimas de voltio). Hipótesis descartada: «bytes 1–2 = batería en mV». |
| `A8` | `78 00 03 "3.0.5" 44 00` | firmware "3.0.5". Los bytes `78 00`, `03` y `44 00` aún no se conocen. |
| `BB` | `00 ×6` | sin ID de dispositivo grabado |
| `AE` | `10` / `00` (flags `01`) | **pausa / reanudar** el envío. **Verificado:** en un trabajo de 1200 filas llegaron 4 pares pausa/reanudar y el envío los respetó sin perder filas (39,6 KB en 11,2 s). |

**Transporte**: SPP/RFCOMM, el trabajo se escribe en bloques de **180 bytes con 4 ms** de pausa, respetando las pausas `AE`.

## Goals / Non-Goals

**Goals:**
- Que la X5h aparezca como impresora estándar en Windows 11 y se pueda imprimir desde cualquier aplicación.
- Panel de control (app de bandeja) para buscar, emparejar, conectar, ver estado y configurar.
- Alcance de red elegible: solo este equipo o toda la LAN, con descubrimiento automático en LAN.
- Sin drivers de kernel ni firma de código.
- Protocolo, rasterizado e IPP en librerías testeables sin hardware.
- Diseño preparado para añadir otros perfiles de la familia `tiny` (catálogo de modelos), aunque solo se valide `d1`.

**Non-Goals:**
- Driver de kernel, UMDF o driver de impresión V3/V4.
- Transporte BLE/GATT (la X5h no lo usa). Puede añadirse más adelante tras la interfaz `ITransport`.
- Escala de grises real (el perfil `d1` solo admite 1 bit).
- Funciones de las apps móviles (Wi-Fi, OTA, escribir device-id, nube, plantillas de etiquetas).
- Soporte de otras familias de protocolo de TiMini (luck, v5x, niimbot, phomemo…).
- macOS/Linux.

## Decisions

### D1. Impresora IPP Everywhere en vez de driver de Windows
El servicio implementa un servidor IPP mínimo y Windows lo instala con el **Microsoft IPP Class Driver**.
- *Por qué*: viene con Windows, no requiere firma y Microsoft está retirando los drivers de terceros. El modo *Windows protected print* solo admite IPP. Además, el mismo servidor sirve a otros dispositivos de la LAN.
- *Descartado*: driver V4 + port monitor (firma, obsoleto); cola "Generic / Text Only" a COM5 (la impresora no entiende ESC/POS ni texto); Print Support App (MSIX, sigue necesitando un dispositivo IPP).

### D2. Stack y estructura: .NET 8 (C#)
`net8.0-windows10.0.19041.0` para WinRT Bluetooth, `System.IO.Ports`, `Microsoft.Extensions.Hosting.WindowsServices` y WPF.

```
src/
  MiniPrinter.Protocol/   tramas, CRC8, comandos, encoder de filas, receta de trabajo, parser
  MiniPrinter.Transport/  ITransport, RfcommTransport (WinRT), SerialTransport, flujo, troceado
  MiniPrinter.Imaging/    PwgRasterReader, decodificación JPEG/PNG, escalado, recorte, dithering
  MiniPrinter.Ipp/        codec IPP, operaciones, atributos, JobQueue
  MiniPrinter.Control/    contratos de la API de control (DTOs) compartidos por el servicio y la bandeja
  MiniPrinter.Service/    host del Windows Service: IPP, API de control, mDNS, gestor de impresora
  MiniPrinter.Tray/       app WPF de bandeja
  MiniPrinter.Cli/        probe / test-print / print / raw
tests/   xUnit: Protocol (vectores de referencia), Imaging, Ipp, Transport (simulado)
scripts/ install.ps1, uninstall.ps1
```

*Descartado*: Python sobre `timiniprint` (más modelos gratis, pero peor servicio de Windows y una API ajena que cambia a menudo).

### D3. Protocolo: portar la receta `d1` con perfiles de modelo en datos
La receta y los parámetros se portan desde TiMini (con atribución Apache-2.0 en `NOTICE`). Los parámetros van en un catálogo JSON (`models.json`: prefijos de nombre → perfil; `profiles.json`: ancho, dpi, energía, velocidad, avance, bloque y retardo). La detección distingue mayúsculas (`X5h` → `d1`, `X5H` → `x6h`).
Los **tests de protocolo comparan byte a byte** con trabajos generados por `tools/debug_protocol_job.py` de TiMini y guardados como fixtures en `tests/fixtures/`.

### D4. Transporte: RFCOMM vía WinRT, con COM como alternativa
- `RfcommTransport`: `RfcommDeviceService` (servicio SPP `00001101-0000-1000-8000-00805F9B34FB`) + `StreamSocket`. Identifica la impresora por su id de dispositivo o MAC, así que no depende de qué número de COM le haya tocado.
- `SerialTransport`: `System.IO.Ports` sobre el COM saliente (COM5 aquí). Es la alternativa probada en hardware y la salida si RFCOMM-WinRT falla desde la sesión 0 del servicio (ver Risks).
- Ambos: escritura troceada en 180 B / 4 ms, lector asíncrono que pasa los bytes a un decodificador de tramas (tolerante a tramas partidas o concatenadas).
- Error conocido: si otra app tiene la conexión (TiMini, app móvil), abrir falla con "nombre duplicado en la red" (`ERROR_DUP_NAME`). Se traduce a "impresora ocupada por otra aplicación".

### D5. Búsqueda y emparejamiento desde la bandeja; conexión desde el servicio
- **Bandeja** (sesión del usuario): `DeviceWatcher` sobre `BluetoothDevice.GetDeviceSelectorFromPairingState(false/true)` para listar impresoras cercanas y emparejadas, filtradas y etiquetadas con el catálogo. El emparejamiento (`DeviceInformationPairing.PairAsync`) se hace aquí porque puede requerir interacción del usuario.
- **Servicio**: recibe la impresora elegida (id + MAC + perfil) por la API de control, la guarda en su configuración y gestiona la conexión.
- *Por qué separar*: el emparejamiento con UI no funciona en la sesión 0; la conexión sí debe vivir en el servicio para imprimir sin usuario logueado ni bandeja abierta.

### D6. API de control bandeja ↔ servicio: HTTP JSON solo en loopback
Un endpoint en `http://127.0.0.1:8632/api/…` servido por el mismo Kestrel que IPP, **siempre en loopback aunque el modo LAN esté activo**. Por eso no hace falta contraseña: desde la red no se llega al panel. Se protege con un token aleatorio en `%ProgramData%\MiniPrinter\control.token`, legible solo por usuarios locales (ACL), para que otras webs del navegador no puedan llamarlo (anti-CSRF). Para el estado en vivo se usa SSE (`/api/events`).

Endpoints: `GET /status`, `POST /printer` (elegir), `POST /connect`, `POST /disconnect`, `POST /test-print`, `POST /feed`, `GET/PUT /settings`, `GET /jobs`, `DELETE /jobs/{id}`, `GET /events`.

*Descartado*: named pipes (WPF + Kestrel ya presentes; HTTP es más fácil de depurar con curl).

### D7. Alcance de red elegible
- **Solo este PC** (por defecto): IPP en `127.0.0.1:8631`.
- **Red local**: IPP en `0.0.0.0:8631`, anuncio mDNS `_ipp._tcp` (+ subtipo `_print`) con TXT (`rp=ipp/print`, `ty=X5h Thermal Printer`, `pdl=image/pwg-raster,image/jpeg,image/png`, `Color=F`, `URF` opcional), y regla de firewall para el perfil privado que el servicio crea al activar el modo y elimina al desactivarlo.
- La cola local de Windows siempre apunta a `http://127.0.0.1:8631/ipp/print`, sea cual sea el modo.
- Cambiar de modo reinicia solo el listener IPP, sin cortar trabajos en curso: se aplica cuando la cola queda vacía.

### D8. Rasterizado
Anunciar `image/pwg-raster` (`sgray_8`, 203 dpi) más JPEG/PNG. Se escala proporcionalmente a 384 px de ancho (la impresora trabaja a 200 dpi; la diferencia con 203 dpi es despreciable porque se escala al ancho igualmente), se recortan filas blancas arriba y abajo y se convierte a 1 bit. Dithering por defecto **Atkinson** para imágenes (el mismo valor por defecto que TiMini) y **umbral** cuando el trabajo es texto o el usuario lo elige.

### D8b. Página virtual de 80 mm y ajuste al contenido (añadido tras la prueba con el Bloc de notas)
Con papel de 48 mm, las aplicaciones aplican sus márgenes de A4 (unos 20 mm por lado en el Bloc de notas) y dejan una columna de ~10 mm (3 caracteres). Ningún driver puede reducir esos márgenes. La solución práctica es reducir los márgenes en la aplicación (confirmado por el usuario). Como opción adicional se ofrecen medios virtuales **80×297/80×100 mm** (el predeterminado sigue siendo 48×210); el rasterizador recorta los laterales en blanco (+1 mm) y escala el contenido al cabezal **sin superar el tamaño real** (203 dpi), reduciendo solo si no cabe. Los tamaños de 48 mm se mantienen para impresión 1:1. Diagnóstico: el servicio guarda el último documento y las páginas enviadas (`%ProgramData%\MiniPrinter\last-job`), visibles desde la bandeja.

### D9. Cola de trabajos, estado y cancelación
- Cola FIFO con un solo worker, historial de 20 trabajos.
- Antes de cada trabajo se envía `A3`. Si el byte de alarmas no es 0, el trabajo queda retenido y el estado IPP pasa a `stopped` con el motivo correspondiente (`media-empty-error`, `door-open-error`, `other-warning`…).
- Mapa de bits: `0x01` sin papel **verificado** (incluye tapa abierta → `media-empty-error`). `0x04` sobrecalentamiento y `0x08` batería baja vienen de iPrint y no se han observado en la X5h.
- Cancelación entre filas: se cierra con `BD 0C`, `A1 30 00` y `A3` para no dejar la impresora a medias.
- Conexión bajo demanda, desconexión tras 60 s de inactividad (configurable) para liberar la impresora a la app móvil. Mientras está conectada, `A3` cada 30 s para refrescar batería y estado en la bandeja.

### D10. Control de flujo
El lector de notificaciones decodifica `51 78 AE 01 01 00 <10|00> <crc> FF`: con `10` el escritor se bloquea antes del siguiente bloque, con `00` continúa. Si llega una pausa y no se reanuda en 10 s, el trabajo falla con "impresora no responde".

## Risks / Trade-offs

- **[RFCOMM WinRT no funciona desde un servicio en la sesión 0]** → **Descartado:** el servicio instalado como LocalSystem conecta por RFCOMM sin problemas (2026-10-01). `SerialTransport` queda como alternativa probada.
- **[El significado de los bits de `A3` no coincide con iPrint]** → resuelto para el bit `0x01`. Cualquier otro bit se muestra como "requiere atención" con el byte en crudo.
- **[El IPP Class Driver pide atributos que no anunciamos y no instala la cola]** → **Descartado:** `Add-Printer -IppURL` crea la cola con "Microsoft IPP Class Driver"; Windows ofrece los 4 tamaños de 48 mm y 203 dpi, e imprime la página de prueba de Windows y trabajos GDI (PWG Raster). El driver envía `job-name` en la página de códigos ANSI aunque declara UTF-8: el codec recurre a Latin-1 si el texto no es UTF-8 válido.
- **[Windows envía PDF en vez de PWG Raster]** → no anunciar PDF. Si fuera imprescindible, añadir PDFium como dependencia opcional.
- **[Conexión exclusiva con otras apps]** → desconexión por inactividad y mensaje claro de "ocupada por otra aplicación".
- **[Modo LAN expone la impresora]** → solo IPP (imprimir), nunca la API de control. Firewall limitado al perfil privado. Desactivado por defecto.
- **[Papel continuo vs. páginas]** → recorte de blanco y medio por defecto de 58 × 210 mm. Los documentos largos se imprimen como varias páginas seguidas sin avance extra entre ellas.
- **[Puertos 8631/8632 ocupados]** → configurables. El instalador usa los configurados.
- **[Cambios futuros en TiMini]** → se porta una foto fija de la receta y el catálogo. Los fixtures de referencia anclan el comportamiento.

## Migration Plan

Instalación nueva, sin datos previos:
1. `dotnet publish` (self-contained, win-x64) del servicio, la bandeja y la CLI.
2. `scripts/install.ps1` (administrador): copia a `%ProgramFiles%\MiniPrinter`, crea `%ProgramData%\MiniPrinter` (configuración, token, logs), registra y arranca el servicio con inicio automático, crea la cola "X5h Thermal Printer" con el IPP Class Driver apuntando a `http://127.0.0.1:8631/ipp/print` (método exacto validado en las tareas), y registra la bandeja en el inicio de sesión de los usuarios.
3. Primer arranque de la bandeja: asistente "Buscar impresora" → emparejar → elegir → prueba de impresión.
4. Rollback: `scripts/uninstall.ps1` elimina la cola, el puerto, la regla de firewall, el servicio y el arranque de la bandeja (opción `-KeepConfig` para conservar `%ProgramData%`).

## Open Questions

- Unidad del byte 2 de `A3` (batería): % o décimas de voltio. Requiere observar una descarga larga.
- Significado de los bytes `78 00`, `03` y `44 00` de `A8` (¿forma parte la `D` de la versión "3.0.5"?).
- Bits `0x04`/`0x08` de `A3` en la X5h (sobrecalentamiento y batería baja nunca observados).

Resueltas el 2026-10-01: bit de sin papel y ausencia de bit de tapa, significado del byte 1 de `A3`, control de flujo `AE`, RFCOMM desde la sesión 0, creación de la cola con `Add-Printer -IppURL`, ancho útil de 384 px y orden de bits (página de calibración correcta).
