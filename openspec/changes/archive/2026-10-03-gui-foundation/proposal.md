## Why

La bandeja es una ventana WPF escrita casi toda en código (`MainWindow.xaml.cs` tiene 695 líneas y la lógica está pegada a los controles), con los colores fijos (`#57606A`, `#FFF8C5`…), los textos escritos dentro del código y del XAML, y sin recordar nada de la ventana. Eso tiene tres consecuencias: no se puede probar nada de la interfaz sin abrirla, no se puede tener modo oscuro ni contraste alto, y cualquier mejora grande de pantallas (los dos changes siguientes: `gui-settings-and-status` y `gui-diagnostics-and-templates`) se haría sobre una base que no admite pruebas ni cambios de aspecto.

Este change no cambia lo que hace la aplicación: pone los cimientos para poder mejorarla sin romperla.

## What Changes

- **Lógica fuera de las ventanas**: un proyecto nuevo `MiniPrinter.Gui` (sin WPF) con **modelos de vista** (`INotifyPropertyChanged`, sin librerías) y servicios pequeños, probados con xUnit. Las ventanas solo enlazan y delegan. Se migran primero las pantallas que luego se rehacen (Estado y Ajustes) y los diálogos pequeños; el editor de plantillas ya sigue este patrón (`TemplateEditorModel`).
- **Textos en recursos**: todos los textos visibles pasan a `Strings.resx` (español), con una clase tipada y una extensión de XAML, para poder traducir después sin tocar pantallas. Los textos del servicio que la bandeja muestra (estados, mensajes) se traducen en una tabla de la biblioteca, con el texto original como reserva.
- **Tema que sigue a Windows**: colores semánticos (superficie, texto, apagado, acento, aviso, correcto, error, borde) en diccionarios claro, oscuro y de contraste alto, que cambian al instante cuando cambia el tema de Windows o el usuario elige uno en *Ajustes → Aspecto* (Automático / Claro / Oscuro). Estilos propios para los controles que se usan (botón, casilla, cuadro de texto, lista desplegable, pestañas, lista con columnas, barras de desplazamiento) y el menú de la bandeja.
- **Ventana que recuerda**: tamaño, posición (validada contra los monitores conectados), maximizada y última pestaña, por usuario en `tray.json`.
- **Escalado**: manifiesto con DPI por monitor y tamaño del texto elegible (pequeño, normal, grande) en *Ajustes → Aspecto*.

## Capabilities

### New Capabilities
- `tray-appearance`: tema (automático, claro, oscuro, contraste alto), tamaño del texto, DPI y memoria de la ventana.
- `tray-localization`: los textos de la interfaz en recursos y la traducción de los estados del servicio.

### Modified Capabilities
<!-- Ninguna: no cambia el comportamiento descrito en las specs existentes; solo cómo se dibuja y de dónde salen los textos. -->

## Impact

- **Código**: nuevo `src/MiniPrinter.Gui` (modelos de vista, tabla de textos del servicio, almacén de preferencias de ventana, modelo del tema) y `tests/MiniPrinter.Gui.Tests`; `MiniPrinter.Tray` (diccionarios de tema, estilos, `Strings.resx`, ventanas más delgadas, manifiesto).
- **Dependencias**: ninguna nueva (se descartan WPF UI y ModernWpf, ver `design.md`).
- **Compatibilidad**: sin tocar el tema (Automático) el aspecto en un Windows claro es el de hoy. `tray.json` gana campos opcionales; un archivo antiguo se lee igual.
- **Riesgos**: los estilos de controles propios son trabajo manual y se prueban a ojo; por eso la primera tarea es una prueba de concepto de pestañas y lista desplegable en oscuro.
- **Fuera de alcance**: cambiar pantallas (los otros dos changes), traducir a otro idioma (solo se prepara), tema para el editor de plantillas más allá de los colores semánticos que ya hereda.
