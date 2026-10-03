## Context

`MiniPrinter.Tray` es WPF sobre .NET 8 (`net8.0-windows`) con cuatro pestañas en `MainWindow.xaml` (344 líneas) y su código asociado (695 líneas), más ventanas hechas en código (editor de plantillas, nota rápida, diálogos de nombre, recrear impresora, actualización) y un `NotifyIcon` de Windows Forms con su menú. `App.xaml` solo define tres estilos. Los colores están fijos en el XAML y el código, y los textos escritos en español en ambos. Las preferencias por usuario viven en `%LocalAppData%\MiniPrinter\tray.json` (`QuickPrint.cs`, hoy solo el atajo de la nota rápida) y los favoritos de plantillas en el mismo directorio.

La lógica de pantallas que ya está probada vive fuera de la bandeja, en `MiniPrinter.Control` (`TemplateEditorModel`, `PaperSizes`, `QueueRecreator`); el resto está pegado a los controles y solo se prueba abriendo la aplicación.

## Goals / Non-Goals

**Goals:**
- Que la lógica de Estado, Ajustes y los diálogos se pueda probar con xUnit sin abrir ventanas.
- Modo claro, oscuro y de contraste alto que siguen a Windows y cambian en caliente.
- Que ningún texto visible esté escrito en el código o el XAML.
- Que la ventana recuerde su sitio y se pueda leer con otro tamaño de texto o DPI.

**Non-Goals:**
- Rehacer las pantallas (lo hacen `gui-settings-and-status` y `gui-diagnostics-and-templates`).
- Traducir a otro idioma, o elegirlo en ejecución.
- Adoptar un marco MVVM ni una librería de temas.

## Decisions

### D1. Proyecto `MiniPrinter.Gui` sin WPF
Un proyecto `net8.0` (como `MiniPrinter.Control`) con los modelos de vista, la tabla de textos y los modelos de preferencias y tema. Depende de `MiniPrinter.Control` (contratos y cliente). La bandeja lo referencia y los tests también, sin necesitar WPF ni un hilo de interfaz.
- Los modelos de vista usan `INotifyPropertyChanged`, `ICommand` propio mínimo y un pequeño `IUiDispatcher` (para marcar hilos en los tests). No se usa CommunityToolkit.Mvvm ni similares: el trabajo es pequeño y así no hay dependencias nuevas.
- *Alternativa descartada*: meter los modelos de vista en `MiniPrinter.Control`. Mezclaría contratos de red con estado de pantallas.

### D2. Textos en `Strings.resx`
`Strings.resx` (neutro, español) en `MiniPrinter.Gui`, con clase tipada generada, para que los modelos de vista y el XAML compartan los textos. En XAML se usa una extensión de marcado `{loc:T Clave}` que lee la misma tabla. Las claves son estables y descriptivas (`Settings.Paper.Title`, `Job.State.Completed`). Un test recorre los `.xaml` y el código de la bandeja buscando textos literales visibles (con una lista corta de excepciones: nombres de producto, formatos) para que no vuelvan a colarse.
- La **traducción de los estados del servicio**: el servicio devuelve texto en inglés (`Completed`, `ProcessingStopped`, `Printer did not answer`). Una tabla en la biblioteca los asocia a claves de recursos; lo que no está en la tabla se muestra tal cual. No se cambia el servicio.
- *Alternativa descartada*: archivos JSON propios de textos. El `.resx` ya trae herramientas, cultura y generación de clases.

### D3. Tema con recursos propios (no WPF UI ni ModernWpf)
Se escriben diccionarios `Theme.Light.xaml`, `Theme.Dark.xaml` y `Theme.HighContrast.xaml` con **pinceles semánticos** (`Surface`, `SurfaceAlt`, `Text`, `TextMuted`, `Accent`, `Ok`, `Warn`, `Error`, `Border`, `Selection`), y un diccionario de estilos que los usa con `DynamicResource` para los controles que la aplicación utiliza. Cambiar de tema es sustituir el diccionario activo.
- **Por qué no una librería**: WPF UI es un rediseño completo (ventanas con Mica, navegación propia) que obligaría a rehacer también las ventanas hechas en código; ModernWpf no se mantiene. La aplicación tiene cinco tipos de ventana y unos ocho tipos de control. El coste de escribirlos es menor que el de adaptarse a una librería y no añade dependencias ni licencias.
- **Riesgo y salida**: si los estilos propios se descontrolan (por ejemplo, el `ComboBox` y las pestañas son plantillas largas), la primera tarea es una prueba de concepto con esos dos controles en oscuro; si no sale limpia, se vuelve a decidir y se evalúa WPF UI antes de seguir.
- El **contraste alto** no usa colores propios: el diccionario apunta a `SystemColors` (`WindowBrush`, `WindowTextBrush`, `HighlightBrush`…).
- **Seguir a Windows**: se lee `AppsUseLightTheme` del registro y se escucha `SystemEvents.UserPreferenceChanged` y `SystemParameters.HighContrast`. La preferencia del usuario (Automático, Claro, Oscuro) manda sobre Windows salvo en contraste alto.
- **Menú de la bandeja** (Windows Forms): un `ToolStripProfessionalRenderer` con una tabla de colores sacada del tema activo.
- **Barra de título**: en oscuro se activa `DWMWA_USE_IMMERSIVE_DARK_MODE` para que no quede una barra clara.

### D4. Memoria de la ventana
Se amplía `tray.json` (clase de preferencias de `QuickPrint.cs`) con `Window { Left, Top, Width, Height, Maximized, Tab }`, `Theme` (`Auto`/`Light`/`Dark`) y `TextSize` (`Small`/`Normal`/`Large`). Al cargar, la posición se valida contra `SystemParameters.VirtualScreen*`/pantallas conectadas y, si no cae en ninguna, se centra. Se guarda al cerrar y, con retardo, al cambiar tamaño. Un archivo sin esos campos funciona igual.

### D5. Escalado
El manifiesto declara DPI por monitor (`PerMonitorV2`) y la ventana usa unidades independientes del dispositivo. El tamaño del texto se aplica con un único recurso de escala (`FontSize` base 12/13/15) y los anchos de columnas y márgenes que hoy son píxeles fijos pasan a medidas relativas o `Auto` donde cabe. El tamaño mínimo de la ventana se calcula para que no se corte nada en «Grande».

### D6. Qué se migra aquí
Aquí solo se crea la capa y se migran: el diálogo de nombre, el de recrear impresora, el de actualización, la nota rápida y los textos y colores de `MainWindow`. **No** se migra la lógica de Estado ni de Ajustes (lo hace el change siguiente, sobre esta base) ni el editor de plantillas (ya tiene su modelo).

## Risks / Trade-offs

- **Estilos de control a mano** → más líneas de XAML y más a ojo. Mitigación: prueba de concepto primero, diccionarios con pocos pinceles, comprobación visual por el usuario con el instalador.
- **Los textos en recursos hacen más lento escribir pantallas** → se asume: es el precio de poder traducir; las claves son legibles.
- **El `NotifyIcon` de Windows Forms no admite bien los temas** → solo se pinta el menú; el icono ya tiene estados propios.
- **Cambiar de tema en caliente puede dejar colores sueltos** si algún control usa un color fijo → un test recorre los XAML y busca colores literales fuera de los diccionarios de tema.
- **Dos hilos**: el modelo de vista recibe eventos del cliente de control en un hilo de fondo → `IUiDispatcher` los lleva al hilo de la interfaz.

## Migration Plan

1. Proyecto `MiniPrinter.Gui` y de tests, con la base de modelos de vista y `IUiDispatcher`.
2. `Strings.resx`, extensión de XAML y tabla de estados del servicio; migrar los textos de `MainWindow`, de los diálogos y del menú de la bandeja.
3. Prueba de concepto del tema (pestañas y lista desplegable en oscuro); si sale bien, diccionarios, estilos y cambio en caliente.
4. Memoria de ventana, DPI y tamaño del texto; sección *Aspecto* en *Ajustes* (solo esa sección, el resto lo hace el change siguiente).
5. Pruebas, comprobación visual en claro, oscuro y contraste alto, y README.

Sin migración de datos: `tray.json` solo gana campos. Revertir es dejar el tema en Claro.

## Open Questions

- ¿Hace falta un cuarto tema de acento (color elegible)? Se deja fuera; el acento sale del color de acento de Windows (`SystemParameters` / `UISettings`) si está disponible.
- ¿La traducción a inglés se hace después con el mismo `.resx` (cultura `en`)? Está preparado, pero queda fuera.
