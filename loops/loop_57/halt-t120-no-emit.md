# Loop 57 — Halt @ t=120dk (2026-04-29 11:11 TR) — 2h 0 EMIT

## Halt Sebebi
BB MeanRev volume bug fix uygulandı (commit `e17e21d`) ama 2h içinde 0 emit. Loop 49 referansı 2h ilk emit, biz aşırı muhafazakar param (BBstd 1.8 + RSI 45) → fix bypass yetersiz.

| Loop | BBstd | RSI | volZ | Süre | Emit | Skip |
|---|---|---|---|---|---|---|
| 49 | 2.0 | 38 | 0.5 | 2h | 1+ | — |
| 57 | 1.8 | 45 | 0.0 | 2h | **0** | 617 |

Fix çalışıyor (volZ 0.0 bypass aktif), ama BBstd 1.8 + RSI 45 hala band içi yakalama yapamıyor (mevcut piyasa rejimi).

## Loop 58 — Daha Agresif Gevşetme

| Parametre | L57 | **L58** |
|---|---|---|
| `BbStdMultiplier` | 1.8 | **1.5** (band çok dar) |
| `RsiOversoldThreshold` | 45 | **55** (oversold genişlet) |
| `MinAtrPct` | 0.0005 | **0.0003** (sessiz coin daha az dışla) |

Diğer aynı: TpAtr 1.8, SlAtr 0.9, MaxHold 120, Cool 4, volZ 0.0 (zaten kapalı)

## Sıradaki: Loop 58 Boot
1. appsettings patch (5 BB MeanRev)
2. dotnet kill + DB reset + reseed
3. API restart
4. Loop 58 boot rapor
5. ScheduleWakeup t30

— PM 2026-04-29 Loop 57 halt @ t=120
