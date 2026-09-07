# TimePlayerControl

Mod de **Valheim** (BepInEx) que limita el tiempo de juego por jugador, sincroniza el tiempo restante entre servidor y cliente, y muestra un HUD configurable.

## Requisitos

- Valheim (cliente y/o servidor dedicado)
- [BepInEx 5](https://docs.bepinex.dev/) instalado en la carpeta del juego o del servidor

## Instalación

Copia estos **2 archivos** a la carpeta `BepInEx/plugins` del **cliente** y del **servidor dedicado**:

| Archivo | Origen típico tras compilar |
|---------|-----------------------------|
| `TimePlayerControl.dll` | `bin/Debug/net462/` o `bin/Release/net462/` |
| `Newtonsoft.Json.dll` | misma carpeta de salida del build |

Rutas de ejemplo:

- Cliente: `...\Valheim\BepInEx\plugins\`
- Servidor: `...\Valheim dedicated server\BepInEx\plugins\`

Reinicia el cliente y el servidor después de copiar los DLL.

## Configuración

Al iniciar, el mod crea (o usa) el archivo:

`BepInEx/config/TimePlayerControl.json`

- **Servidor:** controla tiempos, resets, jugadores y ranking (`TotalPlayedSeconds`).
- **Cliente:** controla sobre todo el layout del HUD (`HudAnchor`, `HudToggleKey`, opacidad, etc.).

### HUD (cliente)

| Campo | Descripción |
|-------|-------------|
| `EnableClientHud` | Activa/desactiva el HUD |
| `HudAnchor` | `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` |
| `HudPositionX` / `HudPositionY` | Márgenes desde el ancla |
| `HudWidth` | Ancho (referencia 1920×1080) |
| `HudHeight` | `0` = altura automática |
| `HudOpacity` | Transparencia del fondo (0–1) |
| `HudAutoHideOverlays` | Oculta el HUD con inventario, mapa, menú, etc. |
| `HudStartCompact` | Empieza en modo compacto |
| `HudShowProgressBar` | Barra de progreso |
| `HudToggleKey` | Tecla para compactar/expandir (ej. `F1`, `F6`) |

### Jugadores (servidor)

Cada entrada en `Players` incluye, entre otros:

- `RemainingSeconds` / `AssignedSeconds` — bloque actual
- `TotalPlayedSeconds` — acumulado histórico (no se resetea cada día)
- `IsExempt` — VIP/admin sin límite
- `NextResetTime` — próxima renovación

## Uso en juego

- **F1** (o la tecla de `HudToggleKey`): alterna HUD compacto / detalle
- Panel detalle: restante, asignado, usado, total jugado, renovación y **top 5** por tiempo jugado
- Chat: comandos como `/tiempo` y `/hud` (según los parches del mod)
- Al agotarse el tiempo: mensaje *«Te has quedado sin tiempo»* y vuelta al menú de inicio

## Compilar (desarrollo)

### Requisitos

- [.NET SDK](https://dotnet.microsoft.com/download) con soporte para `net462` (SDK 6+ suele bastar)
- Conexión a internet la primera vez (NuGet restaura BepInEx, UnityEngine.Modules, Valheim.GameLibs, Newtonsoft.Json)

No hace falta tener Valheim instalado en la misma máquina para compilar: las referencias de juego vienen del paquete NuGet `Valheim.GameLibs`.

### Pasos

```bash
# Desde la raíz del repositorio
dotnet restore
dotnet build -c Release
```

Salida:

```text
bin/Release/net462/TimePlayerControl.dll
bin/Release/net462/Newtonsoft.Json.dll
```

Copia esos 2 DLL a `BepInEx/plugins` (cliente y servidor) como en la sección de instalación.

### Estructura del proyecto

```text
TimePlayerControl/
├── Plugin.cs                 # Entrada BepInEx + Harmony
├── TimeManager.cs            # Lógica de tiempo, RPC y HUD (IMGUI)
├── Data/
│   ├── ServerConfig.cs       # Opciones del JSON
│   ├── PlayerData.cs         # Datos por jugador
│   └── TimeDataStore.cs      # Carga/guardado de TimePlayerControl.json
├── Patches/
│   ├── ChatPatch.cs          # Comandos de chat
│   └── ZNetPatch.cs          # Conexión / kick
├── Deploy-TimePlayerControl.ps1
└── TimePlayerControl.csproj
```

### Notas para modificar el código

- Target: **.NET Framework 4.6.2** (`net462`), compatible con BepInEx 5 de Valheim.
- El HUD es **Unity IMGUI** (`OnGUI`), no Canvas.
- El sync cliente↔servidor usa RPC de Valheim (`TimePlayerControl_TimeSync`, `TimePlayerControl_ForceMenu`).
- Tras cambiar el protocolo RPC, actualiza **cliente y servidor** con el mismo build.

## Licencia

Usa o adapta el código según tus necesidades. Si publicas un fork, conviene indicar la versión de Valheim / BepInEx con la que lo probaste.
