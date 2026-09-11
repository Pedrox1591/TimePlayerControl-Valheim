# Changelog — TimePlayerControl

Los cambios más recientes primero.  
Cliente y servidor deben usar la **misma versión**: el paquete `TimePlayerControl_TimeSync` cambió de layout.

---

## 1.0.3

**Fixes:** el HUD y el top ya no se congelan en pausa colectiva; el umbral «Pausa a N/M» usa el % del JSON (no el ratio actual); el bloque diario se renueva también si el jugador sigue dentro al cruzar `ResetHourUtc`; tras un join fallido (p. ej. password) el HUD vuelve a sincronizar; marco `woodpanel_info` y textos alineados (sin 9-slice ni bleed).
**Features:** `TotalPlayedSeconds` sigue sumando en pausa; `DailyRules` para bloque y % de pausa por día; el HUD muestra conectados/registrados y aviso de pausa.

## 1.0.2

Trabajo intermedio (parte no compilaba: llamadas a `SendPlayerTimeSyncToPeer` con 3 args y método de 2).

- Se quitó el `return` temprano de `ProcessTimeTick` en pausa.
- Se añadió `BroadcastTimeSyncToReadyPeers` y `_lastPauseActive`.
- El `bool pauseActive` en el paquete y `ClientTimeInfo.PauseActive` se cerraron en **1.0.3**.

## 1.0.1

- Límite por SteamID, JSON en `BepInEx/config/TimePlayerControl.json`.
- HUD IMGUI compacto/detalle (`HudToggleKey`, default F1), top 5, `Language` ES/EN.
- RPC `TimePlayerControl_TimeSync` y `TimePlayerControl_ForceMenu`.
- Pausa colectiva: `online / registered >= PauseTimeMinOnlineRatio` (default 0.75) → no descontar. Sin heartbeat: HUD congelado (1.0.3).
- `registeredCount = DataStore.Players.Count` (todos los del JSON, no solo online).
