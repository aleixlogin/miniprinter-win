## 1. Verificación previa

- [x] 1.1 Analizar la prueba de 45 min de keep-alive con `A3` (2026-10-02) y anotar en `design.md` si la X5h mantiene el enlace y si se apaga sola

## 2. Sesión

- [x] 2.1 Añadir `Persistent` y `HeartbeatInterval` a `SessionOptions` y `SetPersistent(bool, TimeSpan)` a `PrinterSession`, separado de `KeepAliveUntil` (muestreo)
- [x] 2.2 En el bucle de mantenimiento: sin desconexión por inactividad, latido `A3` serializado con los trabajos, conexión inmediata al activar
- [x] 2.3 Reconexión con espera creciente 5 s → 60 s (incluidos errores `Busy`) y estado `Connecting` durante los reintentos
- [x] 2.5 Conservar un muestreo de batería en curso cuando se cambia la impresora o su transporte (fallo detectado en la prueba del 2026-10-02: la sesión nueva lo perdía), con test
- [x] 2.4 Tests con el transporte simulado: sin desconexión en modo persistente, latidos periódicos, latido no intercalado en un trabajo, reconexión tras caída, desactivar durante la reconexión, muestreo independiente

## 3. Servicio y API

- [x] 3.1 Ajustes `KeepAlive` y `KeepAliveIntervalSeconds` (validación 10–300) con test de carga de un `settings.json` anterior
- [x] 3.2 `PrinterManager` aplica el modo persistente al crear la sesión y al cambiar los ajustes sin recrearla
- [x] 3.3 `StatusDto.KeepAlive` y test de integración de la API de control

## 4. Bandeja

- [x] 4.1 Casilla "Mantener activa (keep-alive)" e intervalo en Ajustes, deshabilitando "Desconectar tras" mientras esté marcada
- [x] 4.2 Opción marcable "Mantener activa" en el menú del icono
- [x] 4.3 Textos de estado "Activa (keep-alive)" y "Reconectando…"

## 5. Despliegue

- [x] 5.1 Tests completos, publicar y reinstalar con `uninstall.ps1 -KeepConfig` + `install.ps1`
- [x] 5.2 Verificar con la impresora real: conexión mantenida, reconexión al apagar y encender, desactivar desde el menú y usar el móvil
- [x] 5.3 Documentar la opción en el README (efectos sobre el móvil y la batería)
