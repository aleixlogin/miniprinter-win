# Ideas: impresión directa (sin el driver de Windows)

Ideas para imprimir en la X5h sin pasar por el spooler ni el driver IPP de Windows, enviando los trabajos directamente al servicio MiniPrinter. Pendientes de convertir en un change de OpenSpec (`/opsx:propose`).

Contexto: hoy ya van directas al servicio la bandeja (portapapeles, nota rápida, soltar archivos, Enviar a, plantillas), la API de automatización (`/api/v1/print/...`) y la CLI (`miniprinter print`, con conexión Bluetooth propia).

```
Con driver:  Aplicación → spooler de Windows → driver IPP → servicio → impresora
Directo:     cliente (TPV, script, nc…) → puerto 9100 / API ───────→ servicio → impresora
```

## 1. Emulación ESC/POS en el puerto 9100

El servicio escucha en el puerto TCP **9100** (estándar RAW/JetDirect) e interpreta **ESC/POS**, el lenguaje de casi todas las impresoras de tiques, traduciéndolo al protocolo `tiny` de la X5h.

- **Comandos a soportar** (subconjunto práctico):
  - Inicialización `ESC @`, texto con saltos de línea, juego de caracteres (CP437/CP858/UTF-8).
  - Estilos: negrita `ESC E`, subrayado `ESC -`, tamaño doble `GS !`, inverso `GS B`, alineación `ESC a`.
  - Avance `ESC d` / `ESC J`, corte `GS V` (como fin de trabajo: avance final).
  - Imagen raster `GS v 0` e imagen de bits `ESC *`.
  - Códigos QR `GS ( k` y de barras `GS k` (Code 128, EAN-13…), generados con el renderizador de plantillas.
  - Respuestas de estado `DLE EOT` / `GS r` a partir de `A3` (papel, tapa) para los programas que las consultan.
- **Maquetación**: el texto se renderiza con una fuente monoespaciada de 32/42 columnas en 384 px (como una impresora de 58 mm), por lo que los tiques de TPV salen con su formato esperado.
- **Compatibilidad que se gana**: software de TPV, `python-escpos`, `node-thermal-printer`, apps de Android como RawBT, integraciones ESC/POS de Home Assistant, cualquier programa que imprima en "una impresora de tiques en red".
- **Alcance de red**: igual que IPP (solo este PC o red local); ajuste propio para activarlo, desactivado por defecto; regla de firewall solo en redes privadas.
- **Fin de trabajo**: por corte (`GS V`), cierre de la conexión TCP o un tiempo sin datos (p. ej. 2 s).

## 2. Impresión "en bruto" por el puerto 9100

En el mismo puerto, si lo recibido **no es ESC/POS** sino un archivo, se imprime tal cual detectando el formato por su cabecera:

- PNG, JPEG, PDF (`%PDF-`), PWG Raster (`RaS2`) → pipeline normal de documentos.
- Texto plano (UTF-8 sin bytes de control ESC/GS) → renderizado de texto.

Ejemplos: `nc equipo 9100 < foto.png`, `cat nota.txt | nc equipo 9100`, PowerShell con `TcpClient`. Permite imprimir desde Linux, macOS o scripts sin token ni HTTP (por eso conviene limitarlo a "solo este PC" o red privada y mostrar en la bandeja quién ha impreso).

## 5. Modo "raw" para desarrolladores

Entrada que se salta el rasterizado y el tramado para experimentar con el protocolo o integraciones muy específicas:

- **Raster de 1 bit ya preparado**: 384 px de ancho (PBM P4 o bytes empaquetados de 48 bytes por fila); se envuelve con la receta `d1` (cabecera, filas RLE/raw, fin de página).
- **Tramas del protocolo `51 78` ya codificadas**: se validan (cabecera, longitud, CRC-8, `FF`) y se envían tal cual respetando el troceado de 180 B / 4 ms y el control de flujo `AE`. Opción de rechazar o permitir comandos peligrosos (p. ej. escritura de device-id `BB`, OTA).
- **Dónde**: `POST /api/v1/print/raw?format=pbm|frames` (API de automatización, con token) y `miniprinter raw <archivo> --format pbm|frames`.
- **Diagnóstico**: opción `--log` / respuesta con las tramas recibidas de la impresora durante el envío.

## Notas comunes

- Todo entra por la cola del servicio (`JobQueue`), así que respeta el orden, la retención si falta papel, la cancelación y la vista previa del último trabajo.
- Reutiliza `PrintRequests`, `TextRenderer`, `TemplateRenderer` y `PrintJobBuilder`.
- Tests: fixtures de tiques ESC/POS reales (generados con `python-escpos`) comparando el raster resultante; tests de detección de formato en el puerto 9100; tests de validación de tramas en el modo raw.
