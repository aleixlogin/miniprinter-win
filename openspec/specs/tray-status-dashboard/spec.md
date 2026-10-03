# tray-status-dashboard Specification

## Purpose
TBD - created by archiving change gui-settings-and-status. Update Purpose after archive.
## Requirements
### Requirement: Tarjeta de estado
La pestaña *Estado* SHALL empezar con una tarjeta que muestre un único estado, con icono y color, una frase que lo explique y, cuando proceda, un botón con la acción que toca. Los estados SHALL ser, por orden de prioridad: servicio no disponible, sin impresora elegida, con alarma (sin papel, sobrecalentada, batería baja), error de conexión, conectando o reconectando, imprimiendo, lista, desconectada.

#### Scenario: Sin papel
- **WHEN** la impresora informa de que no tiene papel
- **THEN** la tarjeta muestra «Sin papel» en color de aviso con la frase de que los trabajos esperan y se imprimirán al poner papel

#### Scenario: Sin impresora elegida
- **WHEN** no hay ninguna impresora seleccionada
- **THEN** la tarjeta muestra «Ninguna impresora» y el botón *Buscar impresoras* lleva a esa pestaña

#### Scenario: Servicio detenido
- **WHEN** la bandeja no puede hablar con el servicio
- **THEN** la tarjeta muestra «Servicio no disponible» con la indicación de comprobar que está iniciado, y el estado de mayor prioridad es ese aunque haya datos antiguos

#### Scenario: Lista
- **WHEN** la impresora está conectada y sin alarmas
- **THEN** la tarjeta muestra «Lista» en color correcto

#### Scenario: Se cambia el tema
- **WHEN** cambia el tema claro u oscuro
- **THEN** los colores de la tarjeta se adaptan y siguen distinguiéndose

### Requirement: Batería en la tarjeta
La tarjeta SHALL mostrar la batería como barra con porcentaje cuando la unidad sea conocida y como valor en bruto cuando no, y SHALL marcar en aviso el nivel por debajo del umbral configurado.

#### Scenario: Batería baja
- **WHEN** la batería está por debajo del umbral
- **THEN** la barra se muestra en color de aviso

#### Scenario: Unidad desconocida
- **WHEN** la unidad de batería no está configurada
- **THEN** se muestra el valor en bruto sin porcentaje

### Requirement: Acciones principales y secundarias
La pestaña *Estado* SHALL mostrar a la vista solo *Conectar* (o *Desconectar*) y *Página de prueba*. Las demás acciones (avanzar papel, muestrear batería, exportar el registro de batería, vista previa del último trabajo) SHALL estar en un menú «⋯» de la misma pestaña, y los datos de uso poco frecuente (firmware, direcciones de impresión, estado del servicio, versión) en un apartado *Detalles* plegable que recuerda si estaba abierto.

#### Scenario: Acciones a la vista
- **WHEN** el usuario abre *Estado*
- **THEN** ve solo *Conectar* y *Página de prueba* como botones, más el menú «⋯»

#### Scenario: Menú de acciones
- **WHEN** el usuario abre el menú «⋯»
- **THEN** están *Avanzar papel*, *Muestrear batería 8 h*, *Exportar registro de batería* y *Vista previa del último trabajo*

#### Scenario: Detalles
- **WHEN** el usuario despliega *Detalles*
- **THEN** ve el firmware, las direcciones desde las que se imprime, el estado del servicio y la versión

### Requirement: Estado siempre coherente con el servicio
La tarjeta SHALL actualizarse cuando cambie el estado del servicio sin que el usuario pulse nada, y SHALL conservar la selección y el desplazamiento de la lista de trabajos en cada actualización.

#### Scenario: Se acaba el papel con el panel abierto
- **WHEN** la impresora se queda sin papel con el panel abierto
- **THEN** la tarjeta pasa a «Sin papel» en pocos segundos y la fila seleccionada de la lista sigue seleccionada

