## Context

`PrinterSession` ya tiene casi todo lo necesario:

- Conexión bajo demanda (`UseAsync` → `EnsureConnectedAsync`) con reintentos.
- Bucle de mantenimiento que desconecta tras `IdleTimeout` y consulta `A3` cada `PollInterval` (30 s) mientras hay conexión.
- `KeepAlive(until, poll)`, añadido para el muestreo de batería: mientras `KeepAliveUntil` está en el futuro no hay desconexión por inactividad, se reconecta si el enlace cae (cada `poll`) y se consulta `A3` cada `poll`. La API de control lo expone como `SamplingUntil`.

No hay comando de latido documentado para la familia `tiny` (ni en TiMini-Print ni en iPrint); otras familias sí tienen configuración de apagado automático (Phomemo `shutdown_value`, YK Astra `auto_off_time`). El latido elegido es `A3 00` (consulta de estado), inocuo y ya usado por el sondeo.

**Prueba en curso (2026-10-02, 09:44–10:30):** 45 min de keep-alive con `A3` cada 60 s usando el mecanismo del muestreo, registrando el estado del enlace cada 30 s. Objetivo: saber si la X5h mantiene el enlace y no se apaga sola con ese tráfico.

## Goals / Non-Goals

**Goals:**
- Opción para mantener la impresora conectada y viva con un latido.
- Reconexión automática y estado claro en la bandeja.
- No romper el comportamiento por defecto ni el muestreo de batería.

**Non-Goals:**
- Configurar el apagado automático del firmware (no hay comando conocido para la X5h).
- Latidos que muevan el papel o el motor.

## Decisions

### D1. Modo persistente separado del muestreo
`SessionOptions` gana `Persistent` (bool) y `HeartbeatInterval`. El bucle de mantenimiento evalúa `keepAlive = Persistent || (KeepAliveUntil > now)`; el intervalo efectivo es el menor de los activos. `KeepAliveUntil` queda solo para el muestreo, de modo que `SamplingUntil` en la bandeja sigue significando muestreo.
- *Alternativa descartada*: `KeepAlive(DateTimeOffset.MaxValue)`. Funciona, pero mezclaría ambos conceptos y la bandeja mostraría "Muestreando" para siempre.

### D2. Latido = `A3 00` serializado con los trabajos
El latido usa el mismo semáforo que `UseAsync` (`_lock.Wait(0)` en el bucle), así que nunca se intercala en un trabajo: si hay un trabajo en curso, el latido se salta y se envía en el siguiente ciclo. La respuesta actualiza el estado como el sondeo actual.

### D3. Reconexión con espera creciente
En modo persistente, tras un fallo de conexión la espera entre intentos crece 5 s → 10 s → 20 s → 40 s → 60 s (máximo) y se reinicia al conectar. Los errores `Busy` (otra app tiene la impresora) también se reintentan, porque la otra app puede soltarla. Estado publicado: `LinkState.Connecting` con `LastError` del último intento → bandeja "Reconectando…".

### D4. Conexión al arrancar y al cambiar ajustes
`PrinterManager` crea la sesión con `Persistent = settings.KeepAlive`; al cambiar `KeepAlive` o `KeepAliveIntervalSeconds` actualiza la sesión existente (`SetPersistent(bool, interval)`) en lugar de recrearla, para no cortar un trabajo. Si se activa, el bucle intenta conectar en el siguiente ciclo.

### D5. Ajustes y API
- `ServiceSettings.KeepAlive` (falso) y `KeepAliveIntervalSeconds` (30, rango 10–300).
- `StatusDto.KeepAlive` (activo o no) para la bandeja; `Link = "Connecting"` + `LastError` durante la reconexión.
- La opción del menú del icono hace `PUT /api/settings` con `KeepAlive` cambiado (la regla de no tocar la impresora seleccionada ya existe).

### D6. Bandeja
- Ajustes: casilla "Mantener activa (keep-alive)" + intervalo; "Desconectar tras" deshabilitado mientras esté marcada.
- Menú del icono: opción marcable "Mantener activa".
- Estado: "Activa (keep-alive)" cuando está conectada en modo persistente; "Reconectando…" durante los reintentos.

## Risks / Trade-offs

- **[`A3` no evita el apagado automático de la X5h]** → la prueba en curso lo dirá. Si la impresora se apaga igualmente, la opción sigue aportando conexión inmediata y reconexión automática al encenderla; se documentará la limitación y se podrá probar otro latido (`A4 33`) en un change posterior.
- **[Bloquea la impresora para otras apps]** → desactivada por defecto; opción en el menú del icono para soltarla al momento.
- **[Más consumo de batería de la impresora]** → intervalo configurable; documentarlo.
- **[Reintentos continuos con la impresora apagada]** → espera creciente hasta 60 s; cada intento fallido de RFCOMM/COM cuesta unos segundos sin bloquear al resto del servicio.

## Migration Plan

Ajustes nuevos con valores por defecto que mantienen el comportamiento actual. Despliegue con `uninstall.ps1 -KeepConfig` + `install.ps1`.

## Verification (2026-10-02)

- **Prueba con `A3`:** el enlace RFCOMM con la X5h se mantuvo sin cortes en todos los tramos observados (09:44:56–09:48:11 y 09:52:30–09:53 con el muestreo; 09:54–09:59 con el keep-alive ya instalado, 32 lecturas seguidas). La primera prueba se invalidó al volver a seleccionar la impresora (la sesión nueva perdía el muestreo: corregido en la tarea 2.5). La prueba de 45 min no llegó a completarse porque se reinstaló el servicio a mitad.
- **Despliegue:** reinstalado con `uninstall.ps1 -KeepConfig` + `install.ps1`; el usuario confirma que la opción funciona (conexión mantenida, reconexión y desactivación desde el menú).
- 164 tests automáticos en verde.

## Open Questions

- ¿Se apaga sola la X5h tras mucho tiempo (más de 45 min) aunque reciba `A3`? No medido todavía; si ocurre, el keep-alive se reconecta al encenderla.
