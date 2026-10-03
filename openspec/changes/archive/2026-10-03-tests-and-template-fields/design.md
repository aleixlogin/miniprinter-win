## Context

`TestEnv.WaitUntil(Func<bool>)` espera una condición síncrona. Casi todos sus usos pasan un lambda que llama a un cliente asíncrono con `.Result` (`client.GetJobsAsync().Result...`), lo que bloquea un hilo del pool por cada sondeo y es lo que avisa `xUnit1031`. Hay además usos sueltos de `.Result` fuera de esperas (`http.PostAsync(...).Result`, `IppCodec.ReadAsync(...).Result`, `api.GetStringAsync(...).Result`) en `ServiceTests`, `TemplateApiTests` y `PaperSizesIppTests`. Son 15 puntos en 3 archivos.

Las plantillas se consultan hoy con `GET /templates` (lista con campos) y `GET /templates/{nombre}` (JSON completo). `TemplateEndpoints.Map` es compartido por la API de control (`/api`) y la de automatización (`/api/v1`).

## Goals / Non-Goals

**Goals:**
- Que ningún test del servicio bloquee con `.Result`/`.Wait()`, y que el analizador lo impida en adelante.
- Una consulta directa de los campos de una plantilla, igual en las dos APIs y en la CLI.

**Non-Goals:**
- Reescribir los tests ni cambiar lo que verifican.
- Cambiar los tests de otros proyectos (no tienen el aviso).
- Un endpoint de esquema de impresión completo (para eso ya existe `GET /templates/schema`).

## Decisions

### D1. `WaitUntilAsync` en `TestEnv`
Se añade `TestEnv.WaitUntilAsync(Func<Task<bool>> condition, int seconds = 10)` con el mismo bucle y el mismo mensaje de error, y se mantiene `WaitUntil(Func<bool>)` para las condiciones que no llaman a nada asíncrono (por ejemplo `_queue.GetJob(...)!.IsTerminal`). Cada uso con `.Result` pasa a `await TestEnv.WaitUntilAsync(async () => (await client.GetJobsAsync()).Single(...).State == "Completed")`.
- *Alternativa descartada*: sobrecargar `WaitUntil` con `Func<Task<bool>>`: la resolución de sobrecargas con lambdas `async` es confusa y puede elegir la síncrona sin avisar.

### D2. Los usos sueltos pasan a `await`
`PrinterReasons()` y las llamadas HTTP sueltas pasan a métodos `async Task` y se esperan en el test; los helpers que devolvían valores síncronos devuelven `Task<T>`.

### D3. El aviso pasa a error
En `MiniPrinter.Service.Tests.csproj`: `<WarningsAsErrors>xUnit1031</WarningsAsErrors>`. Así un `.Result` nuevo rompe la compilación del proyecto de tests en vez de quedarse como aviso.

### D4. `GET /templates/{nombre}/fields`
Respuesta: `{ "name": "label", "title": "Etiqueta", "fields": [ { name, label, kind, required, choices, default } ], "printParameters": [ { "name": "copies", ... }, { "name": "rows", ... }, { "name": "darkness", ... } ] }`.
- `fields` reutiliza el mismo DTO que ya devuelve `GET /templates` (`TemplateDefinition.Fields`), así no hay un segundo formato.
- `printParameters` lista `copies` (entero 1–50), `rows` (lista de hasta 200 objetos con los campos de cada etiqueta) y `darkness` (entero 1–5): son los parámetros de impresión que acepta `POST /print/template/{nombre}` y que no son campos de la plantilla.
- Va dentro de `TemplateEndpoints.Map`, así que existe en control y en `/api/v1` con sus tokens y su alcance. La ruta `fields` no choca con `assets` ni con las rutas reservadas.

### D5. CLI
`miniprinter template fields <nombre>` imprime los campos y los parámetros con el mismo formato que `miniprinter templates`, leyendo el catálogo local (como `template show`), sin impresora ni servicio.

## Risks / Trade-offs

- **Un `await` olvidado en un test** hace que pase siempre (la tarea no se espera) → el analizador `CS4014`/`xUnit1031` avisa, y el aviso de `xUnit1031` pasa a error; se repasan a mano los 15 puntos y se ejecuta la suite varias veces.
- **Fallos que antes quedaban ocultos por bloqueos** pueden aflorar → se investigan en ese momento; son hallazgos, no regresiones.

## Migration Plan

1. `WaitUntilAsync` y migración de los 15 usos; la suite pasa varias veces seguidas (sin fallos intermitentes) y el compilador no emite `xUnit1031`.
2. `WarningsAsErrors` para `xUnit1031`.
3. Endpoint, cliente, CLI, tests y README.
Sin migración de datos; revertir es quitar el endpoint y volver a los tests anteriores.

## Open Questions

- ¿Conviene que `printParameters` incluya también `copies`/`rows` solo si la plantilla admite lotes? Se deja siempre (todas las plantillas los admiten).
