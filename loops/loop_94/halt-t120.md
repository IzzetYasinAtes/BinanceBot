# Loop 94 Halt — t120 Stratejik Tune Zamanı

Tarih: 2026-05-03 13:04 UTC | Boot: 10:56 UTC | Süre: 128dk

## Halt Sebebi: Realized -$1.16 (eşik -$1.50 yakın), bot 30dk dondu

Realized eşik henüz aşılmadı (-$1.16 vs -$1.50, marj $0.34) AMA:
- Son 30dk **0 yeni emit, 0 yeni close** — sirkülasyon dondu
- netPnl -$1.48 (UPnL dahil)
- 2 açık pozisyon UPnL kötüleşiyor (-$0.08 → -$0.19)

Loop 94 mekanik fix'leri tamamen başarılı (peak/Wallet/AllocateMargin/MaxOpen) AMA stratejik:
- Short bias toxic (2/2 Short SL hit)
- R:R 1:14 asymmetric exit
- Frekans durdu

## Loop 94 Final Sonuç

### Closed (4 trade, %50 W/L)
| # | Symbol | Direction | RealizedPnl | Yorum |
|---|---|---|---|---|
| 1 | XRPUSDT | Short | -$0.633 | SL hit pazar yukarı |
| 2 | BTCUSDT | Short | -$0.614 | SL hit pazar yukarı |
| 3 | ADAUSDT | Long | +$0.041 | Trailing erken çıkış |
| 4 | ETHUSDT | Long | +$0.048 | Trailing erken çıkış |

**Net realized**: **-$1.158**  
**Avg win**: $0.045 / **avg loss**: $0.624 → **R:R 1:14 (KÖTÜ)**

### Open (2, t120 hala açık)
- ADAUSDT Long, 75min hold, UPnL -$0.05
- ETHUSDT Long, 74min hold, UPnL -$0.14

### Frekans
- t30: 22 emit (44/h)
- t60: 26 emit (+4)
- t90: 26 emit (+0) — DURMA başladı
- t120: 26 emit (+0) — 60dk durmuş

## Tespit (3 Stratejik Sorun)

1. **Short bias toxic** — Pazar uptrend (12 loop boyunca da Long bias mainnet doğrulandı). Short emit pozisyon açtığında systematic SL hit. 7 yeni Short detector aktif olmamalı.

2. **R:R asymmetri** — Trailing-stop %0.50 (TrailPct=0.005) çok dar. BE armed olur olmaz (peak +%0.20 sonrası) trailing kazanan trade'i mark'tan %0.5 altta kapatıyor → küçük profit. Losing trade SL'ye kadar (%0.40-0.60) gidiyor.

3. **Frekans donması** — Son 60dk 0 emit. Composer score eşiği aşılmıyor olabilir, MTF gate ±%0.1 sınırına takılıyor olabilir.

## Loop 95 Spec — Parametrik Tune (kod değişikliği minimal)

`loops/loop_95/spec.md` yazılacak (paralel, bu commit'in ardından).

Backend-dev'e mini delege:
1. `appsettings.json` TrailPct 0.005 → **0.003** (BB:61) — winning trade run almak
2. `appsettings.json` TriggerPct 0.002 → **0.003** (BB:56) — BE arm geç (daha fazla profit pencere)
3. `PatternCompositeEvaluator.cs:118` MTF threshold 0.001m → **0.0005m** (gevşet, frekans donmasını çöz)
4. **DB update**: 5 strateji ParametersJson `WeightOverrides` ile 7 Short detector ağırlığı 0 (Long-only emit)

## Carryover

Bot kapatıldı. Loop 95 boot bağımsız restart.
