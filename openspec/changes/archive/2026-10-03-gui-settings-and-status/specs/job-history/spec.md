## ADDED Requirements

### Requirement: Origen de cada trabajo
Cada trabajo SHALL registrar su origen en el punto por el que entró: **Windows** (impresión IPP, con el usuario de Windows), **Panel** (la bandeja: nota rápida, portapapeles, archivos, plantillas, página de prueba), **API** (API de automatización, con la dirección del cliente) o **Puerto 9100** (con la dirección del cliente). La API de control SHALL devolverlo en `source` y `origin`.

#### Scenario: Impresión desde una aplicación de Windows
- **WHEN** se imprime desde el Bloc de notas
- **THEN** el trabajo tiene origen Windows y el usuario que imprimió

#### Scenario: Impresión por el puerto 9100
- **WHEN** un cliente en 192.168.0.30 imprime por el puerto 9100
- **THEN** el trabajo tiene origen Puerto 9100 y la dirección 192.168.0.30

#### Scenario: Impresión con la API de automatización
- **WHEN** un script imprime con el token de la API
- **THEN** el trabajo tiene origen API y la dirección del cliente

#### Scenario: Trabajos antiguos
- **WHEN** un cliente antiguo lee los trabajos
- **THEN** los campos nuevos no rompen su lectura

### Requirement: Lista de trabajos en español
La lista de trabajos de la bandeja SHALL mostrar el estado y el mensaje en el idioma de la interfaz, con un icono por estado y una columna *Origen*.

#### Scenario: Trabajo impreso
- **WHEN** un trabajo se ha impreso
- **THEN** la lista muestra «Completado», «Impreso» y su origen

#### Scenario: Trabajo retenido
- **WHEN** un trabajo espera por falta de papel
- **THEN** la lista muestra «En espera» y «Sin papel», con el icono de aviso

### Requirement: Vista previa de cualquier trabajo reciente
Un doble clic en un trabajo de la lista SHALL abrir la vista previa de sus páginas, tal como se imprimieron, si el servicio las conserva; si no, SHALL decir por qué («ya no está guardado», «demasiado grande»). El servicio SHALL guardar las páginas de los últimos trabajos según el ajuste de historial (por defecto 10, de 0 a 50; 0 solo el último), hasta un máximo de 50 MB y sin guardar trabajos de más de 40 páginas, y SHALL borrar las más antiguas al superar el límite.

#### Scenario: Ver un trabajo anterior
- **WHEN** el usuario hace doble clic en un trabajo de hace tres impresiones
- **THEN** se abre la vista previa con sus páginas

#### Scenario: Trabajo ya borrado
- **WHEN** el trabajo es más antiguo que el límite de historial
- **THEN** la ventana dice que ya no está guardado

#### Scenario: Historial desactivado
- **WHEN** el ajuste es 0
- **THEN** solo se conserva la vista previa del último trabajo

#### Scenario: Límite de espacio
- **WHEN** las páginas guardadas superan los 50 MB
- **THEN** se borran las de los trabajos más antiguos hasta volver bajo el límite

#### Scenario: Trabajo muy grande
- **WHEN** un trabajo tiene más de 40 páginas
- **THEN** no se guardan sus páginas y la vista previa lo indica

### Requirement: Reimprimir un trabajo
La lista SHALL ofrecer *Reimprimir* en los trabajos cuyas páginas se conservan. Reimprimir SHALL poner en la cola las mismas páginas, con la misma oscuridad y modo de impresión, como un trabajo nuevo («Reimpresión de …») con origen Panel, sin volver a procesar el documento original.

#### Scenario: Reimprimir un tique
- **WHEN** el usuario elige *Reimprimir* en un trabajo completado
- **THEN** aparece un trabajo nuevo en la cola y se imprime lo mismo que la primera vez

#### Scenario: Trabajo cancelado
- **WHEN** el trabajo se canceló pero se conservan sus páginas
- **THEN** se puede reimprimir

#### Scenario: Sin páginas guardadas
- **WHEN** las páginas del trabajo ya no están
- **THEN** *Reimprimir* no se ofrece y, si se pidiera por la API, el servicio responde que no existen

### Requirement: Privacidad del historial
Las páginas guardadas SHALL poder desactivarse, SHALL borrarse al desinstalar la aplicación y SHALL estar dentro del directorio de datos del servicio, y el ajuste SHALL advertir de que se guarda lo impreso.

#### Scenario: Desinstalar
- **WHEN** se desinstala la aplicación
- **THEN** el directorio de trabajos guardados se borra

#### Scenario: Al arrancar
- **WHEN** el servicio arranca y hay directorios que no corresponden a ningún trabajo conocido
- **THEN** se borran y el historial se recorta al límite
