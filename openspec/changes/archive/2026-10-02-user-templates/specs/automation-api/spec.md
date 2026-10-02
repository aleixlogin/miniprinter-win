## ADDED Requirements

### Requirement: Gestión de plantillas por la API de automatización
La API de automatización SHALL exponer, con el mismo token y las mismas reglas de activación y alcance de red que el resto de `/api/v1`, `GET /api/v1/templates`, `GET /api/v1/templates/{nombre}`, `PUT /api/v1/templates/{nombre}`, `DELETE /api/v1/templates/{nombre}` y `POST /api/v1/templates/validate`, con la misma semántica que la gestión definida en `template-management`.

#### Scenario: Listar plantillas
- **WHEN** un cliente con el token correcto pide `GET /api/v1/templates`
- **THEN** recibe las plantillas integradas y las de usuario con sus campos

#### Scenario: Sin token
- **WHEN** se pide `PUT /api/v1/templates/recibo` sin el token
- **THEN** se responde 401 y no se guarda nada

#### Scenario: API desactivada
- **WHEN** la API de automatización está desactivada
- **THEN** los endpoints de plantillas responden 404

### Requirement: Límites de las plantillas por API
Las peticiones de plantillas SHALL respetar los límites de tamaño de la API de automatización y los de las plantillas: 64 KB de JSON, 1 MB por imagen, 50 copias y 200 filas por petición.

#### Scenario: Plantilla demasiado grande
- **WHEN** se envía un JSON de plantilla de 100 KB
- **THEN** se rechaza con 413 o 400 indicando el límite
