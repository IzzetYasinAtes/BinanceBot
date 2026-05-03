# Loop 98 Boot — Pos Başı Risk Yarıya + Short Emit Geri

Tarih: 2026-05-04 23:38 UTC | Bot port 5188

## Loop 97 → 98 Geçiş

Loop 97 t60 halt: realized -$1.998 (eşik AŞILDI). 3 ardışık SL hit (XRP+BTC+ADA Long, 1dk içinde, pazar flash crash). Long-only mod kırılgan.

Pattern: Pos başı risk büyük (RiskPerTradePct=0.02) → SL hit'te ortalama -$0.65 loss → 3 ardışık SL = -$2 → halt.

## Tune (PM Doğrudan)

### Tune #1: RiskPerTradePct 0.02 → 0.01
- `appsettings.json:65` updated ✓
- DB UPDATE: RiskProfiles.RiskPerTradePct = 0.01 ✓
- Pos qty yarıya, SL hit loss yarıya (~-$0.32)
- 3 SL hit toplam ~-$1 (eşik -$1.50 marj $0.50)

### Tune #2: WeightOverrides Revoke (Short emit geri)
- `appsettings.json` ParametersJson template'ten WeightOverrides field silindi (5 strateji) ✓
- DB UPDATE REPLACE ile Strategies.ParametersJson'dan WeightOverrides kaldırıldı ✓
- Composer hard-coded weight: 10 Long + 7 Short detector hepsi aktif → balanced emit
- Pazar yönü her iki tarafa hedge

### Tune #3: MaxOpenPositions 5 → 3
- `appsettings.json:70` updated ✓
- DB UPDATE: MaxOpenPositions = 3 ✓
- Risk concentration düşer (5'ten 3'e)

### Korunur (Loop 95-97 fix'leri)
- TriggerPct 0.002 (BE arm — Loop 97'de 1 winning trade ile doğrulandı)
- TrailPct 0.003 (winning pencere)
- MTF threshold 0.002m (gevşek doğru yön)
- RequiredScore 2 (frekans için)
- OffsetPct 0.002

## Boot State

- Bot ayakta, port 5188
- VirtualBalance: Wallet=$500, Margin=$0, Equity=$500
- Counter=0, CB=Healthy, Strategies Active (5 PatternComposite)
- ResetCount: 13 (force-closed 1 + deleted 5 + 9 orders + 96 events silindi)
- 5 coin × 17 detector aktif (10 Long + 7 Short)
- RiskPerTradePct: 0.01 (yarıya)
- MaxOpenPositions: 3 (5'ten geri)

## KPI / Halt Eşikleri

- Halt: realizedPnl < -$1.50
- 0 emit > 1h → pivot
- Frekans hedef: 30+/h

## Hipotez

Loop 98 hedef:
- Pos başı SL hit ~-$0.32 (yarıya), 3 ardışık SL = -$1 (eşik AŞILMAZ)
- Long+Short balanced emit → pazar yönü her iki tarafa fırsat
- Win amount da yarıya (~$0.025 avg) AMA absolute risk azalır
- Win rate %33+ + R:R 1:7 (yarıya küçük loss + küçük win) = neutral cumulative

İlk pozitif loop hedefi: realized > $0.

## Cumulative

18 loop -$21.5, 0 pozitif loop. Loop 98 = pos sizing testi.

## Sonraki

ScheduleWakeup t30.
