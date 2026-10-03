## ADDED Requirements

### Requirement: Ajustes por secciones
La pestaña *Ajustes* SHALL mostrar a la izquierda un menú de secciones (Impresión, Papel, Red, Impresión directa, Automatización, Batería, Aspecto, General) y, a la derecha, solo los ajustes de la sección elegida. Cada ajuste existente SHALL estar en exactamente una sección y conservar su efecto.

#### Scenario: Elegir una sección
- **WHEN** el usuario elige *Red* en el menú
- **THEN** se muestran solo el alcance de red y el puerto IPP

#### Scenario: Todos los ajustes siguen
- **WHEN** se recorren las ocho secciones
- **THEN** están todos los ajustes que existían antes de este cambio, sin duplicados

#### Scenario: Última sección
- **WHEN** el usuario cierra el panel en la sección *Papel* y lo vuelve a abrir
- **THEN** *Ajustes* se abre en *Papel*

### Requirement: Guardado por sección
Cada sección SHALL tener sus botones *Aplicar* y *Descartar*. *Aplicar* SHALL guardar solo los ajustes de esa sección sobre los valores actuales del servicio, sin modificar los de otras secciones ni los que hayan cambiado desde otro sitio. *Descartar* SHALL volver a los valores guardados.

#### Scenario: Dos secciones con cambios
- **WHEN** el usuario cambia el tema en *Aspecto* y la oscuridad en *Impresión* y pulsa *Aplicar* solo en *Impresión*
- **THEN** se guarda la oscuridad, el tema sigue sin guardar y *Aspecto* conserva su cambio pendiente

#### Scenario: Cambio hecho por otro cliente
- **WHEN** otro cliente cambia el puerto IPP mientras el usuario edita *Impresión* y este pulsa *Aplicar*
- **THEN** se guardan los ajustes de *Impresión* y el puerto IPP sigue siendo el que puso el otro cliente

#### Scenario: Descartar
- **WHEN** el usuario cambia tres ajustes de una sección y pulsa *Descartar*
- **THEN** vuelven los valores guardados y desaparece el indicador de cambios

#### Scenario: Papel y recreación de la impresora
- **WHEN** el usuario cambia los tamaños de papel y pulsa *Aplicar* en *Papel*
- **THEN** se ofrece recrear la impresora de Windows, y no se ofrece al aplicar otra sección

### Requirement: Cambios sin guardar visibles
Una sección con cambios sin aplicar SHALL marcarse en el menú lateral y mostrar una barra con «Cambios sin guardar». Cambiar de sección NO SHALL descartar esos cambios, y cerrar el panel con cambios pendientes SHALL preguntar si aplicarlos, descartarlos o cancelar.

#### Scenario: Marca en el menú
- **WHEN** el usuario cambia un ajuste de *Red*
- **THEN** *Red* aparece marcada en el menú hasta que se aplique o se descarte

#### Scenario: Ir a otra sección
- **WHEN** el usuario tiene cambios en *Red* y abre *Papel*
- **THEN** al volver a *Red* los cambios siguen ahí

#### Scenario: Cerrar con cambios
- **WHEN** el usuario cierra el panel con cambios sin aplicar
- **THEN** se pregunta si aplicarlos, descartarlos o cancelar el cierre

### Requirement: Validación junto al campo
Cada campo con una regla (puerto entre 1024 y 65535, números, tamaños de papel, formato del atajo) SHALL mostrar su error justo debajo, y *Aplicar* SHALL estar desactivado mientras la sección tenga errores.

#### Scenario: Puerto fuera de rango
- **WHEN** el usuario escribe 80 como puerto de impresión directa
- **THEN** el campo muestra que debe estar entre 1024 y 65535 y *Aplicar* no se puede pulsar

#### Scenario: Corregir el error
- **WHEN** el usuario escribe 9100
- **THEN** desaparece el mensaje y *Aplicar* se activa

### Requirement: Confirmaciones de los cambios delicados
Aplicar *Toda la red local* o activar la impresión directa en red local SHALL seguir pidiendo confirmación (con el aviso de que no hay autenticación en el 9100), y cancelarla SHALL dejar la sección sin guardar.

#### Scenario: Red local
- **WHEN** el usuario activa la impresión directa con «Toda la red local» y pulsa *Aplicar*
- **THEN** se muestra el aviso y, si cancela, no se guarda nada de esa sección

### Requirement: Búsqueda de ajustes
Una caja de búsqueda SHALL filtrar el menú lateral a las secciones con coincidencias (en el nombre del ajuste o sus sinónimos) y resaltar los ajustes que coinciden dentro de la sección, sin cambiar ningún valor.

#### Scenario: Buscar «puerto»
- **WHEN** el usuario escribe «puerto»
- **THEN** el menú muestra *Red* (puerto IPP) e *Impresión directa* (puerto 9100) y resalta esos campos

#### Scenario: Sin resultados
- **WHEN** nada coincide
- **THEN** se dice «Sin resultados» y se puede borrar la búsqueda con un clic

### Requirement: Ajuste del historial de trabajos
La sección *Impresión* SHALL incluir cuántos trabajos recientes se guardan para la vista previa y la reimpresión (0 a 50, por defecto 10) con un texto que avise de que se guarda lo impreso.

#### Scenario: Desactivar el historial
- **WHEN** el usuario pone 0 y aplica
- **THEN** el servicio deja de guardar las imágenes de los trabajos, salvo el último, y borra las demás
