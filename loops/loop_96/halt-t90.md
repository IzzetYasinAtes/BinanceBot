# Loop 96 Halt — t90 BE Eşiği YÜKSEK Bug

Tarih: 2026-05-04 21:55 UTC | Boot: 20:19 UTC | Süre: 96dk

## Halt Sebebi: Realized -$1.30 (eşik -$1.50, marj $0.20 KRİTİK), 2 SL hit imminent

### t30 → t60 → t90 Trajectory
| Metrik | t30 | t60 | t90 |
|---|---|---|---|
| Realized | -$0.62 | -$0.62 | **-$1.30** ($0.68 düştü) |
| netPnL | -$1.37 | -$1.15 | **-$2.90** ($1.75 düştü) |
| Açık | 4 | 5 | 4 |
| Closed | 1 | 1 | 2 |
| BTC UPnL | +$0.10 | +$0.24 | +$0.06 (KAR KAYBOLDU) |

### KÖK SEBEP: BE Eşiği %0.30 ÇOK YÜKSEK

Loop 95'te tune ettim: TriggerPct 0.002 → 0.003 ("BE geç arm, daha çok profit").

AMA Loop 96 trajectory'si şunu gösterdi:
- BTC Long Peak/Entry-1 = +0.28% maksimum (mark $79013 vs entry $78792)
- BE eşik %0.30 → BTC %0.28'de durdu, BE NEVER ARMED
- Mark $78976 → $78836 düşünce kar geri gitti (+$0.24 → +$0.06)
- Trailing locked profit YOKTU çünkü BE arm olmadı → trailing aktif değil

Loop 91'de TriggerPct 0.002 idi → Loop 91 BTC peak %0.20+ olunca BE arm olup trailing %0.5 ile profit %0.18 lock ediyordu (Memory: BE-stop spec MATEMATIKSEL doğru).

Loop 95 hatası: %0.30 eşiği ulaşılamayan teorik tepe. Loop 96 fix: TriggerPct geri **0.002** (Loop 91 değeri).

### Closed Trade'ler (2 SL hit, %100 loser)
| # | Symbol | Direction | Entry | Exit | RPnL |
|---|---|---|---|---|---|
| 1 | ETHUSDT | Long | $2339.84 | $2327.75 | -$0.620 |
| 2 | ADAUSDT | Long | $0.2509 | $0.2495 | -$0.678 |

**Net realized**: -$1.298  
**Win rate**: 0/2 = 0% (Loop 94'te %50, Loop 95'te 0/0)

### Open Pos t90 (4 — bot durduruldu, kayıp realize edilmedi)
| Symbol | UPnL | SL Yakınlık |
|---|---|---|
| BTCUSDT | +$0.057 | uzak (entry +%0.05) |
| XRPUSDT | -$0.290 | yaklaşıyor |
| SOLUSDT | -$0.639 | **çok yakın** (mark $84.04, SL $84.18) |
| ETHUSDT | -$0.433 | **çok yakın** (mark $2320.10, SL $2319.68) |

Bot durdurulmasaydı SOL+ETH SL hit eder → realized -$2.50+ → eşik aşılır.

### Frekans (Loop 96)
- 10 emit / 96dk = ~6.3/h cumulative
- Loop 95: 3/h → Loop 96: 6.3/h (MTF fix yardım etti AMA hedef 30+'tan uzak)
- RequiredScore=3 hala sıkı olabilir — Loop 97'de RS=2 dene

## Loop 97 Tune (PM doğrudan, kod yok)

1. **TriggerPct 0.003 → 0.002** (BE arm olabilsin) — `appsettings.json:56` Edit yapıldı
2. **RequiredScore 3 → 2** (frekans artırma) — DB UPDATE 5 strateji yapıldı

Test/Build gerekmiyor (config + DB only).

## Carryover

Bot kapatıldı. Loop 97 boot bağımsız restart.

## 17 Loop Cumulative

- Loop 80-91: -$17.04 (Spot long-only sıkı param)
- Loop 92: -$117 (commission bug, gerçek -$0.65)
- Loop 93-94: -$1.16 realized (Short bias toxic)
- Loop 95: -$0 realized (frekans donması, MTF yön hatası)
- Loop 96: **-$1.30 realized** (BE eşiği yüksek, kar kayboldu)
- **Total**: -$19.50 / 17 loop / 0 pozitif loop

Loop 97 hipotezi: BE eşiği geri Loop 91 değeri (%0.20) → BTC peak +%0.28 BE arm → trailing locked profit. Frekans RS=2 ile artar.
