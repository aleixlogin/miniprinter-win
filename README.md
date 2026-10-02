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

## Actualizaciones

La bandeja comprueba las [releases de GitHub](https://github.com/aleixlogin/miniprinter-win/releases) un minuto después de arrancar y cada 24 horas (se puede desactivar en *Ajustes → Actualizaciones*, y forzar con **Buscar actualizaciones** en el menú del icono). Si hay una versión nueva, avisa y muestra sus novedades con **Actualizar**, **Más tarde** u **Omitir esta versión**.

Al pulsar **Actualizar**, la bandeja descarga el instalador y comprueba que es auténtico antes de ejecutarlo: su SHA-256 debe coincidir con el de `SHA256SUMS`, y ese archivo debe llevar una firma ECDSA P-256 válida para la clave pública incrustada en la aplicación. Si algo no cuadra, se borra y no se ejecuta nada. El instalador pide permisos de administrador (UAC), cierra la bandeja y el servicio, actualiza conservando la configuración y vuelve a abrir la bandeja.

### Publicar una versión

1. Sube una etiqueta `vX.Y.Z` (`git tag v0.4.1 && git push origin v0.4.1`).
2. El workflow *Release* de GitHub Actions compila con esa versión, pasa los tests, genera `MiniPrinter-Setup-X.Y.Z.exe`, crea `SHA256SUMS`, lo firma con el secreto `RELEASE_SIGNING_KEY`, comprueba que la firma valida con la clave pública de la app y publica la release con las notas sacadas de los commits.

La clave privada de firma **no está en el repositorio**: vive solo en el secreto `RELEASE_SIGNING_KEY` y en una copia de seguridad. Para generar un par nuevo: `dotnet run --project tools/SignRelease -- keygen` (y actualizar `Updater.ReleasePublicKey`; las versiones ya instaladas solo aceptarán releases firmadas con la clave que llevan).

## Uso del panel (icono de la bandeja)

| Pestaña | Qué hace |
|---|---|
| **Estado** | Conexión, alarmas (sin papel o tapa abierta), batería, firmware, URL de impresión, cola de trabajos con cancelación, página de prueba, avance de papel, vista previa del último trabajo, muestreo y exportación del registro de batería. |
| **Buscar impresoras** | Lista dispositivos Bluetooth emparejados y cercanos, reconoce el modelo por su nombre (`X5h-…` → perfil `d1`), empareja y selecciona la impresora. |
| **Plantillas** | QR, códigos de barras, listas, etiquetas, Wi-Fi, tiques y más, definidas en JSON (también las tuyas), con vista previa en vivo a tamaño real, favoritos con nombre, copias y lotes desde CSV. |
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
| `POST /print/template/{nombre}` | JSON con los campos de la plantilla (ver `miniprinter templates`); admite `copies` y `rows` |
| `GET /jobs/{id}` | Estado: `pending`, `processing`, `completed`, `canceled` o `aborted` |
| `GET /templates` · `GET /templates/{nombre}` | Lista las plantillas (con `source`: `builtin`, `user` o `override`) y lee el JSON de una |
| `PUT /templates/{nombre}` | JSON de la plantilla (ver [Plantillas](#plantillas)): la valida y la crea o reemplaza. `200` con su definición |
| `DELETE /templates/{nombre}` | Borra una plantilla de usuario y sus imágenes (`204`). Las integradas no se pueden borrar |
| `POST /templates/validate` | Valida un JSON de plantilla sin guardarlo |
| `PUT` · `DELETE /templates/{nombre}/assets/{archivo}` | Sube o borra una imagen (PNG/JPEG, hasta 1 MB) de la plantilla, para un bloque `image` con `source` |

Límites: 16 MB por petición y 20 000 caracteres de texto; plantillas de hasta 64 KB, 50 copias y 200 filas por petición. Los errores devuelven `400 {"error": "…"}` (`401` sin token válido, `404` si la API está desactivada).

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
miniprinter template   label --rfcomm 7A:E0:0C:1D:87:AE --csv etiquetas.csv --copies 2   # lote desde CSV
miniprinter template   list | show label | validate mitique.json   # gestión sin impresora
miniprinter template   add mitique.json | remove mitique           # alta y baja (vía el servicio)
miniprinter stripes    --rfcomm 7A:E0:0C:1D:87:AE --rows 1200 --log   # trabajo largo + control de flujo
miniprinter find-port  7A:E0:0C:1D:87:AE                  # qué COM corresponde a la impresora
```


## Plantillas

Las plantillas son **datos**, no código: un JSON con campos y una lista de bloques que se apilan de arriba abajo a 384 px. Vienen integradas `qr`, `barcode`, `todo`, `label`, `sticker`, `shopping`, `wifi`, `contact`, `cable`, `receipt`, `bookmark` y `countdown`, y puedes añadir las tuyas en `%ProgramData%\MiniPrinter\templates\*.json` (o con `miniprinter template add` o la API; la bandeja las usa y las previsualiza, pero no tiene editor). Una plantilla tuya con el nombre de una integrada la sustituye; al borrarla vuelve la integrada.

Ejemplo, un tique con fecha, número consecutivo y precios alineados:

```json
{
  "name": "mitique",
  "title": "Mi tique",
  "fields": [
    { "name": "items", "label": "Líneas (Producto;precio)", "kind": "multiline", "required": true },
    { "name": "total", "label": "Total", "kind": "text" }
  ],
  "blocks": [
    { "type": "text", "value": "BAR PACO", "size": 16, "bold": true, "align": "center" },
    { "type": "text", "value": "{{now:dd/MM/yyyy HH:mm}} · Nº {{counter:tique}}", "size": 10, "align": "center" },
    { "type": "line", "style": "dotted" },
    { "type": "list", "items": "{{items}}", "marker": "none", "quantities": true, "leader": "dots" },
    { "type": "columns", "when": "{{total}}", "left": "TOTAL", "right": "{{total}}", "bold": true }
  ]
}
```

- **Campos**: `kind` = `text`, `multiline`, `choice` (con `choices`), `image` (Base64), `number`, `boolean`; `required`, `default`, `label`.
- **Marcadores** en cualquier texto: `{{campo}}`, `{{now}}` / `{{now:formato}}`, `{{counter}}` / `{{counter:nombre}}` (numeración persistente: avanza al imprimir, no en la vista previa) y filtros `{{campo|upper|lower|wifi|vcard|days|daysleft}}`.
- **Bloques**: `text` (`size`, `bold`, `align`), `qr` (`data`, `caption`, `ecc`, `module`), `barcode` (`format`: `code128`, `code39`, `ean13`, `upca`; `height` en mm), `image` (`data` en Base64 o `source` = archivo de `templates\assets\<plantilla>\`), `line` (`style`, `thickness`), `spacer` (`mm`), `columns` (`left`, `right`, `leader`), `list` (`items`, `marker` = `box`/`bullet`/`number`/`none`, `quantities`, `leader`). Todos admiten `when` (solo se imprimen si ese campo tiene valor). A nivel de plantilla: `gap` (hueco entre bloques), `frame` (marco) y `mode` (`text` o `image`).
- **Límites**: 64 KB por plantilla, 100 bloques, imágenes de hasta 1 MB, 1 m de papel.

**Plantillas integradas**

| Nombre | Para qué | Campos principales |
|---|---|---|
| `qr` | Código QR con texto debajo | `data`, `caption`, `ecc` (L/M/Q/H), `module` |
| `barcode` | Código de barras | `data`, `format` (`code128`, `code39`, `ean13`, `upca`), `caption`, `height` |
| `todo` | Lista de tareas | `title`, `items`, `marker` (`box`/`bullet`/`number`), `size` |
| `label` | Etiqueta con título grande | `title`, `line1`, `line2`, `size`, `align`, `border` |
| `sticker` | Imagen con texto opcional | `image` (Base64 o ruta en la CLI), `caption` |
| `shopping` | Lista de la compra con cantidades | `title`, `items` (`Leche;2`) |
| `wifi` | QR para conectarse a una red Wi-Fi | `ssid`, `password`, `security` (`WPA`/`WEP`/`nopass`) |
| `contact` | QR con una tarjeta de contacto (vCard) | `name`, `org`, `phone`, `email` |
| `cable` | Etiqueta de cable con el texto repetido a ambos lados de una línea de plegado | `text`, `size` |
| `receipt` | Tique con líneas, total, fecha y número consecutivo | `title`, `items` (`Café;1,50`), `total`, `note` |
| `bookmark` | Marcador de lectura | `title`, `author`, `quote` |
| `countdown` | Días que faltan hasta una fecha | `title`, `date` (`2026-12-31`) |

**En la bandeja** (pestaña *Plantillas*): la vista previa a tamaño real se actualiza sola 400 ms después de dejar de escribir y los errores de validación salen junto a ella; el botón **↻** (y abrir la pestaña) vuelve a pedir la lista, así que las plantillas creadas por API o CLI aparecen sin reiniciar la bandeja. Puedes guardar los valores del formulario como **favorito** con nombre (por plantilla), elegir el número de **copias** e imprimir un **lote desde CSV**. Si una plantilla tuya sustituye a una integrada, la bandeja lo avisa.

**Dónde se guardan los datos**: las plantillas, sus imágenes (`templates\assets\<plantilla>\`) y el contador de numeración (`counters.json`) viven en `%ProgramData%\MiniPrinter\`, que los usuarios solo pueden leer: se escriben a través del servicio (API, `miniprinter template add`). Los últimos valores y los favoritos de la bandeja están en `%LocalAppData%\MiniPrinter\` (`templates.json`, `favorites.json`). El desinstalador pregunta si conservar esta carpeta. Una orden `miniprinter template …` ejecutada sin permisos de administrador no puede actualizar el contador, así que las plantillas con `{{counter}}` conviene imprimirlas desde la bandeja o la API.

**Crear una plantilla por API**

```powershell
$h = @{ Authorization = "Bearer TU_TOKEN" }
Invoke-RestMethod -Method Put -Uri http://127.0.0.1:8631/api/v1/templates/mitique -Headers $h `
  -ContentType 'application/json' -Body (Get-Content mitique.json -Raw)
# Con un logo: guarda primero la plantilla sin el bloque image, sube el logo y vuelve a guardarla.
Invoke-RestMethod -Method Put -Uri http://127.0.0.1:8631/api/v1/templates/mitique/assets/logo.png -Headers $h `
  -ContentType 'image/png' -Body ([IO.File]::ReadAllBytes('logo.png'))
```

**Copias y lotes**: `copies` (1–50) repite cada etiqueta; `rows` (hasta 200) imprime una etiqueta por fila, cada fila sobrescribe los campos base; todo sale como un solo trabajo y si una fila falla no se imprime nada (`Fila 2: …`). En la bandeja, *Cargar CSV…* (cabecera con los nombres de campo, separador `,` o `;`). En la CLI:

```powershell
miniprinter templates                                              # plantillas y sus campos
miniprinter template list | show label | validate mitique.json    # sin impresora
miniprinter template add mitique.json                             # vía el servicio (valida y guarda)
miniprinter template remove mitique
miniprinter template label --rfcomm 7A:E0:0C:1D:87:AE --csv etiquetas.csv --copies 2
```

API (con el mismo token): `GET/PUT/DELETE /templates/{nombre}`, `POST /templates/validate`, `PUT/DELETE /templates/{nombre}/assets/{archivo}` y `POST /print/template/{nombre}` con `{"campo": "valor", "copies": 2, "rows": [{...}, {...}]}`.

## Solución de problemas

| Síntoma | Causa probable |
|---|---|
| «La impresora está en uso por otra aplicación» | TiMini-Print, Tiny Print u otra app tiene la conexión Bluetooth. Ciérrala o desconéctala. |
| «No respondió» / trabajos «Esperando impresora» | Impresora apagada o fuera de alcance. Los trabajos esperan hasta 10 min (configurable) y se imprimen solos al volver. |
| Estado «Requiere atención: sin papel» | El mapa de bits de alarmas está **pendiente de confirmar** en la X5h. Si la impresora tiene papel, comunícalo. |
| Windows no añade la cola | Comprueba que el servicio está iniciado (`services.msc`) y que `http://127.0.0.1:8631/` abre en el navegador. |
| La bandeja dice «Servicio no disponible» | El servicio no está instalado o está detenido. Registro: *Visor de eventos → Aplicación → MiniPrinter*. |
| Una plantilla mía no aparece | Pulsa **↻** en la pestaña *Plantillas*. Si sigue sin salir, el JSON es inválido: `miniprinter templates` muestra el motivo de las omitidas y `miniprinter template validate archivo.json` la valida. |
| «Fila N: …» al imprimir un lote | Esa fila del CSV no cumple los campos de la plantilla (por ejemplo, falta uno obligatorio). No se imprime ninguna etiqueta ni se gasta ningún número. |
| «No se puede actualizar el contador» (CLI) | `counters.json` es de solo lectura para usuarios sin privilegios: ejecuta la orden como administrador o imprime desde la bandeja o la API. |

Los ajustes, el token de la API, las plantillas de usuario y el contador de numeración se guardan en `%ProgramData%\MiniPrinter`.

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
| `MiniPrinter.Imaging` | Lector PWG Raster, PDF (PDFium), JPEG/PNG, escalado, recorte de blancos, tramado, renderizado de texto y el motor de plantillas (`Layout/`: modelo JSON, validador, bloques, catálogo y contadores; plantillas integradas en `Templates/*.json`). |
| `MiniPrinter.Ipp` | Codec IPP (RFC 8010) e impresora IPP Everywhere mínima. |
| `MiniPrinter.Service` | Servicio de Windows: cola, endpoint IPP con modo local/LAN (mDNS + firewall), API de control, API de automatización y gestión de plantillas. |
| `MiniPrinter.Tray` | App WPF de bandeja. |
| `MiniPrinter.Control` | Contratos y cliente de la API de control, y lector de CSV para lotes de etiquetas (compartidos por la bandeja y la CLI). |
| `MiniPrinter.Cli` | Diagnóstico, impresión de plantillas y gestión de plantillas. |
| `installer/` · `tools/IconGen` | Script de Inno Setup y generador del icono (`assets/miniprinter.ico`). |
| `MiniPrinter.Updates` · `tools/SignRelease` | Comprobación y verificación de actualizaciones; firma de releases. |

Los tests de protocolo comparan byte a byte con trabajos de referencia generados por TiMini-Print (`tools/generate_timini_fixtures.py`).

Los tests de plantillas comparan píxel a píxel las plantillas integradas con capturas de referencia (`tests/MiniPrinter.Imaging.Tests/Fixtures/templates/`); si cambias a propósito el aspecto de una, regenéralas con `$env:UPDATE_GOLDEN=1; dotnet test tests/MiniPrinter.Imaging.Tests`. Los tests de `Imaging.Tests` se ejecutan en secuencia porque PDFium no es seguro entre hilos.

Instalador en local: `powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version X.Y.Z` (necesita Inno Setup 6); genera `artifacts\installer\MiniPrinter-Setup-X.Y.Z.exe`.

## Historial de cambios

### 0.5.1
- La pestaña *Plantillas* de la bandeja vuelve a pedir la lista al abrirse y tiene un botón **↻**: las plantillas creadas por API o CLI aparecen sin reiniciar. Se quita el botón *Vista previa*, que ya no hace falta.

### 0.5.0 — plantillas definidas por el usuario
- **Plantillas como datos**: layout declarativo en JSON (bloques `text`, `qr`, `barcode`, `image`, `line`, `spacer`, `columns`, `list`; campos, `{{marcadores}}`, `{{now}}`, `{{counter}}`, filtros, `when`, `frame`). Las cinco plantillas originales se migraron al motor con salida idéntica.
- **Plantillas de usuario** en `%ProgramData%\MiniPrinter\templates\`, con gestión por API (`PUT/GET/DELETE /templates`, `validate`, imágenes) y CLI (`template list|show|validate|add|remove`). Una plantilla de usuario puede sustituir a una integrada.
- **Plantillas nuevas**: `shopping`, `wifi`, `contact`, `cable`, `receipt`, `bookmark`, `countdown`.
- **Más opciones**: `label` (tamaño, alineación, marco), `qr` (corrección de errores, módulo), `todo` (casilla, viñeta o número), `barcode` (Code 39, UPC-A, altura).
- **Numeración persistente** (`{{counter}}`): avanza al imprimir, no en la vista previa.
- **Copias y lotes**: `copies` (1–50) y `rows` (hasta 200) como un solo trabajo; una fila inválida cancela todo el lote sin gastar números. CSV en la bandeja y en la CLI (`--csv`, `--copies`).
- **Bandeja**: vista previa en vivo, favoritos con nombre, copias y carga de CSV.

### 0.4.0 y 0.4.1
- Actualizaciones automáticas desde GitHub con firma y pipeline de release, instalador de Inno Setup, icono y *keep-alive* de la impresora.

### Antes de 0.4
- La impresora estándar de Windows (IPP), modo texto, telemetría de batería, papel continuo, impresión rápida, API de automatización, PDF y las cinco plantillas originales.

## Créditos

- Receta del protocolo `tiny` y catálogo de modelos: [TiMini-Print](https://github.com/Dejniel/TiMini-Print) (Apache-2.0). Ver `NOTICE`.
- Notas de ingeniería inversa del GT01: [lisp3r/bluetooth-thermal-printer](https://github.com/lisp3r/bluetooth-thermal-printer).
- [ImageSharp](https://github.com/SixLabors/ImageSharp) e ImageSharp.Drawing (Six Labors Split License).
- [ZXing.Net](https://github.com/micjahn/ZXing.Net) (Apache-2.0) y [Docnet.Core](https://github.com/GowenGit/docnet) con PDFium (MIT / BSD).

Licencia: MIT (ver `LICENSE`).
