## ADDED Requirements

### Requirement: Opción de mantener activa
El sistema SHALL ofrecer el ajuste "Mantener activa (keep-alive)", desactivado por defecto, con un intervalo de latido configurable entre 10 y 300 segundos (30 por defecto). Con el ajuste desactivado, el comportamiento SHALL ser el actual (conexión bajo demanda y desconexión tras el tiempo de inactividad configurado).

#### Scenario: Valor por defecto
- **WHEN** se instala o actualiza el servicio sin cambiar ajustes
- **THEN** el keep-alive está desactivado y la impresora se desconecta tras el tiempo de inactividad configurado

#### Scenario: Intervalo fuera de rango
- **WHEN** se guarda un intervalo de latido de 2 segundos
- **THEN** se ajusta al mínimo de 10 segundos

### Requirement: Conexión persistente
Con el keep-alive activo, el sistema SHALL conectar con la impresora al arrancar el servicio o al seleccionarla, y SHALL NOT desconectarla por inactividad.

#### Scenario: Sin desconexión por inactividad
- **WHEN** el keep-alive está activo y pasan 10 minutos sin trabajos
- **THEN** el enlace sigue conectado

#### Scenario: Conexión al arrancar
- **WHEN** el servicio arranca con el keep-alive activo y la impresora encendida
- **THEN** el servicio se conecta sin esperar a ningún trabajo

### Requirement: Latido periódico
Con el keep-alive activo, el sistema SHALL enviar la consulta de estado `A3 00` cada intervalo de latido y SHALL actualizar el estado de la impresora con cada respuesta. El latido SHALL esperar a que termine el trabajo en curso y nunca intercalarse dentro de uno.

#### Scenario: Latido sin trabajos
- **WHEN** el keep-alive está activo con un intervalo de 30 s y no hay trabajos
- **THEN** se envía `A3 00` aproximadamente cada 30 s y el estado (papel, batería) se actualiza

#### Scenario: Latido durante una impresión
- **WHEN** toca enviar un latido mientras se imprime un trabajo
- **THEN** el latido se envía después de terminar el trabajo, sin insertar bytes en medio del flujo de filas

### Requirement: Reconexión automática
Con el keep-alive activo, si el enlace se pierde (impresora apagada, fuera de alcance u ocupada por otra aplicación) el sistema SHALL reintentar la conexión con espera creciente de 5 s a 60 s, y SHALL publicar el estado "Reconectando" mientras tanto.

#### Scenario: Impresora apagada y encendida
- **WHEN** la impresora se apaga con el keep-alive activo y se vuelve a encender
- **THEN** el estado pasa a "Reconectando" y el servicio se reconecta solo como máximo 60 s después de encenderla

#### Scenario: Desactivar durante la reconexión
- **WHEN** el usuario desactiva el keep-alive mientras se está reconectando
- **THEN** se dejan de hacer intentos y la sesión vuelve al modo bajo demanda

### Requirement: Independencia del muestreo de batería
El muestreo de batería SHALL seguir siendo temporal e independiente: activar o detener el muestreo no SHALL cambiar el ajuste de keep-alive, y la bandeja SHALL mostrar "Muestreando hasta…" solo durante un muestreo.

#### Scenario: Muestreo con keep-alive activo
- **WHEN** el keep-alive está activo y termina un muestreo de batería
- **THEN** la impresora sigue conectada con el latido configurado y la bandeja deja de mostrar "Muestreando"

### Requirement: Control desde la bandeja
La bandeja SHALL ofrecer la opción en la pestaña Ajustes (casilla e intervalo, deshabilitando "Desconectar tras" mientras esté activa) y como opción marcable "Mantener activa" en el menú del icono, que la activa o desactiva al instante. El estado SHALL mostrarse como "Activa (keep-alive)" o "Reconectando…".

#### Scenario: Desactivar desde el menú para usar el móvil
- **WHEN** el usuario desmarca "Mantener activa" en el menú del icono
- **THEN** el servicio vuelve al modo bajo demanda y libera la impresora tras el tiempo de inactividad configurado
