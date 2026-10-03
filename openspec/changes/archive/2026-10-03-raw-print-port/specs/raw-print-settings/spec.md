## ADDED Requirements

### Requirement: Apartado de ajustes del puerto de impresión directa
La pestaña *Ajustes* de la bandeja SHALL incluir un apartado "Impresión directa (puerto 9100)" con una casilla para activar el puerto, el número de puerto (1024–65535, 9100 por defecto), el estado del listener y una nota que advierta de que el puerto no tiene autenticación.

#### Scenario: Estado de fábrica
- **WHEN** el usuario abre *Ajustes* por primera vez
- **THEN** la casilla está desmarcada, el puerto es 9100 y el estado indica que está desactivado

#### Scenario: Activar
- **WHEN** el usuario marca la casilla y guarda
- **THEN** el estado pasa a "Escuchando en …:9100" con las direcciones según el alcance de red

#### Scenario: Puerto no válido
- **WHEN** el usuario escribe un puerto fuera de 1024–65535
- **THEN** se rechaza con un mensaje y no se guarda

#### Scenario: Puerto ocupado
- **WHEN** el puerto elegido está en uso
- **THEN** el apartado muestra el error junto al estado

### Requirement: Aviso de seguridad
Al activar el puerto con el modo "Toda la red local", la bandeja SHALL mostrar un aviso de que cualquier equipo de la red privada podrá imprimir sin autenticarse, con las opciones de continuar o cancelar.

#### Scenario: Activar en red local
- **WHEN** el usuario activa el puerto con el modo "Toda la red local" y guarda
- **THEN** se muestra el aviso y, si cancela, el puerto no se activa

#### Scenario: Solo este PC
- **WHEN** el usuario activa el puerto con el modo "Solo este PC"
- **THEN** no se muestra el aviso, porque solo se puede usar desde este equipo

### Requirement: Estado en la API de control
El estado de la API de control SHALL incluir si el puerto está activo, el puerto y el error si lo hay, para que la bandeja y otros clientes lo muestren.

#### Scenario: Consultar el estado
- **WHEN** se consulta el estado con el puerto activo
- **THEN** incluye el puerto y que está escuchando
