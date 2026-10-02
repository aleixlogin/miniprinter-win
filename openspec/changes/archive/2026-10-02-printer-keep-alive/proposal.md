## Why

Hoy el servicio se conecta a la X5h al imprimir y se desconecta tras un tiempo sin uso (60 s por defecto) para liberarla a otras aplicaciones. El usuario quiere poder elegir lo contrario: **mantener la impresora conectada y viva** enviando un latido periódico (keep-alive), de modo que el enlace no se pierda, la primera impresión sea inmediata y los avisos (sin papel, tapa) lleguen en todo momento.

No existe un comando de latido documentado para la familia `tiny`; el candidato elegido es la consulta de estado `A3 00`, que es inocua. Se está verificando con la impresora real si ese tráfico evita que la X5h suelte el enlace o se apague sola (prueba de 45 min iniciada el 2026-10-02 a las 09:44).

## What Changes

- Nueva opción **"Mantener activa (keep-alive)"** en los ajustes del servicio, en la pestaña Ajustes y en el menú del icono de la bandeja, desactivada por defecto.
- Con la opción activa, la sesión de la impresora:
  - no se desconecta por inactividad (se ignora "Desconectar tras", que queda deshabilitado en la bandeja);
  - se conecta al arrancar el servicio o al elegir la impresora;
  - envía `A3 00` cada N segundos (30 por defecto, configurable) como latido, que además refresca el estado;
  - reconecta sola si el enlace se cae, con espera creciente.
- El modo persistente se separa del **muestreo de batería** (que ya usaba un mecanismo de keep-alive temporal) para que la bandeja no muestre "Muestreando…" de forma permanente.
- La bandeja muestra el estado "Activa (keep-alive)" / "Reconectando…".

## Capabilities

### New Capabilities
- `printer-keep-alive`: modo de conexión persistente con latido periódico, reconexión automática y su control desde el servicio y la bandeja.

### Modified Capabilities
<!-- Ninguna: la desconexión por inactividad de x5h-windows-printing sigue siendo el comportamiento por defecto; el keep-alive es una opción adicional. -->

## Impact

- `MiniPrinter.Transport` (`PrinterSession`: modo persistente distinto del muestreo, latido y reconexión con espera creciente).
- `MiniPrinter.Service` (ajustes `KeepAlive` y `KeepAliveIntervalSeconds`, `PrinterManager`, estado expuesto en la API de control).
- `MiniPrinter.Control` (`ServiceSettings`, `StatusDto`).
- `MiniPrinter.Tray` (casilla en Ajustes, opción en el menú del icono, textos de estado).
- **Efectos colaterales**: con la opción activa, ni la app del móvil ni TiMini-Print pueden conectarse a la impresora, y su batería se gasta más.
