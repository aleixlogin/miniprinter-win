## 1. Servicio: origen e historial con imágenes (job-history)

- [x] 1.1 `JobInfo.Source` y `Origin`; registrarlos en `IppPrinterService` (Windows y usuario IPP), `ControlApi` (Panel), `AutomationApi` (API y dirección del cliente) y `RawPortHost` (Puerto 9100 y dirección); campos `source`, `origin` y `canReprint` en `JobDto` (opcionales para clientes antiguos) y tests de cada entrada
- [x] 1.2 Ajuste `JobHistoryKeep` (0–50, por defecto 10) con validación en `SettingsStore` y en `PUT /api/settings`
- [x] 1.3 Retención de páginas en `%ProgramData%\MiniPrinter\jobs\<id>\` (PNG de 1 bit y `job.json`) al terminar o cancelar un trabajo, con tope de 50 MB, sin guardar más de 40 páginas, recorte de los más antiguos y limpieza al arrancar; tests de los límites
- [x] 1.4 `GET /api/jobs/{id}/pages/{n}` y `POST /api/jobs/{id}/reprint` (404 sin páginas, 409 si el trabajo no ha terminado) manteniendo `GET /api/jobs/last/pages/{n}`; `GetJobPageAsync` y `ReprintJobAsync` en `ControlClient`; tests con el servicio de pruebas
- [x] 1.5 El desinstalador borra `jobs\`; probar con el instalador

## 2. Ajustes por secciones (tray-settings-navigation)

- [x] 2.1 Modelo de vista de *Ajustes*: lista de secciones, sección elegida (y recordada), búsqueda con índice de palabras clave en recursos, y servicio de guardado *leer–cambiar–escribir* con aviso si el servicio cambió mientras se editaba; tests
- [x] 2.2 `SettingsSectionViewModel` base: campos, `IsDirty`, errores por campo, `ApplyAsync` y `Discard`; tests de cambios sin guardar, dos secciones a la vez y cambio concurrente
- [x] 2.3 Secciones *Aspecto* y *General* (las más sencillas), con su vista, para validar el patrón
- [x] 2.4 Secciones *Impresión* (incluye el historial de trabajos) y *Batería*
- [x] 2.5 Sección *Red* y *Impresión directa* con las confirmaciones de red local y el aviso de seguridad del 9100
- [x] 2.6 Sección *Papel*: el bloque de tamaños (hoy `PaperSettingsPanel`), el nombre en Windows y la recreación de la impresora al aplicar; sección *Automatización* con el token
- [x] 2.7 Menú lateral con marca de cambios, barra «Cambios sin guardar», aviso al cerrar con cambios y validación junto a cada campo; retirar el *Guardar* único y el bloque antiguo de `MainWindow.xaml`
- [x] 2.8 Tests de que cada ajuste de `ServiceSettings` editable aparece en una sola sección

## 3. Estado como panel (tray-status-dashboard)

- [x] 3.1 `StatusViewModel` con la función de estado derivado (tabla de casos por prioridad), batería y acciones; tests de todos los estados
- [x] 3.2 Vista de *Estado*: tarjeta con icono y color de tema, botón de acción sugerida, *Conectar*/*Página de prueba*, menú «⋯» y apartado *Detalles* plegable que recuerda su estado
- [x] 3.3 Refresco automático conservando selección y desplazamiento de la lista

## 4. Lista de trabajos (job-history en la bandeja)

- [x] 4.1 `JobRowViewModel`: estado y mensaje traducidos, icono, origen y acciones disponibles; tests con los estados y mensajes conocidos
- [x] 4.2 Columna *Origen*, iconos y acciones *Cancelar* y *Reimprimir*; doble clic abre la vista previa de ese trabajo (ventana de vista previa con varias páginas) y explica por qué falta cuando no hay páginas
- [x] 4.3 Ajuste «Guardar los últimos N trabajos» en *Impresión* con el aviso de privacidad

## 5. Comprobación y cierre

- [x] 5.1 Ejecutar todas las pruebas; comprobación visual por el usuario con el instalador (Ajustes por secciones, Estado con cada estado posible, vista previa y reimpresión de un trabajo anterior)
- [x] 5.2 README (Ajustes por secciones, tarjeta de estado, historial y reimpresión, ajuste del historial) e historial de cambios
