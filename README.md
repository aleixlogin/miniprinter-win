# MiniPrinter

Convierte la mini impresora térmica Bluetooth **X5h-E07A** (y otras de la familia "cat printer" con protocolo `tiny`) en una **impresora normal de Windows**: aparece en *Impresoras y escáneres* y cualquier aplicación puede imprimir en ella desde el diálogo estándar. Opcionalmente, también desde otros equipos y móviles de la red local, y **sin driver** por el puerto 9100 (RAW/JetDirect) para el software de tiques que habla ESC/POS (TPV, `python-escpos`, RawBT, `nc`…).

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

Y, sin driver ni spooler, el software de tiques entra directamente por el **puerto 9100** (ver [Impresión directa](#impresión-directa-puerto-9100)):

```
 TPV, python-escpos, RawBT, nc…  ── TCP 9100: ESC/POS, imágenes, PDF, texto ──▶  Servicio MiniPrinter  ──▶  X5h
```

## Requisitos

- Windows 10 (2004+) o Windows 11 con Bluetooth.
- La impresora emparejada en Windows (la app de bandeja puede emparejarla).
- Para la impresión directa con caracteres japoneses, chinos, coreanos, árabes o tailandeses: las fuentes de Windows para esos idiomas (vienen con Windows; si faltan, esos caracteres salen como `?`).
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
| **Estado** | Una tarjeta con el estado de la impresora (lista, imprimiendo, sin papel, desconectada, error o servicio no disponible) y la batería; *Conectar* y *Página de prueba* a la vista y el resto (avanzar papel, muestrear y exportar la batería, vista previa del último trabajo) en el menú «⋯»; *Detalles* plegable (firmware, direcciones, servicio, versión) la lista de trabajos con su estado, su origen y la vista previa y la reimpresión de cada uno, y el botón **Diagnosticar** (también en «⋯») cuando algo falla. |
| **Buscar impresoras** | Lista dispositivos Bluetooth emparejados y cercanos, reconoce el modelo por su nombre (`X5h-…` → perfil `d1`), empareja y selecciona la impresora. |
| **Plantillas** | Galería de tarjetas con la miniatura de cada plantilla (o lista, como prefieras; se recuerda), buscador, las usadas hace poco primero; QR, códigos de barras, listas, etiquetas, Wi-Fi, tiques y más, definidas en JSON (también las tuyas), con vista previa en vivo a tamaño real, favoritos con nombre, copias y lotes desde CSV. |
| **Ajustes** | Menú lateral con búsqueda y una sección por tema —**Impresión** (oscuridad, modo, tramado, avance final, páginas continuas, fuente, mantener activa, desconexión por inactividad, reintentos e historial de trabajos), **Papel** (nombre en Windows, tamaños que se ofrecen y recreación de la impresora), **Red** (alcance y puerto IPP), **Impresión directa** (puerto 9100), **Automatización** (API y token), **Batería**, **Aspecto** y **General** (actualizaciones y atajo de la nota rápida)—. Cada sección se guarda por separado, marca con un punto los cambios sin guardar y avisa junto al campo si un valor no es válido. |

Puedes **soltar archivos** (PNG, JPEG, PDF, PWG, TXT) sobre la ventana del panel para imprimirlos.

El servicio se conecta a la impresora al imprimir y se desconecta tras 60 s sin uso, para que la app del móvil pueda volver a usarla.

**Historial y reimpresión** — el servicio guarda las páginas de los últimos trabajos (por defecto 10, de 0 a 50; hasta 50 MB y como máximo 40 páginas por trabajo) para poder verlas tal como se imprimieron: doble clic en un trabajo de la lista abre su vista previa, y *Reimprimir* pone en la cola las mismas páginas como un trabajo nuevo («Reimpresión de …») con la misma oscuridad y modo. Lo guardado se borra al arrancar el servicio y al desinstalar, y se desactiva poniendo 0 en *Ajustes → Impresión → Historial de trabajos* (entonces solo se conserva la vista previa del último). Cada trabajo registra su **origen**: Windows (con el usuario), Panel, API (con la dirección del cliente) o Puerto 9100 (con la dirección del cliente).

**Mantener activa (keep-alive)** — en *Ajustes* o en el menú del icono: el servicio mantiene la impresora conectada, le envía una consulta de estado (`A3`) cada 30 s (configurable, 10–300 s) y se reconecta solo si el enlace se cae (reintentos cada 5 s hasta 60 s). La primera impresión es inmediata y los avisos de papel llegan aunque no imprimas. A cambio, **la app del móvil y TiMini-Print no pueden usar la impresora** mientras esté activa (desmárcala en el menú del icono para liberarla) y su batería dura menos.

**Diagnóstico** — el botón *Diagnosticar* de la tarjeta de *Estado* (o la opción del menú «⋯») abre una ventana con un semáforo por comprobación: servicio, Bluetooth de Windows, impresora emparejada, conexión, papel y alarmas, impresora en Windows, escucha IPP, puerto 9100 (y su regla del cortafuegos en red local) y fuentes para japonés, chino, coreano, árabe y tailandés. Cada fila dice qué significa y qué hacer, con un botón que lleva a la solución (buscar impresoras, el Bluetooth de Windows, la sección de Ajustes que toca). Lo que no se puede comprobar sale como «?», nunca como un fallo. **Copiar informe** deja en el portapapeles un texto para pegar en una incidencia (versiones, resumen de ajustes y resultados) **sin token, sin nombres de plantillas ni direcciones de clientes ni de la impresora**.

**Plantillas desde el icono** — el menú del icono tiene *Plantillas*: *Favoritos* (cada uno con el nombre «Plantilla — favorito»; al elegirlo **se imprime al instante** con sus valores y un aviso, que se puede cancelar desde *Estado*; con más de 15 se agrupan por plantilla) y *Recientes* (las últimas 5 impresas desde la pestaña; abren el panel en esa plantilla con sus últimos valores, sin imprimir).

**Aspecto** — el panel y el menú del icono siguen el tema de Windows (claro u oscuro, y el de contraste alto cuando está activo) y cambian al instante si lo cambias en Windows. En *Ajustes → Aspecto* puedes fijar el tema (automático, claro u oscuro) y el tamaño del texto (pequeño, normal o grande); la ventana crece lo necesario para que nada se corte. El panel recuerda su tamaño, su posición, si estaba maximizado y la pestaña abierta (si el monitor ya no existe, se abre centrado), y se ve nítido en cada monitor aunque tengan escalas distintas. Las preferencias se guardan en `%LOCALAPPDATA%\MiniPrinter	ray.json`.

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

En **Ajustes → Imprimir desde → Toda la red local**, el servicio escucha en todas las interfaces, crea una regla de firewall solo para redes **privadas** y anuncia la impresora por mDNS (`_ipp._tcp`). Otros PCs con Windows, iPhone/iPad (Imprimir) y Android (servicio de impresión predeterminado) la encuentran solos. Solo se comparte la impresión: el panel y la API de control nunca salen de `127.0.0.1`. Si activas la [impresión directa](#impresión-directa-puerto-9100), su puerto 9100 se abre también a la red local (otra regla de firewall, `MiniPrinter RAW`, solo para redes privadas).

### Papel

Los tamaños de **48 mm** de ancho (50/100/210/297 mm de largo; 48×210 por defecto) imprimen a escala 1:1: es el área imprimible de un rollo de 58 mm a 203 dpi. Si una aplicación usa márgenes grandes pensados para A4 (el Bloc de notas, unos 20 mm por lado), redúcelos en su *Configurar página*, o elige uno de los tamaños virtuales de **80 mm**: el servicio recorta los laterales en blanco y ajusta el contenido al cabezal sin agrandarlo nunca. Las filas en blanco al principio y al final se recortan, así que un documento corto no gasta un folio entero de papel.

**Elegir los tamaños que ve Windows** (*Ajustes → Papel que se muestra a Windows*): una lista con una casilla por tamaño y un tamaño **por defecto**. Los siete tamaños de siempre son los preajustes (se pueden activar o desactivar, no editar) y puedes añadir **tamaños propios**: ancho de 30 a **57 mm** y largo de 10 a 1000 mm. Siempre queda al menos un tamaño activo y el por defecto es uno de ellos. Un tamaño propio de más de 48 mm (lo que imprime el cabezal) anuncia márgenes laterales de `(ancho − 48) / 2` mm, para que las aplicaciones maqueten a 48 mm dentro de la página. Sin tocar nada se ofrecen los mismos siete tamaños que antes.

Windows guarda los tamaños al crear la impresora, así que, al guardar con otra lista (o con otro nombre), la bandeja pregunta si **recrear la impresora de Windows**. El servicio da primero a la impresora una identidad nueva (Windows no acepta dos colas con el mismo `printer-uuid`), crea una cola nueva con un nombre temporal, comprueba que existe, borra la antigua (y su puerto si nadie más lo usa) y renombra la nueva: si algo falla, la anterior sigue funcionando. Después **lee los tamaños que Windows ofrece para la cola** (el driver de Windows puede tardar una creación en reflejar los cambios) y, si no coinciden con los anunciados, repite la recreación hasta 3 veces. Se pierden las preferencias de impresión de esa impresora y no debe haber trabajos pendientes en su cola de Windows; si era tu impresora predeterminada, la bandeja vuelve a marcarla. *Guardar sin recrear* guarda los ajustes pero Windows conserva los tamaños antiguos; el botón **Recrear impresora de Windows** lo hace cuando quieras (y repara la cola si se borró a mano). El servicio corre como administrador; si por lo que sea no tiene permisos, la bandeja repite el trabajo con `miniprinter queue-recreate` y **Windows pide el permiso de administrador (UAC) automáticamente**. La API de control tiene `GET /api/windows-queue` y `POST /api/windows-queue/recreate`.

Botón **Vista previa del último trabajo** (pestaña Estado): muestra exactamente lo que se envió a la impresora.

## Impresión directa (puerto 9100)

El servicio puede escuchar en el puerto TCP **9100** (el estándar RAW/JetDirect de las impresoras de red) para que imprima cualquier programa que sepa hablar con «una impresora de tiques en red», **sin driver, sin spooler y sin token**: software de TPV, `python-escpos`, `node-thermal-printer`, RawBT en Android, Home Assistant o simplemente `nc`.

Está **desactivado por defecto**. Se activa en **Ajustes → Impresión directa (puerto 9100)** (casilla, puerto de 1024 a 65535 y estado: «Escuchando en…» o el error si el puerto está ocupado) y se aplica al guardar, sin reiniciar el servicio. El alcance es el mismo que el de IPP: en *Solo este PC* escucha únicamente en `127.0.0.1`; en *Toda la red local* escucha en todas las interfaces y crea la regla de firewall `MiniPrinter RAW` solo para redes privadas (se quita al desactivarlo o volver a *Solo este PC*). Al activarlo en red local la bandeja avisa de que **el puerto no tiene autenticación**: cualquier equipo de la red privada podrá imprimir.

**Desde la aplicación.** Con el puerto escuchando, la sección *Impresión directa* de *Ajustes* ofrece: las direcciones para usarlo (en red local, el nombre del equipo y sus IP; en *Solo este PC*, `127.0.0.1`) con **Copiar** y **Mostrar código QR** (para escanearlo desde el móvil), **Imprimir ticket de prueba** (un tique ESC/POS real —cabecera, estilos, dos columnas, un QR y el corte— enviado por TCP al propio puerto, de modo que prueba todo el camino, y con el motivo si falla: desactivado, ocupado…) y la lista de los **últimos 50 clientes** (hora, dirección, tipo, tamaño, tiques o trabajo y resultado, incluidos los rechazados o cortados por un límite; solo en memoria y sin guardar nunca el contenido).

**Qué se imprime.** El servicio decide por los primeros bytes de cada conexión:

| Contenido | Cómo se reconoce | Resultado |
|---|---|---|
| PNG, JPEG, PDF, PWG Raster | cabecera (`89 PNG`, `FF D8 FF`, `%PDF-`, `RaS2`/`RaSt`) | documento normal (misma escala, recorte y tramado que por IPP) |
| ESC/POS | empieza por `ESC`, `GS` o `DLE`, o los contiene en los primeros 512 bytes | intérprete de tiques (abajo) |
| Texto plano | UTF-8 válido sin órdenes de control | renderizado de texto de los ajustes |
| Cualquier otra cosa | — | se cierra la conexión sin imprimir |

Un trabajo termina cuando el cliente **cierra la conexión**, tras **2 s sin datos** o, en ESC/POS, con la orden de **corte** (lo que llegue después empieza otro trabajo: una conexión puede llevar varios tiques). Límites: 4 conexiones a la vez, 16 MB por conexión, 30 s de inactividad y un tope de **20 m de papel por conexión** (160 000 puntos: lo que pida de más se ignora y la conexión se cierra, para que unos pocos bytes no puedan pedir toneladas de papel). Cada trabajo aparece en la cola (y en *Estado*) con el usuario `raw@<dirección del cliente>`, y pasa por la cola normal: respeta el orden, la retención si falta papel y la cancelación.

**ESC/POS soportado.** El texto se maqueta en celdas fijas sobre los 384 puntos: 32 columnas de 12×24 con la fuente A y 42 de 9×17 con la B, de modo que las columnas de un tique de TPV quedan alineadas. Un tique es una sola página continua.

| Orden | Efecto |
|---|---|
| `ESC @` | reinicia estilos, alineación y tabla de caracteres |
| `ESC t n` | tabla de caracteres (las del perfil por defecto de `python-escpos`: CP437, 850, 858, 1252, 860, 863, 865, 866, 852, ISO 8859-7/15…); si el texto es UTF-8 válido se decodifica como UTF-8 |
| `FS &`, `FS .` | modo kanji: con él activo los bytes son de dos bytes en Shift-JIS (por defecto; la configuración `RawPort:KanjiCodePage` admite 936 GBK, 950 Big5 y 949 EUC-KR); `ESC t 1` es CP932 (katakana de ancho medio y Shift-JIS) |
| `ESC E`/`ESC G`, `ESC -`, `GS !`, `GS B`, `ESC a`, `ESC M`, `ESC !` | negrita, subrayado, tamaño ×1–×8, inverso, alineación, fuente A/B |
| `ESC d`, `ESC J`, `ESC 2`/`ESC 3`, `ESC SP` | avance en líneas o puntos, interlineado y espaciado |
| `GS V`, `ESC i`/`ESC m` | corte: fin del tique (se recortan los blancos del final) |
| `GS v 0`, `ESC *` | imágenes raster y de bits (8 y 24 puntos); las que superan 384 puntos se recortan |
| `GS ( k` | código QR (módulo, corrección de errores y datos) y, con los mismos pasos de almacenar e imprimir, **PDF417**, **Aztec** y **Data Matrix** (`cn` 48, 53 y 54); si no caben, se reducen hasta 3 puntos por módulo (2 en los otros) y si aun así no caben se omiten |
| `GS k`, `GS H/h/w` | códigos de barras UPC-A, EAN-13, Code 39, Code 128 (y UPC-E, EAN-8, ITF, Codabar, Code 93) con texto legible |
| `DLE EOT 1–4`, `GS r 1` | respuestas de estado por la misma conexión (en línea, tapa, sin papel) |

Las órdenes desconocidas **nunca salen impresas como texto**: se descartan (se registran una vez en el log) y el resto del tique sigue. La gaveta, el buzzer y similares se ignoran. Un código que no es válido (un EAN-13 con 5 dígitos) o que no cabe se omite sin tirar el tique. Aspecto: la fuente es monoespaciada del sistema (Consolas), así que no será idéntica a la de una impresora de tiques real, pero las columnas y el formato sí.

**Caracteres de ancho completo.** Los ideogramas (japonés, chino simplificado y tradicional), el kana, el Hangul, las formas de ancho completo y los emoji ocupan **dos celdas** (24 puntos) y se dibujan con fuentes del sistema (Yu Gothic, MS Gothic, Microsoft YaHei, Malgun Gothic… según el texto: con kana, japonesas; con Hangul, coreanas); los símbolos que Consolas no tiene (☕ ⌘ …) salen de Segoe UI Symbol. El texto se normaliza (`e` + acento combinado → `é`) y los caracteres de ancho cero no ocupan celda. El **árabe** y el **tailandés** se dibujan como bloques proporcionales con el motor de texto del sistema (Tahoma, Segoe UI; Leelawadee UI): letras unidas, ligaduras y orden de derecha a izquierda en árabe, y vocales y tonos sobre su consonante en tailandés; un bloque ocupa un número entero de celdas y, si no cabe, se parte por palabras (el árabe, alineado a la derecha). El hebreo se dibuja celda a celda en orden visual. Un carácter que ninguna fuente tenga sale como `?`. Necesita las fuentes instaladas en el equipo del servicio (las de Windows las traen).

**Ajustes avanzados** (opcionales, en el `appsettings.json` junto a `MiniPrinter.Service.exe` o como variables de entorno del servicio, por ejemplo `RawPort__KanjiCodePage`): `RawPort:KanjiCodePage` (codificación del modo kanji: `932` Shift-JIS, por defecto, `936` GBK, `950` Big5, `949` EUC-KR), `RawPort:SilenceSeconds` (2: silencio que termina un trabajo) y `RawPort:IdleSeconds` (30: inactividad que cierra la conexión).

```powershell
# Una imagen o un texto (PowerShell, sin dependencias)
$c = New-Object Net.Sockets.TcpClient('127.0.0.1', 9100); $s = $c.GetStream()
$b = [IO.File]::ReadAllBytes('foto.png'); $s.Write($b, 0, $b.Length); $c.Close()
```
```bash
nc equipo 9100 < foto.png               # PNG, JPEG, PDF…
cat nota.txt | nc equipo 9100           # texto UTF-8
```
```python
from escpos.printer import Network       # pip install python-escpos
p = Network("127.0.0.1", 9100)
p.set(align="center", bold=True); p.text("MI TIENDA\n"); p.set(align="left", bold=False)
p.text("Cafe                       1,50\n"); p.qr("https://example.com", native=True); p.cut()
```

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
| `GET /templates/{nombre}/fields` | Solo los campos de una plantilla (nombre, etiqueta, tipo, obligatorio, opciones, valor por defecto) y `printParameters` (`copies` 1–50, `rows` hasta 200, `darkness` 1–5). `404` si no existe |
| `PUT /templates/{nombre}` | JSON de la plantilla (ver [Plantillas](#plantillas)): la valida y la crea o reemplaza. `200` con su definición |
| `DELETE /templates/{nombre}` | Borra una plantilla de usuario y sus imágenes (`204`). Las integradas no se pueden borrar |
| `POST /templates/validate` | Valida un JSON de plantilla sin guardarlo |
| `GET /templates/schema` | Esquema de los bloques (tipos, propiedades, rangos, opciones, filtros), generado por el motor |
| `POST /templates/preview` | Vista previa de una plantilla **sin guardar**: `{"template": {...}, "fields": {...}, "draft": "id"}`. Devuelve el PNG, con las filas de cada bloque en la cabecera `X-Template-Blocks`; los campos obligatorios vacíos no son un error |
| `POST /templates/{nombre}/preview` | Igual, para una plantilla guardada |
| `POST /templates/drafts` · `PUT`/`DELETE /templates/drafts/{id}/assets/{archivo}` · `DELETE /templates/drafts/{id}` | Borradores: subir imágenes de una plantilla aún sin guardar; se guardan con `PUT /templates/{nombre}?draft={id}` y caducan a las 24 h |
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

Las plantillas son **datos**, no código: un JSON con campos y una lista de bloques que se apilan de arriba abajo a 384 px. Vienen integradas `qr`, `barcode`, `todo`, `label`, `sticker`, `shopping`, `wifi`, `contact`, `cable`, `receipt`, `bookmark` y `countdown`, y puedes añadir las tuyas en `%ProgramData%\MiniPrinter\templates\*.json` (con el **editor de la bandeja**, con `miniprinter template add` o con la API). Una plantilla tuya con el nombre de una integrada la sustituye; al borrarla vuelve la integrada.

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

**Editor de plantillas** (pestaña *Plantillas* → **Crear**, **Editar**, **Eliminar**): abre una ventana con la lista de bloques a la izquierda, la vista previa a tamaño real en el centro y las propiedades del bloque a la derecha.

- **Bloques**: añadir (cualquier tipo), duplicar, borrar y reordenar arrastrando o con ↑ ↓. Un clic en la vista previa selecciona el bloque que hay debajo y lo resalta. Deshacer y rehacer con `Ctrl+Z` / `Ctrl+Y` (100 pasos; lo tecleado seguido cuenta como uno).
- **Propiedades**: los controles salen del esquema que publica el servicio (casillas, desplegables, números con su rango). El botón `{ }` de cada texto inserta `{{campo}}`, `{{now}}`, `{{counter}}` o un campo con filtro en la posición del cursor. En un bloque de imagen, *Elegir imagen…* la sube a un borrador del servicio, así que el logo se ve antes de guardar.
- **Pestaña Plantilla**: título, descripción, modo, hueco y marco, y los campos (nombre, etiqueta, tipo, obligatorio, valor por defecto y opciones). Borrar un campo que se usa en algún bloque pide confirmación y dice en cuáles. **Datos de prueba** rellena los campos solo para la vista previa (no se guardan) y **JSON** muestra el JSON, editable y sincronizado: si lo escrito no es válido se conserva el último estado válido.
- **Validación en línea**: el mensaje del servicio aparece junto al control afectado, el bloque se marca con ⚠ en la lista y el error general sale bajo la vista previa.
- **Guardar** valida y guarda; si el servicio la rechaza, la ventana sigue abierta con el error. **Guardar como…** crea una plantilla con otro nombre (el nombre de una existente no se cambia). Al cerrar con cambios pregunta si guardar. Editar una plantilla integrada y guardarla con su nombre crea una versión tuya que la sustituye; el JSON original puede tener comentarios, que se pierden al guardar desde el editor (se avisa).
- **Eliminar** borra una plantilla tuya (con confirmación); si sustituye a una integrada, el botón pasa a **Restaurar integrada**; las integradas sin versión tuya no se pueden eliminar.

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
miniprinter template list | show label | fields label | validate mitique.json    # sin impresora
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
| Nada llega al puerto 9100 (se queda colgado o «tiempo agotado») | Desde otro equipo: el modo es «Solo este PC» (el puerto solo escucha en `127.0.0.1`), tu red está marcada como **Pública** en Windows (la regla `MiniPrinter RAW` solo vale para redes privadas) o el router aísla a los clientes (redes de invitados). Compruébalo con `Test-NetConnection IP -Port 9100`. |
| «Conexión rechazada» en el 9100 | La casilla de *Impresión directa* no está activada y guardada, o el puerto está ocupado por otro programa (el estado en *Ajustes* muestra el motivo). |
| El 9100 acepta la conexión y no imprime nada | El contenido no se reconoce (ni ESC/POS, ni PNG/JPEG/PDF/PWG, ni texto UTF-8) y se cierra sin imprimir, o el tique no tiene nada visible. Mira el registro del servicio. |
| Un tique ESC/POS sale con `?` en vez de letras | El carácter no está en ninguna fuente (o falta la fuente del idioma en el equipo del servicio). Si es un texto de un TPV con acentos mal, prueba otra tabla de caracteres en el programa (`CP858` o UTF-8). |
| Un tique con japonés o chino sale con basura | El cliente usa el modo kanji con otra codificación: el servicio asume Shift-JIS (ver *Ajustes avanzados* del puerto 9100). |

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
| `MiniPrinter.Escpos` | Intérprete ESC/POS en streaming (celdas de 12×24 / 9×17, tablas de caracteres, imágenes, QR, códigos de barras y respuestas de estado) para el puerto 9100. |
| `MiniPrinter.Service` | Servicio de Windows: cola, endpoint IPP con modo local/LAN (mDNS + firewall), puerto 9100 de impresión directa, API de control, API de automatización y gestión de plantillas. |
| `MiniPrinter.Tray` | App WPF de bandeja: temas propios (`Themes/`), textos en `Strings.resx`. |
| `MiniPrinter.Gui` | Lógica de la interfaz sin WPF (modelos de vista, preferencias de la bandeja, tema, textos) para poder probarla. |
| `MiniPrinter.Control` | Contratos y cliente de la API de control, y lector de CSV para lotes de etiquetas (compartidos por la bandeja y la CLI). |
| `MiniPrinter.Cli` | Diagnóstico, impresión de plantillas y gestión de plantillas. |
| `tools/UiShots` | Fotografía el panel real (claro, oscuro, texto grande) con datos inventados, para revisar la interfaz sin servicio: `dotnet run --project tools/UiShots -- <carpeta> --app`. |
| `installer/` · `tools/IconGen` | Script de Inno Setup y generador del icono (`assets/miniprinter.ico`). |
| `MiniPrinter.Updates` · `tools/SignRelease` | Comprobación y verificación de actualizaciones; firma de releases. |

Los tests de protocolo comparan byte a byte con trabajos de referencia generados por TiMini-Print (`tools/generate_timini_fixtures.py`).

Los tests de ESC/POS interpretan tiques reales generados con `python-escpos` (`tools/generate_escpos_fixtures.py`) y `node-thermal-printer` (`tools/generate_node_escpos_fixtures.js`), con las fixtures en `tests/MiniPrinter.Escpos.Tests/Fixtures/` y los comparan con capturas de referencia; regenéralas con `UPDATE_GOLDEN=1` si cambias a propósito el aspecto.

Los tests de plantillas comparan píxel a píxel las plantillas integradas con capturas de referencia (`tests/MiniPrinter.Imaging.Tests/Fixtures/templates/`); si cambias a propósito el aspecto de una, regenéralas con `$env:UPDATE_GOLDEN=1; dotnet test tests/MiniPrinter.Imaging.Tests`. Los tests de `Imaging.Tests` se ejecutan en secuencia porque PDFium no es seguro entre hilos.

Instalador en local: `powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version X.Y.Z` (necesita Inno Setup 6); genera `artifacts\installer\MiniPrinter-Setup-X.Y.Z.exe`.

## Historial de cambios

### 0.9.3 — versión de prueba
- **Versión de prueba, sin cambios** respecto a la 0.9.2: sirve para comprobar la ventana de actualización. No hace falta instalarla.

### 0.9.2 — la ventana de actualización no se bloquea
- Si en la ventana de actualización se rechaza el permiso de administrador (UAC) o el instalador falla, la ventana lo dice y **vuelve a activar los botones** en vez de quedarse bloqueada.

### 0.9.1 — más robusto al arrancar
- El servicio **reintenta abrir el puerto de impresión de Windows (IPP)** cada segundo si estaba ocupado un instante al arrancar, en vez de quedarse sin escuchar hasta cambiar un ajuste.
- Los tests del servicio son más robustos en máquinas lentas.

### 0.9.0 — nueva interfaz
- **Aspecto**: tema claro, oscuro y de contraste alto que sigue a Windows (también el menú del icono y la barra de título), tamaño del texto Pequeño/Normal/Grande, ventana que recuerda tamaño, posición y pestaña, y DPI por monitor.
- **Ajustes por secciones** con menú lateral, búsqueda, guardado por sección, cambios sin guardar marcados, validación junto a cada campo y aviso al cerrar con cambios pendientes; salen a la vista ajustes que no tenían control (fuente y tamaño del texto, reintentos).
- **Estado como panel**: tarjeta con el estado, la batería en barra, acciones principales a la vista y el resto en «⋯», detalles plegables y lista de trabajos en español con iconos y columna *Origen*.
- **Historial de trabajos**: origen de cada trabajo, páginas de los últimos trabajos guardadas por el servicio, vista previa de cualquiera de ellos (`GET /api/jobs/{id}/pages/{n}`) y reimpresión (`POST /api/jobs/{id}/reprint`); ajuste `JobHistoryKeep`.
- **Diagnóstico** con semáforos y qué hacer en cada caso, y un informe copiable sin secretos (`GET /api/diagnostics`).
- **Puerto 9100 en la interfaz**: copiar la dirección, código QR, ticket de prueba por el propio puerto (`POST /api/raw-port/test`) y últimos clientes (`GET /api/raw-port/clients`).
- **Plantillas**: galería con miniaturas (`GET /api/templates/{nombre}/thumbnail`), buscador y las recientes primero; submenú *Plantillas* del icono con favoritos (se imprimen al instante) y recientes.
- **Editor de plantillas**: los datos de prueba cumplen las validaciones de lo que alimentan (un campo numérico ligado a la altura de un código de barras recibe un valor dentro de su rango, y las fechas de los filtros `days` y `daysleft` una fecha válida) y un bloque de imagen sin imagen enseña una imagen genérica en la vista previa y en las miniaturas (nunca en papel). El tamaño de módulo del QR se valida de 4 a 40, como ya exigía el motor.
- Todos los textos de la bandeja en `Strings.resx` (base para traducciones) con pruebas que impiden textos o colores sueltos.

### 0.8.0 — impresión directa por el puerto 9100
- **Puerto TCP 9100 (RAW/JetDirect)**, desactivado por defecto y con el alcance de IPP: acepta ESC/POS, PNG, JPEG, PDF, PWG Raster y texto plano, con límites (4 conexiones, 16 MB, 30 s), firewall solo en redes privadas, origen `raw@dirección` en la cola y sección propia en *Ajustes*.
- **Intérprete ESC/POS** (`MiniPrinter.Escpos`): texto en celdas fijas (32/42 columnas), tablas de caracteres, estilos, imágenes, QR, códigos de barras, corte y respuestas de estado; probado con tiques de `python-escpos`.
- **Más códigos y escrituras**: PDF417, Aztec y Data Matrix nativos (`GS ( k`); japonés, chino, coreano y emoji en celdas de ancho doble con el modo kanji (Shift-JIS, GBK, Big5 y EUC-KR) y la tabla CP932; árabe con letras unidas y tailandés con sus marcas (dibujados como bloques con el motor de texto del sistema); hebreo en orden visual; caracteres combinados (NFC) y de ancho cero.
- **Robusto ante entradas hostiles** (el puerto no tiene autenticación): tope de 20 m de papel por conexión, descarte sin almacenar de imágenes de gigabytes, avances en blanco sin memoria y pruebas de fuzzing; las órdenes con parámetros que no se interpretan (`ESC W`, `ESC &`, `FS p`, `FS q`, `ESC B`) ya no dejan basura en el ticket.
- `GET /templates/{nombre}/fields` (control y `/api/v1`) y `miniprinter template fields <nombre>`; los tests del servicio ya no bloquean con `.Result`.

### 0.7.1 — tamaños de papel configurables
- **Papel que se muestra a Windows** en *Ajustes*: elegir los tamaños activos y el por defecto, y añadir tamaños propios (ancho 30–57 mm, largo 10–1000 mm) con márgenes laterales automáticos para los de más de 48 mm. Sin cambios se ofrecen los siete tamaños de siempre.
- **Recrear la impresora de Windows** (al cambiar tamaños o nombre, o con el botón): identidad nueva, cola temporal, comprobación, borrado de la antigua y renombrado, con confirmación previa, comprobación de trabajos pendientes, verificación de los tamaños que ve Windows (con repasos si están desfasados), petición de permisos de administrador (UAC) si el servicio no los tiene y restauración de la impresora predeterminada. `GET /api/windows-queue` y `POST /api/windows-queue/recreate` en la API de control.

### 0.6.0 — editor de plantillas
- **Editor visual de plantillas** en la bandeja: botones **Crear**, **Editar** y **Eliminar** (o **Restaurar integrada**), ventana con lista de bloques, vista previa exacta en vivo con selección por clic, propiedades generadas desde el esquema, campos, datos de prueba, JSON en crudo, deshacer/rehacer y validación en línea.
- **API**: `GET /templates/schema`, `POST /templates/preview` (plantilla sin guardar, tolerante con campos vacíos), cabecera `X-Template-Blocks` con las filas de cada bloque, borradores con imágenes (`/templates/drafts`, `PUT /templates/{nombre}?draft=`) y errores de validación con `block` y `property`.
- Nombres reservados para plantillas: `list`, `show`, `add`, `remove`, `validate`, `schema`, `preview`, `drafts`.

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
