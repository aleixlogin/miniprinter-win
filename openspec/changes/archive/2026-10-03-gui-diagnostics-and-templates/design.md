## Context

El servicio ya conoce casi todo lo que hay que comprobar: el enlace con la impresora y sus alarmas (`StatusBuilder`), la cola de Windows (`WindowsQueue.GetAsync`), el estado del puerto 9100 (`RawPortHost.Current`) y las URL de IPP (`IppHost.Urls`). Lo que no sabe es lo que solo ve la bandeja: que el servicio responde, si el Bluetooth está encendido y si la impresora elegida está emparejada (esa enumeración está en `BluetoothScanner.cs` de la bandeja, con `DeviceInformation`).

El puerto 9100 (`RawPortHost`) atiende cada conexión en una tarea y deja constancia solo en el registro. Las plantillas se muestran en `TemplatesPanel` con una lista desplegable; los **favoritos** (valores guardados con nombre) y los **últimos valores** de cada plantilla son por usuario, en `%LocalAppData%\MiniPrinter\favorites.json` y `templates.json`. La vista previa de plantillas ya la genera el servicio (`POST /api/templates/{name}/preview`, `PrintRequests.RenderTemplateDetailed`/`PreviewDraft` con modo tolerante).

## Goals / Non-Goals

**Goals:**
- Que, ante un fallo, un clic diga qué parte falla y qué hacer.
- Usar el puerto 9100 sin salir de la aplicación y ver quién lo usa.
- Elegir una plantilla viéndola, y tener a mano las que se usan cada día.

**Non-Goals:**
- Controlar o arreglar automáticamente lo que falla (el diagnóstico informa y orienta; las acciones son las que ya existen).
- Una lista blanca de direcciones o registro persistente de clientes (seguridad, otro change).
- Importar o exportar plantillas.

## Decisions

### D1. Diagnóstico mixto: servicio y bandeja
Las comprobaciones se reparten según quién puede saberlas:

| Comprobación | Quién | Cómo |
|---|---|---|
| Servicio iniciado | bandeja | si `GET /api/status` responde; si no, las demás quedan «desconocido» y solo se explica esa |
| Bluetooth encendido | bandeja | `Windows.Devices.Radios.Radio` de tipo Bluetooth en estado `On` |
| Impresora emparejada | bandeja | la dirección de la impresora elegida está entre los dispositivos emparejados (`BluetoothScanner`) |
| Conexión con la impresora | servicio | enlace y último error de `StatusDto` |
| Papel y alarmas | servicio | alarmas de `StatusDto` (sin papel, calor, batería baja) |
| Cola de Windows | servicio | `WindowsQueue.GetAsync`: existe, apunta a nuestro puerto y su estado |
| Escucha IPP | servicio | conexión a su propio puerto IPP en loopback |
| Puerto 9100 | servicio | desactivado (informativo), escuchando, o el error (ocupado); en red local, si existe la regla `MiniPrinter RAW` (`netsh advfirewall firewall show rule`) |
| Fuentes para CJK, árabe y tailandés | servicio | familias de la lista de `CellFont` con glifos reales |

El servicio devuelve su parte con `GET /api/diagnostics` (lista de `{ id, status, detail, hint }`, con `status` en `ok`/`warn`/`fail`/`unknown`); la bandeja añade las suyas y las ordena. Un `DiagnosticsViewModel` combina ambas y es una función pura sobre los resultados, probada con una tabla de casos.
- **Qué hacer** (`hint`): cadenas de recursos con la acción (p. ej. «Enciende el Bluetooth de Windows», «Pulsa Recrear impresora en Ajustes → Papel»), con un botón que abre la sección correspondiente cuando existe.
- *Alternativa descartada*: que el servicio compruebe el Bluetooth. Corre como administrador sin sesión de usuario y el estado de la radio es el de la sesión; la bandeja sí lo ve.

### D2. Informe copiable
El texto incluye: versión de la aplicación y del servicio, versión de Windows, ajustes resumidos (modo de red, puertos, tamaños de papel, si el 9100 y la API están activos; **sin** token, nombres de plantillas ni contenido impreso), resultado de cada comprobación y el último error del estado. Es texto plano, estable y con un encabezado para pegarlo en una incidencia. Una prueba comprueba que ningún secreto sale en él.

### D3. Registro de clientes del 9100
`RawPortHost` guarda en memoria un anillo de los últimos 50 clientes: `{ time, address, kind (ESC/POS, PNG, JPEG, PDF, PWG, texto, desconocido), bytes, tickets o jobId, result (queued, rejected, closed-by-limit, error) }`. Se rellena al terminar cada conexión (también las rechazadas o cortadas por un límite) y se expone con `GET /api/raw-port/clients` (más recientes primero). No se escribe a disco ni se conserva al reiniciar. Es una vista de información y no impone nada.

### D4. Ticket de prueba por el propio puerto
`POST /api/raw-port/test` genera un ticket ESC/POS real (cabecera, estilos, dos columnas, QR y corte, y una línea con japonés y emoji si las fuentes están) y lo envía **por TCP a su propio puerto en loopback**, de modo que prueba el camino completo (escucha, detección, intérprete, cola). Responde con el trabajo creado o con el error de conexión (puerto desactivado u ocupado) con un mensaje claro. El trabajo aparece con el origen *Puerto 9100* y la dirección 127.0.0.1, como cualquier otro cliente.

### D5. Dirección y código QR
*Copiar dirección* copia `dirección:puerto` de la primera dirección útil (no loopback en red local; loopback si no); si hay varias, un menú deja elegir. *Mostrar código QR* abre una ventana pequeña con el QR de ese texto (ZXing, ya usado por las plantillas) y la dirección escrita debajo. En «Solo este PC» solo se ofrece la dirección de loopback y un aviso de que desde el móvil hay que activar la red local.

### D6. Galería de plantillas
La lista pasa a una cuadrícula de tarjetas (miniatura de 160 puntos de ancho, título, etiqueta de origen: integrada, usuario o sustituye). Un interruptor alterna galería y lista (la vista se recuerda en `tray.json`), un cuadro de búsqueda filtra por título y descripción y las usadas recientemente van primero.
- **Miniaturas en el servicio**: `GET /api/templates/{name}/thumbnail?width=160` dibuja la plantilla en modo tolerante con valores de ejemplo (el valor por defecto del campo, o su etiqueta) y devuelve un PNG; los campos de imagen sin valor salen como un marcador gris. Se cachean en memoria por nombre y huella del JSON y se invalidan al guardar o borrar la plantilla. La bandeja las pide en segundo plano y muestra un marcador mientras llegan.
- *Alternativa descartada*: dibujar las miniaturas en la bandeja. Duplicaría el motor de plantillas, que ya vive en el servicio.

### D7. Plantillas en el menú del icono
El submenú *Plantillas* tiene *Favoritos* (cada entrada «Plantilla — nombre», de `favorites.json`) y *Recientes* (hasta 5, de una lista nueva `RecentTemplates` en `tray.json`, que se actualiza al imprimir desde la pestaña). Elegir un favorito **imprime al instante** con sus valores (como en la pestaña) y avisa con un globo («Imprimiendo …»; se cancela desde *Estado*); elegir una reciente abre el panel en *Plantillas* con esa plantilla y sus últimos valores cargados, **sin imprimir**. Una plantilla borrada desaparece del menú. Si hay más de 15 favoritos, se agrupan por plantilla.

## Risks / Trade-offs

- **Comprobaciones que dan falsos avisos** (por ejemplo, la radio Bluetooth que no se puede consultar en algunos equipos) → el resultado es *desconocido*, nunca *error*, cuando la comprobación no puede completarse.
- **`netsh` para la regla de firewall** necesita ejecutarse como administrador; el servicio ya lo es. Si falla, la comprobación es *desconocido*.
- **Miniaturas costosas** → tamaño pequeño, caché, y petición en segundo plano con límite de dos a la vez.
- **Favorito que imprime al instante puede sorprender** → el globo lo dice y *Estado* permite cancelar; el ajuste de pedir confirmación se deja como pregunta abierta.
- **Registro de clientes y privacidad** → solo dirección, tipo y tamaño en memoria; no se guarda el contenido.

## Migration Plan

1. Servicio: `DiagnosticsService` y `GET /api/diagnostics`; registro de clientes y `GET /api/raw-port/clients`; `POST /api/raw-port/test`; miniaturas; contratos y cliente de control; pruebas.
2. Modelos de vista (diagnóstico, puerto 9100, galería, menú de plantillas) con pruebas; comprobaciones de la bandeja (Bluetooth, emparejada, servicio).
3. Vistas: diálogo de diagnóstico, bloque nuevo de *Impresión directa*, galería, submenú del icono.
4. README, comprobación visual por el usuario y pruebas completas.
Sin migración de datos: `tray.json` solo gana campos. Revertir: las pantallas anteriores siguen en el historial de git.

## Open Questions

- ¿Debe haber un ajuste para pedir confirmación antes de imprimir un favorito desde el menú? Se empieza sin él.
- ¿El diagnóstico debe poder ejecutarse al arrancar y avisar con un globo si algo falla? Se deja para después.
