## Context

El servicio ya tiene una cola única (`JobQueue`) a la que llegan IPP, la API de control, la de automatización y las entradas rápidas de la bandeja. Se puede encolar un trabajo de dos formas: un documento (`CreateJob` + `SubmitDocument(stream, formato)`, que pasa por `Rasterizer`) o rásters ya dibujados (`SubmitBitmaps`, una página cada uno). `TextRenderer` dibuja texto proporcional con ajuste de línea y `TemplateEngine` apila bloques; no hay un dibujo por celdas de ancho fijo.

`IppHost` levanta Kestrel con el alcance de `NetworkMode` (loopback o toda la red), crea la regla de firewall `MiniPrinter IPP` en modo red local y se reinicia cuando cambian ajustes de red. El estado de la impresora (`DeviceState`: alarmas, sensor de papel) está en `PrinterManager`.

El cabezal imprime 384 puntos (48 mm a 203 dpi). Una impresora de tiques de 58 mm con ESC/POS usa por defecto la fuente A de 12×24 puntos (32 columnas) y la B de 9×17 (42 columnas).

## Goals / Non-Goals

**Goals:**
- Imprimir por el puerto 9100 tiques ESC/POS reales (generados con `python-escpos`) con el formato esperado, e imágenes, PDF y texto en bruto.
- Que sea seguro por defecto: desactivado, sin abrir nada fuera de este PC salvo en modo red local.
- Reutilizar la cola, la retención sin papel, la cancelación y la vista previa del último trabajo.

**Non-Goals:**
- Autenticación o cifrado del puerto 9100.
- Cobertura completa de ESC/POS (gaveta, gráficos NV, macros, modo página real) y escrituras de derecha a izquierda o con formas contextuales (árabe, hebreo, tailandés).
- Emular otra marca (Star, Zebra ZPL, TSPL).
- Modo "raw para desarrolladores" con tramas `51 78`.

## Decisions

### D1. Un proyecto nuevo `MiniPrinter.Escpos`
El intérprete y su render viven en una biblioteca propia, sin Windows ni ASP.NET: entra un `byte[]` (o un flujo) y salen **páginas** (`MonoBitmap`), más los bytes de respuesta que se deben enviar al cliente. Así se prueba con fixtures de bytes sin red. Depende de `MiniPrinter.Protocol` (`MonoBitmap`) e `MiniPrinter.Imaging` (QR, códigos de barras y fuentes).

### D2. Listener `RawPortHost`
Un `BackgroundService` con `TcpListener`:
- Escucha en `127.0.0.1:<puerto>` o en todas las interfaces según `NetworkMode` (como IPP). Solo arranca con `RawPortEnabled`.
- Se reinicia al cambiar `RawPortEnabled`, `RawPort` o `NetworkMode` (con la misma espera a que no haya trabajos).
- Hasta 4 conexiones a la vez (las demás se cierran con el puerto ocupado); 16 MB como máximo por conexión; **tiempo máximo de 30 s de inactividad en lectura** (cierra y descarta lo no impreso si no hay datos completos).
- En modo red local, regla de firewall `MiniPrinter RAW` solo para redes privadas (`FirewallRule` se generaliza con nombre y puerto).
- Cada conexión crea trabajos con `raw@<ip del cliente>` como usuario.

### D3. Detección del contenido
Se acumulan los primeros 512 bytes (o hasta el fin de la conexión) y se decide una sola vez:

```
 empieza por 89 50 4E 47          → PNG   ┐
 empieza por FF D8 FF             → JPEG  │ documento: CreateJob + SubmitDocument
 empieza por "%PDF-"              → PDF   │
 empieza por "RaS2" / "RaSt"      → PWG   ┘
 contiene ESC (1B) o GS (1D) o DLE (10) antes que texto imprimible,
 o empieza por esos bytes         → ESC/POS (intérprete)
 UTF-8 válido sin otros controles → texto plano (TextRenderer)
 cualquier otra cosa              → se rechaza y se cierra
```
Un tique ESC/POS sin órdenes (solo texto y `LF`) cae en texto plano, que da el mismo resultado práctico. Los documentos se leen hasta el cierre de la conexión (o el tiempo sin datos); el texto plano igual.

### D4. Fin de trabajo en ESC/POS
Un trabajo termina con el **corte** (`GS V`), con el cierre de la conexión o con **2 s sin datos** después de haber recibido algo. Lo recibido tras un corte empieza otro trabajo. Cada trabajo es una sola página continua (sin avances intermedios) enviada con `SubmitBitmaps`. Un tique sin contenido imprimible no se imprime.

### D5. Render por celdas
El intérprete mantiene un **búfer de línea** de celdas (carácter, estilo) y un cursor:
- Celda base 12×24 puntos (fuente A, 32 columnas) o 9×17 (fuente B, 42 columnas); `GS !` multiplica el ancho y el alto hasta ×8.
- Al llegar a `LF` (o al llenar la línea), la línea se dibuja a la vez, con la altura de su celda más alta, y se añade al lienzo.
- Cada carácter se dibuja con una fuente monoespaciada (`Consolas`, con `Cascadia Mono` y `Lucida Console` de reserva) a un tamaño tal que su avance sea el ancho de la celda; negrita con el estilo de la fuente, subrayado como una raya de 2 puntos, inverso como celda negra con carácter blanco.
- Alineación (`ESC a`) por línea: se desplaza la línea completa dentro de los 384 puntos.
- El texto se dibuja a 1 bit sin tramado (como `TextRenderer`).
- *Alternativa descartada*: reutilizar `TextRenderer` (proporcional). Rompería las columnas de los tiques de TPV, que dependen de un ancho fijo.

### D6. Subconjunto de órdenes
| Orden | Efecto |
|---|---|
| `ESC @` | reinicia estilos, alineación y tabla de caracteres |
| texto, `LF`, `CR` | escribe en celdas; `LF` cierra la línea; `CR` se ignora |
| `ESC t n` | tabla de caracteres: 0 = CP437, 2 = CP850, 16 = CP1252, 19 = CP858; otras se tratan como CP437 |
| `FS &`/`FS .` o `ESC R n` | se ignoran (UTF-8: si el flujo es UTF-8 válido, se decodifica así) |
| `ESC E n`, `ESC G n` | negrita |
| `ESC - n`, `FS -` | subrayado (0, 1, 2) |
| `GS ! n` | tamaño (ancho y alto ×1–×8) |
| `GS B n` | inverso |
| `ESC a n` | alineación 0/1/2 |
| `ESC M n` | fuente A/B |
| `ESC d n`, `ESC J n` | avanza `n` líneas / `n` puntos |
| `ESC 2`, `ESC 3 n`, `ESC SP n` | interlineado y espaciado de caracteres |
| `GS V m [n]` | corte: fin del trabajo (con avance final) |
| `GS v 0 m xL xH yL yH d…` | imagen raster (modos normal y doble) |
| `ESC * m nL nH d…` | imagen de bits (8 y 24 puntos) |
| `GS ( k` (funciones 65, 67, 69, 80, 81, 82) | código QR: modelo, módulo, corrección, datos, imprimir |
| `GS k m …` (formatos 0–6 terminados en NUL y 65–73 con longitud) | códigos de barras: UPC-A, EAN-13, Code 39, Code 128 |
| `GS H`, `GS h`, `GS w`, `GS f` | texto bajo el código, altura, ancho de módulo y fuente del texto |
| `DLE EOT n`, `GS r n` | respuestas de estado (D8) |
| `ESC p`, `ESC =`, `GS P`, `ESC 7`, `GS a` y similares | se ignoran saltando sus parámetros |

Una orden desconocida cuyo tamaño de parámetros no se conoce **no se interpreta como texto**: se descarta solo su prefijo (`ESC x` o `GS x`) y el resto continúa; se registra una vez en el log.
- La QR se genera con el módulo y la corrección pedidos (mínimo de 3 puntos por módulo y reducción para que quepa en 384); los códigos de barras se dibujan como en las plantillas y se centran o alinean según `ESC a`.

### D7. Juegos de caracteres
`System.Text.Encoding.CodePages` registra las páginas. `ESC t n` usa la tabla del **perfil por defecto de `python-escpos`** (un superconjunto de la de Epson): 0 = CP437, 2 = CP850, 3 = CP860, 4 = CP863, 5 = CP865, 11 = CP851, 13 = CP857, 14 = CP737, 15 = ISO 8859-7 (con el euro en `0xA4`, que .NET no trae), 16 = CP1252, 17 = CP866, 18 = CP852, 19 = CP858, 21 = CP874, 32–38 (CP720, CP775, CP855, CP861, CP862, CP864, CP869), 39 = ISO 8859-2, 40 = ISO 8859-15 y 45–52 = CP1250–CP1258; una tabla desconocida o que .NET no tenga se trata como CP437. `python-escpos` cambia de tabla por sí solo según los caracteres del texto, por eso hacen falta todas. La decodificación es **por tramo de texto** (los bytes entre dos órdenes): si el tramo es UTF-8 válido y tiene bytes altos se decodifica como UTF-8 (muchos clientes modernos lo envían así), si no con la tabla activa. El carácter que la fuente no tiene se sustituye por `?`.

### D11. Caracteres de ancho completo (CJK, coreano, emoji)
- **Anchura**: un carácter East Asian Wide/Fullwidth (Hangul, kana, ideogramas, formas de ancho completo, `U+1F300`–`U+1FAFF` de emoji) ocupa **dos celdas** (24 × 24 puntos con la fuente A, 18 × 17 con la B, escalados por `GS !` como el resto). Los katakana de ancho medio (`U+FF61`–`U+FF9F`) siguen ocupando una.
- **Fuentes**: se prueba una lista según la región del tramo de texto (con kana → japonesa: Yu Gothic, MS Gothic; con Hangul → coreana: Malgun Gothic; si no → china: Microsoft YaHei, SimSun, Microsoft JhengHei) y, para símbolos y emoji, Segoe UI Symbol y Segoe UI Emoji. Un carácter que ninguna tenga sale como `?` de una celda. El glifo se dibuja en la celda ancha y se umbraliza como el resto (1 bit).
- **Modo kanji**: `FS &` activa el modo y `FS .` lo desactiva (también `ESC @`); con él activo los bytes se decodifican con la página de códigos CJK del intérprete (Shift-JIS 932 por defecto; configurable a GBK 936, Big5 950 o EUC-KR 949), sin la detección de UTF-8. `FS C n` (modo de código kanji) se acepta y se ignora. La tabla `ESC t 1` es CP932, así que un tramo con bytes de katakana o de Shift-JIS fuera del modo kanji también sale bien.
- **Composición**: el texto de cada tramo se normaliza a NFC (`e` + `´` → `é`), y los caracteres de ancho cero (`U+200B`–`U+200F`, `U+2060`, `U+FE00`–`U+FE0F`, `U+FEFF`) no ocupan celda.
- **Hebreo**: los tramos de hebreo (y los espacios entre sus palabras) se ponen en orden visual antes de dibujarlos celda a celda, con la fuente de reserva.
- **Árabe y tailandés (escrituras complejas)**: un tramo continuo de árabe (con los espacios y signos entre sus palabras) o de tailandés se dibuja **entero como un bloque proporcional** con una fuente del sistema (Tahoma, Segoe UI, Arial; Leelawadee UI, Tahoma), de modo que el motor de texto aplica las formas contextuales, las ligaduras (lam-alef), el orden de derecha a izquierda del árabe y la posición de las marcas del tailandés. El bloque ocupa un número entero de celdas para no desalinear el resto de la línea, respeta `GS !` y la negrita, y si es más ancho que el cabezal se parte por palabras (el árabe alineado a la derecha). Un carácter que la fuente no tenga sale como `?`.

### D8. Respuestas de estado
El cliente puede pedir estado y esperar la respuesta por la misma conexión:
- `DLE EOT 1` (impresora): `0x12`; `DLE EOT 2` (offline): `0x12` en línea o `0x32` con tapa abierta; `DLE EOT 3` (error): `0x12` o `0x72` con alarma; `DLE EOT 4` (papel): `0x12` con papel, `0x72` sin papel.
- `GS r 1`: `0x00` con papel o `0x0C` sin papel.
Se calculan del `DeviceState` actual (alarma de papel o de tapa) o `0x12` si la impresora está desconectada. Las respuestas no esperan a la cola de trabajos.

### D9. Ajustes y estado
`ServiceSettings` gana `RawPortEnabled` (falso) y `RawPort` (9100, 1024–65535). La bandeja añade una sección *Impresión directa (puerto 9100)* con la casilla, el puerto, una nota ("el puerto no tiene autenticación: usa solo en redes de confianza") y el estado (escuchando en…, o el error si el puerto está ocupado). El estado también va en `StatusDto`.

### D10. Datos de prueba
Los tests usan **fixtures de bytes ESC/POS reales** (generadas con `python-escpos` y guardadas como `.bin` en el repositorio, con su descripción) y comparan el raster con PNG de referencia, como las plantillas.

## Risks / Trade-offs

- **Puerto sin autenticación**: cualquiera con acceso al puerto imprime → desactivado por defecto, alcance como IPP, firewall solo en redes privadas, límites de tamaño, conexiones y tiempo, y origen visible en la cola.
- **ESC/POS es un estándar de facto con variantes** → se cubre el subconjunto más usado y se ignora con seguridad el resto; las fixtures reales detectan lo que falte.
- **Tamaño de celda y fuentes** → el aspecto no será idéntico al de una impresora de tiques real, pero las columnas y el formato sí; la fuente se escoge para encajar en 12 puntos de avance.
- **Cierre por inactividad de 2 s** puede partir un tique lento → con `GS V` el cliente lo marca; sin corte, 2 s es el equilibrio habitual.
- **Detección ambigua** (un archivo de texto que contiene un `ESC` suelto) → pasa por el intérprete, que descarta lo desconocido y sigue.
- **Impresión larga y bluetooth** → la cola ya serializa y reintenta; un cliente que envía mucho seguido llena la cola de trabajos, por eso el tope de conexiones y de tamaño.

## Migration Plan

1. Biblioteca `MiniPrinter.Escpos` con el intérprete y su render, probada con fixtures sin red.
2. Listener, detección y documentos en bruto (parte simple), con ajustes y estado.
3. Regla de firewall y alcance, límites.
4. Ajustes de la bandeja.
5. Prueba real con `nc` y con `python-escpos` apuntando al puerto 9100.
Sin migración de datos. Revertir es dejar `RawPortEnabled` en falso.

## Open Questions

Resueltas al implementar:
- **mDNS**: no se anuncia el puerto 9100 (`_pdl-datastream._tcp`); queda como mejora futura.
- **Fuente B**: se dibuja a su tamaño real de celda (9×17) con la misma fuente monoespaciada, no escalando la A.
- **Tiempos**: el silencio de 2 s y la inactividad de 30 s se pueden cambiar con la configuración `RawPort:SilenceSeconds` y `RawPort:IdleSeconds` (para los tests).
- **Aviso al cancelar**: si el usuario cancela el aviso de seguridad al activar el puerto en red local, no se guarda ningún ajuste de ese guardado (el puerto no se activa).
