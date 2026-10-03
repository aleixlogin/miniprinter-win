## Why

Dos cosas pequeñas que quedaron pendientes:

1. El CI avisa (`xUnit1031`) de que 15 tests del servicio bloquean hilos con `.Result` dentro de bucles de espera. Es la causa más probable de los fallos intermitentes (la release de `v0.7.1` falló una vez en un test que pasa siempre en local), y esconde fallos reales como bloqueos.
2. Para imprimir una plantilla desde un script hay que saber qué campos pide. Hoy solo se puede filtrar la lista completa de plantillas o leer el JSON entero; falta una consulta directa y pequeña.

## What Changes

- **Tests sin bloqueos**: sustituir los `.Result` de los tests del servicio por esperas asíncronas (`await`), con un `TestEnv.WaitUntilAsync` que recibe una condición asíncrona; sin cambiar lo que comprueban. El CI deja de emitir `xUnit1031` y el proyecto de tests pasa a tratarlos como error para que no vuelvan.
- **`GET /templates/{nombre}/fields`** (API de control y `/api/v1`): devuelve solo los campos de una plantilla (nombre, etiqueta, tipo, obligatorio, opciones y valor por defecto), más los parámetros de impresión que acepta (`copies`, `rows`, `darkness`). `404` si la plantilla no existe.
- **CLI**: `miniprinter template fields <nombre>` muestra lo mismo (ya hay `templates`, pero lista todas).

## Capabilities

### New Capabilities
<!-- Ninguna. -->

### Modified Capabilities
- `template-management`: nueva consulta de los campos de una plantilla.

## Impact

- **Código**: `MiniPrinter.Service` (`TemplateEndpoints`), `MiniPrinter.Control` (`ControlClient` y contrato), `MiniPrinter.Cli` (subcomando) y los tests de `MiniPrinter.Service.Tests` (`TestEnv` y 15 usos de `.Result`).
- **Dependencias**: ninguna.
- **Compatibilidad**: solo se añade un endpoint; nada existente cambia.
- **Riesgo**: bajo. Los tests asíncronos son más rápidos de escribir mal (un `await` olvidado); se compensa con el aviso del analizador convertido en error.
