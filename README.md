# TimePlayerControl

Limita el **tiempo de juego por jugador** en servidores de Valheim (BepInEx). Incluye HUD en cliente, ranking top 5 y desconexión limpia al menú cuando se acaba el tiempo.

## Características

- Tiempo diario (o por bloque) por SteamID, configurable en JSON
- Bloques y umbral de pausa **por día de la semana** (`DailyRules`)
- HUD compacto / detalle (tecla `F1` por defecto, configurable)
- Top 5 de mayor tiempo jugado acumulado (sigue contando en pausa colectiva)
- Auto-ocultado del HUD con inventario, mapa y menú
- Mensaje *«Te has quedado sin tiempo»* y vuelta al menú de inicio
- Jugadores VIP/admin exentos (`IsExempt`)

## Instalación

### Con gestor (r2modman / Thunderstore Mod Manager)

1. Instala el pack en el **perfil del cliente** y en el del **servidor dedicado**
2. Asegúrate de que ambos usen la **misma versión** del mod

### Manual

Copia el contenido de `plugins/` a:

`BepInEx/plugins/`

Archivos incluidos:

- `TimePlayerControl.dll`
- `Newtonsoft.Json.dll`

Reinicia cliente y servidor.

## Importante: cliente + servidor

El mod debe estar en **ambos**. El servidor gestiona tiempos y kicks; el cliente muestra el HUD y recibe el sync.

## Configuración

Al iniciar se crea:

`BepInEx/config/TimePlayerControl.json`

- **Servidor:** tiempos, resets, jugadores, `TotalPlayedSeconds`, idioma de mensajes (`Language`)
- **Cliente:** layout del HUD e idioma del HUD (`Language`)

### Idioma

```json
"Language": "ES"
```

- `ES` — español (default)
- `EN` — inglés

El del **cliente** afecta el HUD; el del **servidor** afecta kick/chat/`/tiempo`.

### Tiempo y pausa (servidor)

Los valores globales aplican a todos los días. Con `DailyRules` podés cambiar el bloque y el % de pausa **solo algunos días**. Los días que no listes siguen usando el global.

El día se calcula con la medianoche local de `ResetHourUtc` (con `3` = 00:00 Chile).

```json
"DefaultBlockSeconds": 21600,
"PauseTimeMinOnlineRatio": 0.75,
"DailyRules": {
  "Monday": {
    "DefaultBlockSeconds": 18000,
    "PauseTimeMinOnlineRatio": 0.8
  },
  "Friday": {
    "DefaultBlockSeconds": 28800,
    "PauseTimeMinOnlineRatio": 0.5
  },
  "Saturday": {
    "DefaultBlockSeconds": 28800
  }
}
```

- `DefaultBlockSeconds` — segundos del bloque (21600 = 6 h, 18000 = 5 h)
- `PauseTimeMinOnlineRatio` — fracción online/registrados para pausar el descuento (0.75 = 75 %)
- En un día podés omitir un campo: se usa el global
- Nombres de día: `Monday`…`Sunday` o `Lunes`…`Domingo`

### HUD (cliente)

| Campo | Descripción |
|-------|-------------|
| `Language` | `ES` o `EN` |
| `HudAnchor` | `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` |
| `HudToggleKey` | Tecla para expandir/compactar (ej. `F1`, `F6`) |
| `HudAutoHideOverlays` | Oculta el HUD con UI del juego |
| `HudStartCompact` | Empieza en modo compacto |
| `HudOpacity` | Transparencia (0–1) |
| `HudSnapBelowMinimap` | Coloca el HUD bajo el minimapa |
| `HudUseCustomPosition` | Usa posición arrastrada (Escape) |

### Jugadores (servidor)

- `RemainingSeconds` / `AssignedSeconds` — bloque actual
- `TotalPlayedSeconds` — acumulado (no se resetea cada día; también suma durante la pausa colectiva)
- `IsExempt` — sin límite
- `NextResetTime` — próxima renovación

## Uso en juego

- **F1** (o tu `HudToggleKey`): compacto ↔ detalle
- Panel detalle: restante, asignado, usado, total jugado, renovación y **top 5**
- Chat: `/tiempo`, `/tiempos`, `/hud`

## Código fuente

https://github.com/Pedrox1591/TimePlayerControl-Valheim

## Notas

- Probado con BepInEx 5 (pack Valheim)
- Tras actualizar el mod, actualiza **cliente y servidor**
