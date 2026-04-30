# Loop 65 — Halt @ t=120dk (2026-04-30 16:47 TR) — 2h 0 emit

## Halt Sebebi
2h boyunca 0 emit. BBstd 1.8 + RSI 40 + EMA200 trend filter hala blokeliyor.

## Loop 66 Pivot

| Parametre | L65 | **L66** |
|---|---|---|
| `BbStdMultiplier` | 1.8 | **1.5** (band çok dar) |
| `RsiOversoldThreshold` | 40 | **45** (orta-üst RSI) |
| `VolumeZScoreThreshold` | 0.1 | **0.0** (filter off) |
| `MinAtrPct` | 0.0003 | **0.0002** |
| EMA200 trend filter | KORU ✓ | **KORU ✓** (anti-disaster) |

## Sıradaki: Loop 66 Boot
1. DB UPDATE strategy params
2. dotnet kill + DB reset + restart
3. Loop 66 boot rapor + ScheduleWakeup t30

— PM 2026-04-30 Loop 65 halt @ t=120
