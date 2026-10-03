## ADDED Requirements

### Requirement: Tema que sigue a Windows
La bandeja SHALL tener un tema claro, uno oscuro y uno de contraste alto. Por defecto (Automático) SHALL usar el tema de aplicaciones de Windows y cambiar al instante cuando Windows lo cambie, sin reiniciar. En *Ajustes → Aspecto* el usuario SHALL poder fijar Automático, Claro u Oscuro; con el contraste alto de Windows activo SHALL usarse siempre el de contraste alto, con los colores del sistema.

#### Scenario: Windows en modo oscuro
- **WHEN** Windows usa el tema oscuro de aplicaciones y el ajuste es Automático
- **THEN** todas las ventanas de la bandeja (panel, editor de plantillas, diálogos, nota rápida) se muestran en oscuro, con la barra de título también oscura

#### Scenario: Cambio de tema de Windows con el panel abierto
- **WHEN** el usuario cambia el tema de Windows mientras el panel está abierto
- **THEN** el panel cambia de tema sin cerrarse y sin perder lo que había escrito

#### Scenario: Tema fijado por el usuario
- **WHEN** el usuario elige Oscuro en *Ajustes → Aspecto* con Windows en claro
- **THEN** la bandeja sigue en oscuro aunque Windows cambie, hasta que vuelva a Automático

#### Scenario: Contraste alto
- **WHEN** Windows tiene un tema de contraste alto
- **THEN** la bandeja usa los colores del sistema y todo el texto, los bordes y la selección se distinguen

#### Scenario: Sin colores sueltos
- **WHEN** se revisan los XAML de la bandeja
- **THEN** no hay colores literales fuera de los diccionarios de tema

### Requirement: Menú del icono con el tema
El menú contextual del icono de la bandeja SHALL pintarse con los colores del tema activo.

#### Scenario: Menú en oscuro
- **WHEN** el tema activo es oscuro y se abre el menú del icono
- **THEN** el fondo, el texto, la selección y los separadores son los del tema oscuro

### Requirement: Tamaño del texto
En *Ajustes → Aspecto* el usuario SHALL poder elegir el tamaño del texto (Pequeño, Normal, Grande) y la interfaz SHALL ajustarse sin cortar textos ni controles, también con la escala de pantalla de Windows (DPI por monitor).

#### Scenario: Texto grande
- **WHEN** el usuario elige Grande
- **THEN** el texto de todas las pantallas aumenta y la ventana crece hasta su tamaño mínimo para que no se corte nada

#### Scenario: Cambio de monitor
- **WHEN** la ventana se mueve a un monitor con otra escala
- **THEN** se redibuja nítida con el tamaño correcto

### Requirement: La ventana recuerda su estado
La bandeja SHALL recordar por usuario el tamaño, la posición, si estaba maximizada y la última pestaña, y SHALL restaurarlos al volver a abrir el panel. Si la posición guardada ya no cae en ningún monitor conectado, la ventana SHALL abrirse centrada.

#### Scenario: Reabrir el panel
- **WHEN** el usuario cambia el tamaño, mueve el panel, elige la pestaña Ajustes y lo cierra y vuelve a abrir
- **THEN** el panel se abre en el mismo sitio, con el mismo tamaño y en la pestaña Ajustes

#### Scenario: Monitor desconectado
- **WHEN** el panel estaba en un monitor que ya no está conectado
- **THEN** se abre centrado en el monitor principal

#### Scenario: Archivo antiguo
- **WHEN** `tray.json` no tiene los campos de ventana ni de tema
- **THEN** se usan los valores por defecto sin error y sin perder el resto de preferencias

### Requirement: Lógica de pantallas probable
La lógica de las pantallas migradas SHALL estar en modelos de vista sin dependencia de WPF, de modo que sus cambios de estado y órdenes se puedan comprobar con pruebas automáticas sin abrir ventanas.

#### Scenario: Prueba sin ventanas
- **WHEN** se ejecutan las pruebas del proyecto de la interfaz
- **THEN** se comprueban los modelos de vista (tema, preferencias de ventana, diálogos) sin crear ninguna ventana
