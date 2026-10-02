## 1. Modelo de layout y motor (template-layouts)

- [x] 1.1 Definir el modelo JSON (`TemplateLayout`, campos, bloques, `mode`) en `MiniPrinter.Imaging` con deserialización de `System.Text.Json` y los límites (64 KB, 100 bloques)
- [x] 1.2 Implementar el sustituidor de marcadores `{{campo}}`, `{{now[:formato]}}` y `{{counter[:nombre]}}`, con error por marcador no declarado y tests
- [x] 1.3 Implementar `ILayoutBlock`, el registro `type → factoría` y el `LayoutEngine` que apila los bloques con `Stack`; un bloque de texto vacío no ocupa altura
- [x] 1.4 Implementar `TemplateValidator` (tipos conocidos, marcadores, rangos de propiedades, archivos referenciados) con mensajes que indican el bloque y la propiedad; tests de los escenarios de rechazo

## 2. Bloques base y migración de las plantillas actuales

- [x] 2.1 Capturar PNG de referencia de `qr`, `barcode`, `todo`, `label` y `sticker` con el código actual y sus valores por defecto, y guardarlos como fixtures de los tests
- [x] 2.2 Implementar los bloques `text`, `spacer` y `line` (continua/punteada) sobre `TextRenderer`
- [x] 2.3 Implementar los bloques `qr` (con `ecc` y `module`) y `barcode` (`code128`, `ean13`) reutilizando la lógica y los mensajes de error actuales
- [x] 2.4 Implementar los bloques `list` (`box`, `bullet`, `number`) e `image` (campo Base64 con tramado Atkinson)
- [x] 2.5 Escribir las 5 plantillas actuales como JSON incrustados en `MiniPrinter.Imaging` y cargarlas con el catálogo
- [x] 2.6 Test de regresión píxel a píxel de las 5 plantillas contra los PNG de referencia; después, hacer que `TemplateRenderer.Render` delegue en el motor y borrar el `switch` y las funciones de dibujo antiguas

## 3. Catálogo y plantillas de usuario

- [x] 3.1 Implementar `TemplateCatalog`: integradas + `%ProgramData%\MiniPrinter\templates\*.json` (ruta inyectable para tests), sustitución por nombre, omisión de plantillas inválidas con registro del error
- [x] 3.2 Recarga automática: el catálogo compara la firma (nombre, fecha y tamaño) de los JSON en cada acceso y se relee si cambió (más simple y fiable que un `FileSystemWatcher`); la CLI lo lee en cada ejecución
- [x] 3.3 Crear la carpeta con permisos de lectura para todos los usuarios y escritura para administradores y el servicio (instalador y arranque del servicio); conservarla al desinstalar
- [x] 3.4 Tests del catálogo: plantilla nueva, sustitución de una integrada, restauración al borrarla, JSON roto junto a uno válido

## 4. Valores automáticos

- [x] 4.1 Implementar `CounterStore` persistente (`counters.json`, escritura atómica con archivo temporal, avance antes de encolar) con `SemaphoreSlim` en el servicio y bloqueo de archivo en la CLI local
- [x] 4.2 Hacer que la vista previa muestre el valor siguiente sin consumirlo, y que la impresión lo consuma una vez por etiqueta distinta
- [x] 4.3 Tests: dos impresiones consecutivas, reinicio del servicio, vista previa repetida sin avance, fecha con formato

## 5. Bloques nuevos y ajustes (template-blocks)

- [x] 5.1 Implementar el bloque `columns` (izquierda/derecha, relleno con puntos, parte el texto izquierdo si no cabe) con tests
- [x] 5.2 Añadir al bloque `barcode` los formatos `code39` y `upca` con validación de caracteres y dígito de control, y `height` en milímetros
- [x] 5.3 Añadir a `label` los campos opcionales `size`, `align` y `border`; a `todo` `marker` y `size`; comprobar que sin ellos la salida no cambia (test 2.6)
- [x] 5.4 Añadir el recurso de imagen por archivo (`source`) con caché por nombre y fecha de modificación, y la validación de existencia
- [x] 5.5 Escribir las plantillas integradas `shopping`, `wifi` (con escape de `;`, `,`, `:` y `\`), `contact` (vCard), `cable`, `receipt`, `bookmark` y `countdown` (fecha pasada rechazada), con un test de render por plantilla

## 6. Gestión de plantillas (template-management)

- [x] 6.1 Validar nombres de plantilla (identificador de hasta 40 caracteres) y rechazar rutas fuera de la carpeta; test con `../config`
- [x] 6.2 Endpoints de control: `GET /api/templates/{nombre}`, `PUT`, `DELETE` (las integradas no se borran) y `POST /api/templates/validate`; actualizar los contratos de `MiniPrinter.Control` y `ControlClient`
- [x] 6.3 Endpoints de recursos: `PUT`/`DELETE /api/templates/{nombre}/assets/{archivo}` (PNG/JPEG, 1 MB) y borrado de recursos al borrar la plantilla
- [x] 6.4 Mapear la gestión en `/api/v1/templates…` de la API de automatización con su token, activación y límites; tests de 401, 404 y 413
- [x] 6.5 CLI: `template list|show|add|remove|validate` (validación local sin servicio) y actualización de la ayuda

## 7. Copias y lotes (template-batch-printing)

- [x] 7.1 Extender `PrintRequests.PrintTemplate` con `copies` (1–50) y `rows` (hasta 200): una página por etiqueta, hueco entre ellas y un único trabajo en la cola; falla completo indicando el número de fila
- [x] 7.2 Numeración: `{{counter}}` avanza una vez por etiqueta distinta y no por copia
- [x] 7.3 Aceptar `copies` y `rows` en `POST /api/print/template/{nombre}` y `POST /api/v1/print/template/{nombre}`, y `--copies` y `--csv` en la CLI; tests con el simulador (3 copias, lote de 3, fila inválida, 201 filas, cancelación a mitad)
- [x] 7.4 Implementar el lector de CSV (UTF‑8, `,` o `;`, comillas, cabecera) compartido por la bandeja y la CLI, con aviso de columnas desconocidas

## 8. Bandeja

- [x] 8.1 Vista previa en vivo: temporizador de 400 ms, una petición en vuelo, número de secuencia para descartar respuestas obsoletas, error de validación junto a la vista previa
- [x] 8.2 Favoritos con nombre por plantilla (`%LocalAppData%\MiniPrinter\favorites.json`): guardar, cargar y borrar; mantener el último uso
- [x] 8.3 Campo de copias y botón "Cargar CSV…" con recuento, vista previa de la primera fila y confirmación a partir de 20 etiquetas
- [x] 8.4 Generar los controles de los campos nuevos de las plantillas (`Number`, `Boolean`, `Date` si procede) en `TemplatesPanel`
- [x] 8.5 Aviso en la bandeja cuando una plantilla de usuario sustituye a una integrada

## 9. Cierre

- [x] 9.1 Actualizar el `README` (formato de plantilla con un ejemplo de tique, carpeta, API y CLI); no se añaden plantillas de ejemplo al instalador porque las integradas ya cubren los casos
- [x] 9.2 Resolver las preguntas abiertas del diseño (ver "Open Questions" resueltas en design.md)
- [x] 9.3 Ejecutar todas las pruebas y comprobar en la impresora real un recibo, una etiqueta de cable, un lote CSV de 3 etiquetas y un QR de Wi‑Fi escaneado con el móvil
