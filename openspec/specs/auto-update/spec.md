# auto-update Specification

## Purpose
TBD - created by archiving change auto-update. Update Purpose after archive.
## Requirements
### Requirement: Comprobación de versiones
La bandeja SHALL consultar `https://api.github.com/repos/aleixlogin/miniprinter-win/releases/latest` al arrancar (tras 1 minuto) y cada 24 horas mientras la opción "Comprobar actualizaciones automáticamente" esté activa (activada por defecto), y SHALL comparar la versión de la etiqueta (`vX.Y.Z`) con la instalada usando versionado semántico. Las releases marcadas como borrador o pre-release SHALL ignorarse.

#### Scenario: Hay una versión mayor
- **WHEN** la versión instalada es 0.4.0 y la última release es `v0.5.0`
- **THEN** la bandeja muestra una notificación "MiniPrinter 0.5.0 disponible"

#### Scenario: Sin novedades o sin conexión
- **WHEN** la última release es igual o anterior a la instalada, o la consulta falla
- **THEN** no se muestra ningún aviso y se vuelve a comprobar en el siguiente ciclo

### Requirement: Confirmación del usuario
La bandeja SHALL mostrar una ventana con la versión nueva, sus notas (cuerpo de la release) y las opciones "Actualizar", "Más tarde" y "Omitir esta versión". Nunca SHALL instalar nada sin que el usuario pulse "Actualizar".

#### Scenario: Omitir una versión
- **WHEN** el usuario elige "Omitir esta versión" para 0.5.0
- **THEN** no se vuelve a avisar de 0.5.0, pero sí de 0.5.1 o posteriores

#### Scenario: Más tarde
- **WHEN** el usuario elige "Más tarde"
- **THEN** se vuelve a avisar en la siguiente comprobación

### Requirement: Descarga verificada
Al aceptar, la bandeja SHALL descargar el instalador de la release (`MiniPrinter-Setup-X.Y.Z.exe`), `SHA256SUMS` y `SHA256SUMS.sig` a una carpeta temporal, SHALL verificar la firma ECDSA P-256 de `SHA256SUMS` con la clave pública incrustada en la aplicación y SHALL comprobar que el SHA-256 del instalador coincide con el listado. Si alguna verificación falla, SHALL borrar lo descargado, no ejecutar nada e informar del error.

#### Scenario: Firma correcta
- **WHEN** la firma y el hash son válidos
- **THEN** se ejecuta el instalador descargado

#### Scenario: Instalador manipulado
- **WHEN** el hash del instalador no coincide con `SHA256SUMS` o la firma no es válida
- **THEN** el archivo se borra, no se ejecuta y se muestra "La actualización no supera la verificación de seguridad"

### Requirement: Instalación mediante el instalador
La bandeja SHALL ejecutar el instalador descargado con `/SILENT /SUPPRESSMSGBOXES /NORESTART` sin elevar ella misma; el instalador SHALL pedir permisos de administrador (UAC), cerrar la bandeja y detener el servicio, actualizar, volver a arrancar el servicio y reabrir la bandeja sin privilegios. Si el usuario rechaza el UAC, la versión instalada SHALL seguir funcionando sin cambios.

#### Scenario: Actualización aceptada
- **WHEN** el usuario pulsa "Actualizar" y acepta el UAC
- **THEN** al terminar la bandeja vuelve a abrirse con la versión nueva y la impresora seleccionada se conserva

#### Scenario: UAC rechazado
- **WHEN** el usuario rechaza el aviso UAC
- **THEN** no cambia nada y la bandeja informa de que la actualización se canceló

### Requirement: Control manual y versión visible
El menú del icono SHALL incluir "Buscar actualizaciones" (comprobación inmediata con resultado visible aunque no haya novedades), el panel SHALL mostrar la versión instalada y los ajustes SHALL permitir desactivar la comprobación automática.

#### Scenario: Comprobación manual sin novedades
- **WHEN** el usuario pulsa "Buscar actualizaciones" y no hay versión nueva
- **THEN** se muestra "MiniPrinter está actualizado (versión X.Y.Z)"

