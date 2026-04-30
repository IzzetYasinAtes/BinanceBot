# Loop 64 — Halt @ t=120dk (2026-04-30 14:39 TR) — 2h 0 emit

## Halt Sebebi
2h boyunca 0 emit. EMA200 trend filter 5 coin'in hepsini downtrend skip ediyor + BBstd 2.0 sıkı.

## Loop 65 Pivot

| Parametre | L64 | **L65** |
|---|---|---|
| `BbStdMultiplier` | 2.0 | **1.8** (band daha dar) |
| `RsiOversoldThreshold` | 35 | **40** (geniş) |
| `VolumeZScoreThreshold` | 0.3 | **0.1** (neredeyse off) |
| `MinAtrPct` | 0.0005 | **0.0003** |
| EMA200 trend filter | KORU ✓ | **KORU ✓** (anti-disaster) |
| Diğer | aynı | aynı |

5 coin (BTC/ETH/XRP/SOL/ADA), MaxOpenPos=3, MaxConsecLosses=3 korunur.

## Sıradaki: Loop 65 Boot
1. appsettings patch (5 BB MeanRev)
2. dotnet kill+DB reset+restart
3. Loop 65 boot rapor + ScheduleWakeup t30

— PM 2026-04-30 Loop 64 halt @ t=120
