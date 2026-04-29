# Loop 53 — Halt @ t=120dk (2026-04-29 04:07 TR) — 2h 0 EMIT

## Halt Sebebi
2 saat boyunca BB MeanRev gevşek (BBstd 1.8, RSI 45, volZ 0.3) **0 SignalEmitted, 640 SignalSkipped**. Loop 49'da 2h içinde sinyal gelmişti — bu rejim farklı.

| Loop | BBstd | RSI | volZ | Süre | Emit | Skip |
|---|---|---|---|---|---|---|
| 49 | 2.0 | 38 | 0.5 | 2h | 1+ | — |
| 53 | 1.8 | 45 | 0.3 | 2h | **0** | 640 |

Daha gevşek parametre ile daha az sinyal — anomali. Olası: piyasa "sıkışmış band" rejimi (BB lower'a temas yok).

## Loop 54 — Volume Filter OFF + Maksimum Gevşeme

| Parametre | L53 | **L54** |
|---|---|---|
| `BbStdMultiplier` | 1.8 | **1.5** (band çok dar) |
| `RsiOversoldThreshold` | 45 | **55** (orta) |
| `VolumeZScoreThreshold` | 0.3 | **0.0** (filter off — `>0.0` her pozitif vol kabul) |

Diğer aynı: TpAtr 1.8×, SlAtr 0.9×, MaxHold 120dk, Cool 3 bar, MinAtr 0.0005

Beklenti: Bu konfige hala 0 emit ise **strateji konseptinde başka sorun var** (snapshot null, log kontrolü gerek).

## Sıradaki: Loop 54 Boot
1. appsettings.json patch (5 BB MeanRev)
2. dotnet kill + DB reset + reseed
3. API restart
4. Loop 54 boot rapor
5. ScheduleWakeup t30

— PM 2026-04-29 Loop 53 halt @ t=120
