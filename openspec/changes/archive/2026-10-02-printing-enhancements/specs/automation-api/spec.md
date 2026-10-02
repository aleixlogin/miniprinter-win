## ADDED Requirements

### Requirement: Endpoints de impresión
El sistema SHALL ofrecer una API REST de impresión bajo `/api/v1/print` en el mismo listener que IPP:
- `POST /api/v1/print/text` (JSON con `text` y opcionalmente `fontSize`, `align`, `darkness`),
- `POST /api/v1/print/image` (cuerpo binario PNG/JPEG/PDF o `multipart/form-data`),
- `POST /api/v1/print/qr` (JSON con `data` y opcionalmente `caption`),
- `POST /api/v1/print/template/{nombre}` (JSON con los campos de la plantilla),
- `GET /api/v1/jobs/{id}` (estado del trabajo).
Cada petición de impresión SHALL responder `202 Accepted` con el identificador del trabajo.

#### Scenario: Imprimir texto desde un script
- **WHEN** un script envía `POST /api/v1/print/text` con `{"text":"Hola"}` y un token válido
- **THEN** la respuesta es `202` con `{"jobId":N}` y el trabajo aparece en la cola

#### Scenario: Consultar el trabajo
- **WHEN** se consulta `GET /api/v1/jobs/N` tras imprimir
- **THEN** la respuesta indica el estado (`pending`, `processing`, `completed`, `canceled` o `aborted`) y el mensaje

### Requirement: Autenticación por token propio
La API de automatización SHALL exigir `Authorization: Bearer <token>` con un token distinto del de la API de control, generado por el servicio y visible y regenerable desde la bandeja. Las peticiones sin token o con token incorrecto SHALL recibir `401`.

#### Scenario: Sin token
- **WHEN** se llama a un endpoint de impresión sin cabecera `Authorization`
- **THEN** la respuesta es `401` y no se crea ningún trabajo

#### Scenario: Regenerar token
- **WHEN** el usuario regenera el token en la bandeja
- **THEN** el token anterior deja de ser válido inmediatamente

### Requirement: Activación y alcance de red
La API de automatización SHALL estar desactivada por defecto y activarse desde la bandeja. Con el modo "Solo este PC" SHALL responder solo en `127.0.0.1`; con "Toda la red local", en la red. La API de control SHALL seguir sin exponerse nunca a la red.

#### Scenario: API desactivada
- **WHEN** la API está desactivada
- **THEN** los endpoints de `/api/v1` responden `404`

#### Scenario: Modo red local
- **WHEN** la API está activada y el modo es "Toda la red local"
- **THEN** un equipo de la red con el token puede imprimir, y `/api/settings` y demás endpoints de control siguen siendo inaccesibles desde la red

### Requirement: Límites y validación
El sistema SHALL limitar el cuerpo de las peticiones a 16 MB y el texto a 20 000 caracteres, y SHALL responder `400` con un mensaje claro ante datos inválidos (JSON mal formado, formato de imagen no admitido, plantilla inexistente o campos que faltan).

#### Scenario: Plantilla inexistente
- **WHEN** se llama a `POST /api/v1/print/template/no-existe`
- **THEN** la respuesta es `400` con el mensaje "Plantilla desconocida" y la lista de plantillas disponibles

### Requirement: Documentación de integración
El README SHALL incluir ejemplos de uso con `curl`, PowerShell, un `rest_command` de Home Assistant y un nodo HTTP de n8n.

#### Scenario: Ejemplo de Home Assistant
- **WHEN** el usuario copia el `rest_command` del README, pone su token y lo invoca
- **THEN** la impresora imprime el texto enviado
