# Loop 105 Boot — R:R 1:1 Simetri (ADR-0026 Option C)

Tarih: 2026-05-05 06:30 UTC | Bot port 5188

## Loop 104 → 105 Geçiş

Loop 104 t90 halt: realized -$1.91 (eşik AŞILDI). 25 loop net -$24.5, 0 pozitif loop.

**Stratejik İçgörü**: 25 loop boyunca aynı pattern — bar close anlık Long emit → bar zirvede yakalanma → pos açıldıktan sonra mark düşüyor → SL hit -$0.6 (R:R 1:15 fiili).

**ADR-0026 (architect+binance-expert paralel danışmanlık)**: 3 opsiyon — A (Pullback Limit), B (Next Bar Confirm), C (R:R 1:1 simetri). **Karar**: C → A → B sırası. Loop 105 = en küçük değişiklik (parametrik, 0 kod).

## Tune

- DB UPDATE 5 strateji `TpRiskRewardRatio 2.0 → 1.0` (R:R 1:1 simetri) ✓
- TP simetri: SL × 1.0 = TP (eskisi SL × 2.0). 
  - Yatay pazarda peak +0.10% civarı → SL ~%0.40, TP ~%0.40 (eskiden %0.80'di, asla ulaşamadı)
  - Win rate aynı, win büyüklüğü daha sık tetikli

## Korunur (Loop 95-104 fix'leri)
- Status=3 (Active) ✓
- WeightOverrides 7 Short=0 (Long-only)
- BE TriggerPct 0.001 + OffsetPct 0.001 (DB)
- RPT 0.01 (pos sizing)
- MaxOpen 3
- RS=1
- MTF threshold 0.002 (denge)
- TrailPct 0.003

## Boot State
- Bot ayakta, port 5188
- Wallet $500, 0 pos
- ResetCount 21, deleted 6 pos + 10 orders + 127 events
- Counter=0, CB=Healthy, Strategies Active=3 ✓

## Hipotez (ADR-0026 §C)

R:R 1:1 ile:
- TP %0.40 (eski %0.80 ulaşılamıyordu)
- Win frekansı arttar (TP daha yakın)
- BE-stop sermaye koruma kalıcı
- Cumulative expectancy: %50 win × $0.10 - %50 loss × $0.40 = -$0.15 (hala negatif AMA Loop 104'tekinden iyi)

Eğer bu çalışmazsa Loop 106-107 = Option A Pullback Limit (4 commit, architectural).

## Cumulative

25 loop -$24.5, 0 pozitif loop. Loop 105 = R:R simetri test (ADR-0026 §C ilk fase).

## Sonraki

ScheduleWakeup t30.
