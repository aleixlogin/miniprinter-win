## 1. Base: proyecto de lógica y modelos de vista

- [x] 1.1 Crear `src/MiniPrinter.Gui` (`net8.0`, referencia a `MiniPrinter.Control`) y `tests/MiniPrinter.Gui.Tests`; añadirlos a la solución y a la bandeja
- [x] 1.2 Base de modelos de vista: `ObservableObject` (`INotifyPropertyChanged`), `RelayCommand`/`AsyncCommand` mínimos con estado «ejecutando» y `IUiDispatcher` (con versión síncrona para los tests); pruebas de cada pieza
- [x] 1.3 Preferencias por usuario: extraer de `QuickPrint.cs` un modelo `TrayPreferences` (atajo, ventana, tema, tamaño del texto) con lectura tolerante, validación de la posición contra los monitores y guardado atómico; pruebas con archivos antiguos, corruptos y posiciones fuera de pantalla

## 2. Textos en recursos

- [x] 2.1 `Strings.resx` (español) con clase tipada en `MiniPrinter.Gui`, extensión de marcado `{loc:T Clave}` en la bandeja y reserva que muestra el nombre de la clave
- [x] 2.2 Tabla de estados y mensajes del servicio (`Completed`, `Canceled`, `Aborted`, `Pending`, `Processing`, `ProcessingStopped`, `Printed`, `Printer did not answer`, `Printer needs attention: …`, …) hacia claves de recursos, con el texto original como reserva; pruebas de cada caso y del desconocido
- [x] 2.3 Migrar a recursos los textos de `MainWindow.xaml`/`.cs`, de los diálogos (nombre, recrear impresora, actualización, nota rápida) y del menú y los avisos de la bandeja
- [x] 2.4 Prueba automática que recorre los `.xaml` y el código de la bandeja buscando textos visibles literales (con lista corta de excepciones) y comprueba que toda clave usada existe

## 3. Tema

- [x] 3.1 Prueba de concepto: `Theme.Dark.xaml` con pinceles semánticos y estilos de `TabControl` y `ComboBox` en oscuro, comprobada por el usuario con el instalador; decidir si se sigue con recursos propios o se evalúa WPF UI
- [x] 3.2 Diccionarios `Theme.Light`, `Theme.Dark` y `Theme.HighContrast` (este último con `SystemColors`) y estilos de los controles usados: botón, casilla, cuadro de texto, lista desplegable, pestañas, lista con columnas y su cabecera, barra de desplazamiento y descripción emergente
- [x] 3.3 `ThemeService`: lectura de `AppsUseLightTheme` y de contraste alto, escucha de `UserPreferenceChanged`, preferencia Automático/Claro/Oscuro y cambio en caliente de todas las ventanas abiertas; barra de título oscura (`DWMWA_USE_IMMERSIVE_DARK_MODE`)
- [x] 3.4 Sustituir los colores fijos de `MainWindow`, el editor de plantillas, los diálogos y la nota rápida por pinceles de tema; prueba que busca colores literales fuera de los diccionarios
- [x] 3.5 Menú del icono de la bandeja con un renderizador que usa los colores del tema activo

## 4. Ventana y escala

- [x] 4.1 Recordar tamaño, posición, maximizada y pestaña (guardado al cerrar y con retardo al cambiar de tamaño) y restaurarlos validados contra los monitores
- [x] 4.2 Manifiesto con DPI por monitor y revisión de los tamaños fijos de `MainWindow` (columnas, márgenes, anchos de cuadros) a medidas relativas o `Auto`
- [x] 4.3 Tamaño del texto (Pequeño, Normal, Grande) con un único recurso de escala y tamaño mínimo de ventana que no corta nada en «Grande»
- [x] 4.4 Sección *Aspecto* en *Ajustes* (tema y tamaño del texto), que se aplica al instante

## 5. Comprobación y cierre

- [x] 5.1 Ejecutar todas las pruebas y las de la interfaz nuevas; comprobación visual por el usuario en claro, oscuro y contraste alto, con texto Grande y con otra escala de pantalla
- [x] 5.2 Documentar en el `README` (Aspecto, tema, tamaño del texto, ventana que recuerda) y en el historial de cambios
