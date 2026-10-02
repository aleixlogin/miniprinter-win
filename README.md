# MiniPrinter

Convierte la mini impresora térmica Bluetooth **X5h-E07A** (y otras de la familia "cat printer" con protocolo `tiny`) en una **impresora normal de Windows**: aparece en *Impresoras y escáneres* y cualquier aplicación puede imprimir en ella desde el diálogo estándar. Opcionalmente, también desde otros equipos y móviles de la red local.

```
 Aplicaciones (Bloc de notas, Edge, Word, Fotos…)
            │  diálogo de impresión
            ▼
 Spooler + Microsoft IPP Class Driver  (incluido en Windows, sin drivers de terceros)
            │  IPP · PWG Raster
            ▼
 Servicio MiniPrinter ── raster 384 px + tramado ── protocolo tiny (51 78 …) ── Bluetooth RFCOMM ──▶ X5h
            ▲
            │  API local (solo 127.0.0.1)
 App de bandeja: buscar, emparejar, conectar, estado, cola, ajustes
```

## Requisitos

- Windows 10 (2004+) o Windows 11 con Bluetooth.
- La impresora emparejada en Windows (la app de bandeja puede emparejarla).
- Para compilar: .NET SDK 8 o superior.

## Instalación

Descarga y ejecuta **`MiniPrinter-Setup-X.Y.Z.exe`** (pide permisos de administrador). El instalador:

- instala, si faltan, los runtimes de .NET 8 (ASP.NET Core y Windows Desktop);
- copia la aplicación a `%ProgramFiles%\MiniPrinter`, registra e inicia el servicio `MiniPrinter` y crea la cola **«X5h Thermal Printer»** con el *Microsoft IPP Class Driver* (`http://127.0.0.1:8631/ipp/print`);
- crea la carpeta **MiniPrinter** en el menú Inicio (**MiniPrinter** y **Desinstalar MiniPrinter**), el acceso «Enviar a → MiniPrinter», un icono opcional en el escritorio y el arranque de la bandeja con cada sesión.

Instalar una versión nueva encima conserva la configuración. Para desinstalar: menú Inicio → **Desinstalar MiniPrinter** (o *Configuración → Aplicaciones*); pregunta si conservar la configuración.

Windows SmartScreen puede avisar al ejecutar el instalador porque todavía no está firmado: *Más información → Ejecutar de todas formas*.

La primera vez, la bandeja abre el asistente: **Buscar → Emparejar → Usar esta impresora → página de prueba**.

**Compilar el instalador** (requiere [Inno Setup 6](https://jrsoftware.org/isinfo.php), p. ej. `winget install JRSoftware.InnoSetup`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version 0.4.0
# → artifacts\installer\MiniPrinter-Setup-0.4.0.exe
```

**Alternativa para desarrollo** (consola de administrador, sin instalador):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1 -KeepConfig
```

## Uso del panel (icono de la bandeja)

| Pestaña | Qué hace |
|---|---|
| **Estado** | Conexión, alarmas (sin papel o tapa abierta), batería, firmware, URL de impresión, cola de trabajos con cancelación, página de prueba, avance de papel, vista previa del último trabajo, muestreo y exportación del registro de batería. |
| **Buscar impresoras** | Lista dispositivos Bluetooth emparejados y cercanos, reconoce el modelo por su nombre (`X5h-…` → perfil `d1`), empareja y selecciona la impresora. |
| **Plantillas** | QR, código de barras (Code 128 / EAN-13), lista de tareas, etiqueta y pegatina, con vista previa a tamaño real. Recuerda los últimos valores. |
| **Ajustes** | Oscuridad, mantener activa (keep-alive), modo de impresión (automático / imagen / texto), tramado, avance final, páginas continuas, unidad y aviso de batería, desconexión por inactividad, nombre en Windows, alcance de red, puerto IPP, API de automatización y atajo de la nota rápida. |

Puedes **soltar archivos** (PNG, JPEG, PDF, PWG, TXT) sobre la ventana del panel para imprimirlos.

El servicio se conecta a la impresora al imprimir y se desconecta tras 60 s sin uso, para que la app del móvil pueda volver a usarla.

**Mantener activa (keep-alive)** — en *Ajustes* o en el menú del icono: el servicio mantiene la impresora conectada, le envía una consulta de estado (`A3`) cada 30 s (configurable, 10–300 s) y se reconecta solo si el enlace se cae (reintentos cada 5 s hasta 60 s). La primera impresión es inmediata y los avisos de papel llegan aunque no imprimas. A cambio, **la app del móvil y TiMini-Print no pueden usar la impresora** mientras esté activa (desmárcala en el menú del icono para liberarla) y su batería dura menos.

### Impresión rápida

- **Imprimir portapapeles** (menú del icono): imprime la imagen, el texto o los archivos copiados.
- **Nota rápida** (`Ctrl+Alt+P`, configurable): escribe, mira la vista previa y pulsa `Ctrl+Enter` para imprimir.
- **Enviar a → MiniPrinter** (menú contextual del Explorador): imprime los archivos seleccionados, uno por trabajo.

### Calidad y papel continuo

- **Modo de impresión automático**: las páginas que son texto o dibujo lineal se imprimen en el modo texto de la impresora (más energía, trazo más negro); las fotos, en modo imagen. Se decide página a página.
- **Páginas continuas** (Ajustes): las páginas de un trabajo salen como una sola tira, separadas por un hueco configurable, sin avance de papel entre ellas.
- Tamaño **48×1000 mm (rollo)**: para tiques y listas largas; el papel sobrante se recorta.

### Batería

La unidad del valor de batería que envía la X5h no está confirmada (39–40 observado). Por defecto se muestra el valor en bruto. Con **Muestrear batería 8 h** el servicio mantiene la conexión y registra una lectura por minuto; exporta el registro (CSV) y, cuando sepas la unidad, elígela en Ajustes para ver el porcentaje y recibir el aviso de batería baja. Registro: `%ProgramData%\MiniPrinter\telemetry\`.

### Imprimir desde la red local

En **Ajustes → Imprimir desde → Toda la red local**, el servicio escucha en todas las interfaces, crea una regla de firewall solo para redes **privadas** y anuncia la impresora por mDNS (`_ipp._tcp`). Otros PCs con Windows, iPhone/iPad (Imprimir) y Android (servicio de impresión predeterminado) la encuentran solos. Solo se comparte la impresión: el panel y la API de control nunca salen de `127.0.0.1`.

### Papel

Los tamaños de **48 mm** de ancho (50/100/210/297 mm de largo; 48×210 por defecto) imprimen a escala 1:1: es el área imprimible de un rollo de 58 mm a 203 dpi. Si una aplicación usa márgenes grandes pensados para A4 (el Bloc de notas, unos 20 mm por lado), redúcelos en su *Configurar página*, o elige uno de los tamaños virtuales de **80 mm**: el servicio recorta los laterales en blanco y ajusta el contenido al cabezal sin agrandarlo nunca. Las filas en blanco al principio y al final se recortan, así que un documento corto no gasta un folio entero de papel.

Botón **Vista previa del último trabajo** (pestaña Estado): muestra exactamente lo que se envió a la impresora.

## API de automatización

Permite imprimir desde scripts, Home Assistant, n8n o cualquier herramienta que haga peticiones HTTP. Está **desactivada por defecto**: actívala en *Ajustes → API de automatización* y copia el token. Con el modo *Solo este PC* solo responde en `127.0.0.1`; con *Toda la red local*, también desde otros equipos (la API de control del panel nunca sale de este PC).

Base: `http://<equipo>:8631/api/v1` · Cabecera: `Authorization: Bearer <token>` · Respuesta: `202 {"jobId": N}`.

| Método y ruta | Cuerpo |
|---|---|
| `POST /print/text` | JSON: `text` (obligatorio), `fontSize` (pt), `align` (`Left`/`Center`/`Right`), `bold`, `darkness` (1–5) |
| `POST /print/image` | PNG, JPEG o PDF en el cuerpo (con su `Content-Type`) o `multipart/form-data` con un archivo; `?darkness=` opcional |
| `POST /print/qr` | JSON: `data` (obligatorio), `caption`, `darkness` |
| `POST /print/template/{nombre}` | JSON con los campos de la plantilla (`qr`, `barcode`, `todo`, `label`, `sticker`; ver `miniprinter templates`) |
| `GET /jobs/{id}` | Estado: `pending`, `processing`, `completed`, `canceled` o `aborted` |

Límites: 16 MB por petición y 20 000 caracteres de texto. Los errores devuelven `400 {"error": "…"}` (`401` sin token válido, `404` si la API está desactivada).

**curl**

```bash
curl -X POST http://127.0.0.1:8631/api/v1/print/text \
  -H "Authorization: Bearer TU_TOKEN" -H "Content-Type: application/json" \
  -d '{"text":"Hola desde curl","fontSize":14}'

curl -X POST http://127.0.0.1:8631/api/v1/print/image \
  -H "Authorization: Bearer TU_TOKEN" -H "Content-Type: image/png" --data-binary @foto.png
```

**PowerShell**

```powershell
$h = @{ Authorization = "Bearer TU_TOKEN" }
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8631/api/v1/print/template/todo -Headers $h `
  -ContentType 'application/json' -Body (@{ title = 'Compra'; items = "Pan`nLeche`nHuevos" } | ConvertTo-Json)
```

**Home Assistant** (`configuration.yaml`; usa la IP del PC con el modo red local activo):

```yaml
rest_command:
  miniprinter_text:
    url: "http://192.168.0.211:8631/api/v1/print/text"
    method: post
    headers:
      Authorization: "Bearer TU_TOKEN"
    content_type: "application/json"
    payload: '{"text": "{{ message }}", "fontSize": 12}'
```

Uso en una automatización: `service: rest_command.miniprinter_text` con `data: { message: "Lavadora terminada" }`.

**n8n**: nodo *HTTP Request* → Method `POST`, URL `http://192.168.0.211:8631/api/v1/print/qr`, Authentication *Generic Credential Type → Header Auth* (Name `Authorization`, Value `Bearer TU_TOKEN`), Body *JSON* `{"data": "{{$json.url}}", "caption": "Escanéame"}`.

## Herramienta de diagnóstico

`miniprinter.exe` (en la carpeta de instalación, o `dotnet run --project src/MiniPrinter.Cli --`):

```powershell
miniprinter probe      --rfcomm 7A:E0:0C:1D:87:AE        # estado, firmware, id
miniprinter test-print --port COM5                        # página de calibración
miniprinter print      --mac 7A:E0:0C:1D:87:AE foto.jpg   # imprime un archivo (PNG, JPEG, PWG, PDF)
miniprinter print      --rfcomm 7A:E0:0C:1D:87:AE doc.pdf --pages 2-3   # solo algunas páginas
miniprinter templates                                     # plantillas disponibles y sus campos
miniprinter template   qr --rfcomm 7A:E0:0C:1D:87:AE --data "https://example.com" --caption "Escanéame"
miniprinter stripes    --rfcomm 7A:E0:0C:1D:87:AE --rows 1200 --log   # trabajo largo + control de flujo
miniprinter find-port  7A:E0:0C:1D:87:AE                  # qué COM corresponde a la impresora
```

## Solución de problemas

| Síntoma | Causa probable |
|---|---|
| «La impresora está en uso por otra aplicación» | TiMini-Print, Tiny Print u otra app tiene la conexión Bluetooth. Ciérrala o desconéctala. |
| «No respondió» / trabajos «Esperando impresora» | Impresora apagada o fuera de alcance. Los trabajos esperan hasta 10 min (configurable) y se imprimen solos al volver. |
| Estado «Requiere atención: sin papel» | El mapa de bits de alarmas está **pendiente de confirmar** en la X5h. Si la impresora tiene papel, comunícalo. |
| Windows no añade la cola | Comprueba que el servicio está iniciado (`services.msc`) y que `http://127.0.0.1:8631/` abre en el navegador. |
| La bandeja dice «Servicio no disponible» | El servicio no está instalado o está detenido. Registro: *Visor de eventos → Aplicación → MiniPrinter*. |

Los ajustes y el token de la API se guardan en `%ProgramData%\MiniPrinter`.

## Desarrollo

```powershell
dotnet build MiniPrinter.slnx
dotnet test  MiniPrinter.slnx
```

Para ejecutar el servicio en consola sin instalarlo: `$env:MINIPRINTER_DATA="$PWD\.data"; dotnet run --project src/MiniPrinter.Service` (la bandeja respeta la misma variable).

| Proyecto | Contenido |
|---|---|
| `MiniPrinter.Protocol` | Tramas `51 78 … CRC8 FF`, comandos, filas RLE/raw, receta de trabajo `d1`, decodificador de respuestas, catálogo de modelos. |
| `MiniPrinter.Transport` | RFCOMM (WinRT), puerto COM, conexión con control de flujo, sesión con reconexión y desconexión por inactividad. |
| `MiniPrinter.Imaging` | Lector PWG Raster, PDF (PDFium), JPEG/PNG, escalado, recorte de blancos, tramado, renderizado de texto y plantillas. |
| `MiniPrinter.Ipp` | Codec IPP (RFC 8010) e impresora IPP Everywhere mínima. |
| `MiniPrinter.Service` | Servicio de Windows: cola, endpoint IPP con modo local/LAN (mDNS + firewall), API de control. |
| `MiniPrinter.Tray` | App WPF de bandeja. |
| `MiniPrinter.Cli` | Diagnóstico. |
| `installer/` · `tools/IconGen` | Script de Inno Setup y generador del icono (`assets/miniprinter.ico`). |

Los tests de protocolo comparan byte a byte con trabajos de referencia generados por TiMini-Print (`tools/generate_timini_fixtures.py`).

## Créditos

- Receta del protocolo `tiny` y catálogo de modelos: [TiMini-Print](https://github.com/Dejniel/TiMini-Print) (Apache-2.0). Ver `NOTICE`.
- Notas de ingeniería inversa del GT01: [lisp3r/bluetooth-thermal-printer](https://github.com/lisp3r/bluetooth-thermal-printer).
- [ImageSharp](https://github.com/SixLabors/ImageSharp) e ImageSharp.Drawing (Six Labors Split License).
- [ZXing.Net](https://github.com/micjahn/ZXing.Net) (Apache-2.0) y [Docnet.Core](https://github.com/GowenGit/docnet) con PDFium (MIT / BSD).

Licencia: MIT (ver `LICENSE`).
