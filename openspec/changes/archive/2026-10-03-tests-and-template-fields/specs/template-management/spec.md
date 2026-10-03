## ADDED Requirements

### Requirement: Consultar los campos de una plantilla
El servicio SHALL ofrecer `GET /api/templates/{nombre}/fields` (y `GET /api/v1/templates/{nombre}/fields`) que devuelve los campos de esa plantilla (nombre, etiqueta, tipo, obligatorio, opciones y valor por defecto) y los parámetros de impresión que acepta (`copies`, `rows` y `darkness`, con su descripción y límites). Si la plantilla no existe SHALL responder 404.

#### Scenario: Campos de una plantilla integrada
- **WHEN** se pide `GET /api/v1/templates/label/fields` con el token
- **THEN** la respuesta lista `title` (obligatorio), `line1`, `line2`, `size` (valor por defecto 26), `align` (opciones) y `border`

#### Scenario: Parámetros de impresión
- **WHEN** se consultan los campos de cualquier plantilla
- **THEN** la respuesta incluye los parámetros `copies` (1–50), `rows` (hasta 200 filas) y `darkness` (1–5)

#### Scenario: Plantilla de usuario
- **WHEN** se consultan los campos de una plantilla creada por el usuario
- **THEN** la respuesta refleja los campos de su JSON, con sus tipos y valores por defecto

#### Scenario: Plantilla inexistente
- **WHEN** se pide `/templates/no-existe/fields`
- **THEN** se responde 404 con un mensaje que indica que la plantilla no existe

#### Scenario: Sin token
- **WHEN** se pide la ruta en `/api/v1` sin token válido
- **THEN** se responde 401 y no se revela nada de la plantilla

### Requirement: Campos de una plantilla desde la CLI
`miniprinter template fields <nombre>` SHALL mostrar los campos de la plantilla y los parámetros de impresión sin necesidad de impresora ni de que el servicio esté en marcha.

#### Scenario: Consulta local
- **WHEN** se ejecuta `miniprinter template fields receipt` con el servicio parado
- **THEN** se listan sus campos, marcando los obligatorios, y los parámetros `copies`, `rows` y `darkness`

#### Scenario: Plantilla desconocida
- **WHEN** se pide una plantilla que no existe
- **THEN** la orden termina con un error que lista las plantillas disponibles
