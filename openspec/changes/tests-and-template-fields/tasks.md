## 1. Tests sin bloqueos

- [x] 1.1 Añadir `TestEnv.WaitUntilAsync(Func<Task<bool>>, int seconds = 10)` (mismo bucle y mismo mensaje que `WaitUntil`) y conservar `WaitUntil(Func<bool>)` para condiciones síncronas
- [x] 1.2 Migrar los `.Result` de `ServiceTests` (esperas de trabajos y de estado, `PrinterReasons`, la llamada IPP y la consulta de la API) a `await`, sin cambiar lo que comprueban
- [x] 1.3 Migrar los de `TemplateApiTests` (3 esperas de trabajos) y de `PaperSizesIppTests` (`PrinterAttributes` y `OfferedSizes`)
- [x] 1.4 Comprobar que la compilación ya no emite `xUnit1031` y ejecutar la suite del servicio al menos 10 veces seguidas sin fallos intermitentes
- [x] 1.5 Poner `xUnit1031` como error en `MiniPrinter.Service.Tests.csproj` (`WarningsAsErrors`) y comprobar que un `.Result` nuevo rompe la compilación

## 2. Campos de una plantilla (template-management)

- [x] 2.1 Añadir `GET /templates/{nombre}/fields` en `TemplateEndpoints` (control y `/api/v1`) con `fields` (el DTO existente) y `printParameters` (`copies`, `rows`, `darkness`); 404 si no existe
- [x] 2.2 Contrato y método `GetTemplateFieldsAsync` en `MiniPrinter.Control`
- [x] 2.3 Tests del servicio: plantilla integrada (`label`), plantilla de usuario, parámetros de impresión, 404, 401 por `/api/v1`, plantilla que sustituye a una integrada
- [x] 2.4 CLI: `miniprinter template fields <nombre>` (lee el catálogo local; plantilla desconocida lista las disponibles) y su ayuda
- [x] 2.5 Documentar en el `README` (tabla de la API de automatización y comandos de la CLI) y ejecutar todas las pruebas
