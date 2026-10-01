# MiniPrinter

Convierte la mini impresora térmica Bluetooth **X5h-E07A** (y otras de la familia "cat printer" con protocolo `tiny`) en una **impresora normal de Windows**: aparece en *Impresoras y escáneres* y cualquier aplicación puede imprimir en ella desde el diálogo estándar. Opcionalmente, también desde otros equipos y móviles de la red local.

```
 Aplicaciones (Bloc de notas, Edge, Word, Fotos…)
            │  diálogo de impresión
            ▼
 Spooler + Microsoft IPP Class Driver  (incluido en Windows, sin drivers de terceros)
            │  IPP · PWG Raster
            ▼
 Servicio MiniPrinter ── raster 384 px + tramado ── protocolo tiny (51 78 …) ── Bluetooth RFCOMM ──▶ X5h
            ▲
            │  API local (solo 127.0.0.1)
 App de bandeja: buscar, emparejar, conectar, estado, cola, ajustes
```

## Requisitos

- Windows 10 (2004+) o Windows 11 con Bluetooth.
- La impresora emparejada en Windows (la app de bandeja puede emparejarla).
- Para compilar: .NET SDK 8 o superior.

## Instalación

En una consola **de administrador**, desde la raíz del repositorio:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

El script publica la aplicación, la copia a `%ProgramFiles%\MiniPrinter`, registra e inicia el servicio `MiniPrinter`, crea la cola **«X5h Thermal Printer»** con el *Microsoft IPP Class Driver* (`http://127.0.0.1:8631/ipp/print`) y arranca la app de bandeja, que también se inicia con cada sesión.

La primera vez, la bandeja abre el asistente: **Buscar → Emparejar → Usar esta impresora → página de prueba**.

Desinstalar (también como administrador):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1            # todo
powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1 -KeepConfig  # conserva ajustes
```

## Uso del panel (icono de la bandeja)

| Pestaña | Qué hace |
|---|---|
| **Estado** | Conexión, alarmas (sin papel, tapa abierta…), firmware, URL de impresión, cola de trabajos con cancelación, página de prueba y avance de papel. |
| **Buscar impresoras** | Lista dispositivos Bluetooth emparejados y cercanos, reconoce el modelo por su nombre (`X5h-…` → perfil `d1`), empareja y selecciona la impresora. |
| **Ajustes** | Oscuridad (1–5), tramado (automático, Atkinson, Floyd–Steinberg, umbral), avance final, desconexión por inactividad, nombre en Windows, alcance de red y puerto IPP. |

El servicio se conecta a la impresora al imprimir y se desconecta tras 60 s sin uso, para que la app del móvil pueda volver a usarla.

### Imprimir desde la red local

En **Ajustes → Imprimir desde → Toda la red local**, el servicio escucha en todas las interfaces, crea una regla de firewall solo para redes **privadas** y anuncia la impresora por mDNS (`_ipp._tcp`). Otros PCs con Windows, iPhone/iPad (Imprimir) y Android (servicio de impresión predeterminado) la encuentran solos. Solo se comparte la impresión: el panel y la API de control nunca salen de `127.0.0.1`.

### Papel

Los tamaños de **48 mm** de ancho (50/100/210/297 mm de largo; 48×210 por defecto) imprimen a escala 1:1: es el área imprimible de un rollo de 58 mm a 203 dpi. Si una aplicación usa márgenes grandes pensados para A4 (el Bloc de notas, unos 20 mm por lado), redúcelos en su *Configurar página*, o elige uno de los tamaños virtuales de **80 mm**: el servicio recorta los laterales en blanco y ajusta el contenido al cabezal sin agrandarlo nunca. Las filas en blanco al principio y al final se recortan, así que un documento corto no gasta un folio entero de papel.

Botón **Vista previa del último trabajo** (pestaña Estado): muestra exactamente lo que se envió a la impresora.

## Herramienta de diagnóstico

`miniprinter.exe` (en la carpeta de instalación, o `dotnet run --project src/MiniPrinter.Cli --`):

```powershell
miniprinter probe      --rfcomm 7A:E0:0C:1D:87:AE        # estado, firmware, id
miniprinter test-print --port COM5                        # página de calibración
miniprinter print      --mac 7A:E0:0C:1D:87:AE foto.jpg   # imprime un archivo (PNG, JPEG, PWG)
miniprinter stripes    --rfcomm 7A:E0:0C:1D:87:AE --rows 1200 --log   # trabajo largo + control de flujo
miniprinter find-port  7A:E0:0C:1D:87:AE                  # qué COM corresponde a la impresora
```

## Solución de problemas

| Síntoma | Causa probable |
|---|---|
| «La impresora está en uso por otra aplicación» | TiMini-Print, Tiny Print u otra app tiene la conexión Bluetooth. Ciérrala o desconéctala. |
| «No respondió» / trabajos «Esperando impresora» | Impresora apagada o fuera de alcance. Los trabajos esperan hasta 10 min (configurable) y se imprimen solos al volver. |
| Estado «Requiere atención: sin papel» | El mapa de bits de alarmas está **pendiente de confirmar** en la X5h. Si la impresora tiene papel, comunícalo. |
| Windows no añade la cola | Comprueba que el servicio está iniciado (`services.msc`) y que `http://127.0.0.1:8631/` abre en el navegador. |
| La bandeja dice «Servicio no disponible» | El servicio no está instalado o está detenido. Registro: *Visor de eventos → Aplicación → MiniPrinter*. |

Los ajustes y el token de la API se guardan en `%ProgramData%\MiniPrinter`.

## Desarrollo

```powershell
dotnet build MiniPrinter.slnx
dotnet test  MiniPrinter.slnx
```

Para ejecutar el servicio en consola sin instalarlo: `$env:MINIPRINTER_DATA="$PWD\.data"; dotnet run --project src/MiniPrinter.Service` (la bandeja respeta la misma variable).

| Proyecto | Contenido |
|---|---|
| `MiniPrinter.Protocol` | Tramas `51 78 … CRC8 FF`, comandos, filas RLE/raw, receta de trabajo `d1`, decodificador de respuestas, catálogo de modelos. |
| `MiniPrinter.Transport` | RFCOMM (WinRT), puerto COM, conexión con control de flujo, sesión con reconexión y desconexión por inactividad. |
| `MiniPrinter.Imaging` | Lector PWG Raster, JPEG/PNG, escalado, recorte de blancos y tramado. |
| `MiniPrinter.Ipp` | Codec IPP (RFC 8010) e impresora IPP Everywhere mínima. |
| `MiniPrinter.Service` | Servicio de Windows: cola, endpoint IPP con modo local/LAN (mDNS + firewall), API de control. |
| `MiniPrinter.Tray` | App WPF de bandeja. |
| `MiniPrinter.Cli` | Diagnóstico. |

Los tests de protocolo comparan byte a byte con trabajos de referencia generados por TiMini-Print (`tools/generate_timini_fixtures.py`).

## Créditos

- Receta del protocolo `tiny` y catálogo de modelos: [TiMini-Print](https://github.com/Dejniel/TiMini-Print) (Apache-2.0). Ver `NOTICE`.
- Notas de ingeniería inversa del GT01: [lisp3r/bluetooth-thermal-printer](https://github.com/lisp3r/bluetooth-thermal-printer).
- [ImageSharp](https://github.com/SixLabors/ImageSharp) (Six Labors Split License).

Licencia: MIT (ver `LICENSE`).
