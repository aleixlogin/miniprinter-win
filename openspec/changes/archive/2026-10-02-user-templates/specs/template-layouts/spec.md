## ADDED Requirements

### Requirement: Formato de plantilla declarativo
El sistema SHALL describir cada plantilla como un documento JSON con `name`, `title`, `description`, `mode` (`text` o `image`), `fields` (nombre, etiqueta, tipo, obligatorio, opciones, valor por defecto) y `blocks` (lista ordenada de bloques). Los textos de los bloques SHALL admitir marcadores `{{campo}}` que se sustituyen por el valor del campo.

#### Scenario: Sustitución de campos
- **WHEN** una plantilla tiene un bloque `text` con el valor "Hola {{nombre}}" y se imprime con `nombre` = "Ana"
- **THEN** se imprime "Hola Ana"

#### Scenario: Campo no declarado
- **WHEN** un bloque usa `{{cliente}}` y la plantilla no declara el campo `cliente`
- **THEN** la plantilla se rechaza al validarla indicando el marcador desconocido

#### Scenario: Campo opcional vacío
- **WHEN** un marcador `{{linea2}}` corresponde a un campo opcional sin valor
- **THEN** se sustituye por una cadena vacía y un bloque `text` cuyo resultado es vacío no ocupa altura

### Requirement: Plantillas integradas como JSON
Las plantillas integradas (`qr`, `barcode`, `todo`, `label`, `sticker` y las nuevas) SHALL definirse con este mismo formato e interpretarse con el mismo motor que las de usuario, sin un código de dibujo específico por nombre de plantilla. La salida de `qr`, `barcode`, `todo`, `label` y `sticker` con sus valores por defecto SHALL ser idéntica a la anterior a este cambio.

#### Scenario: Regresión de una plantilla existente
- **WHEN** se renderiza la plantilla `label` con título "Cajón 3" y sin opciones nuevas
- **THEN** el raster resultante es idéntico píxel a píxel al generado antes de este cambio

### Requirement: Plantillas de usuario
El sistema SHALL cargar las plantillas de usuario desde `%ProgramData%\MiniPrinter\templates\*.json`, de modo que las vean el servicio, la bandeja y la CLI. Una plantilla de usuario con el nombre de una integrada SHALL sustituirla, y al borrarla SHALL volver a usarse la integrada.

#### Scenario: Plantilla nueva
- **WHEN** se guarda `recibo.json` en la carpeta de plantillas
- **THEN** `GET /api/templates` y `miniprinter templates` la incluyen sin reiniciar el servicio

#### Scenario: Sustituir una integrada
- **WHEN** existe un `label.json` de usuario
- **THEN** la plantilla `label` usa el JSON del usuario

#### Scenario: Restaurar la integrada
- **WHEN** se borra el `label.json` de usuario
- **THEN** la plantilla `label` vuelve a ser la integrada

### Requirement: Validación y límites
El sistema SHALL validar cada plantilla al cargarla y al recibirla por la API: JSON bien formado, tipos de bloque conocidos, marcadores declarados y propiedades dentro de rango. Una plantilla SHALL tener como máximo 64 KB de JSON y 100 bloques. Una plantilla inválida SHALL omitirse del catálogo registrando el motivo, sin impedir cargar las demás.

#### Scenario: Tipo de bloque desconocido
- **WHEN** una plantilla contiene un bloque `type` = "video"
- **THEN** se rechaza indicando el bloque y los tipos disponibles

#### Scenario: Demasiados bloques
- **WHEN** una plantilla tiene 101 bloques
- **THEN** se rechaza con un error que indica el máximo

#### Scenario: Una plantilla rota no afecta a las demás
- **WHEN** la carpeta contiene un JSON mal formado y otro válido
- **THEN** el catálogo incluye la válida y registra el error de la otra

### Requirement: Valores automáticos
Los textos SHALL admitir `{{now}}` y `{{now:formato}}` (formato de fecha y hora de .NET) con el reloj del equipo, y `{{counter}}` y `{{counter:nombre}}` con un contador persistente que se incrementa solo al imprimir.

#### Scenario: Fecha con formato
- **WHEN** un bloque usa `{{now:dd/MM/yyyy}}` el 2 de octubre de 2026
- **THEN** se imprime "02/10/2026"

#### Scenario: Numeración consecutiva
- **WHEN** se imprime dos veces una plantilla con `{{counter}}`
- **THEN** la segunda impresión lleva el número siguiente al de la primera, también tras reiniciar el servicio

#### Scenario: La vista previa no consume números
- **WHEN** se previsualiza varias veces una plantilla con `{{counter}}`
- **THEN** el contador no avanza y la vista previa muestra el número que se imprimiría
