# Loop 109 Boot — PositionSafety Net (SL+MaxHold Bug Fix)

Tarih: 2026-05-05 15:06 UTC | Bot port 5188

## Loop 108 → 109 Geçiş

Loop 108 t210 KRİTİK BUG: ETH Long pos
- Hold: 79min (MaxHoldMinutes=60 geçti)
- Mark: $2376.89 < SL: $2395.68 (BE armed sonrası)
- **Long pos mark < SL hit OLMASI gerekirken AÇIK kalmış**
- MaxHold timeout da çalışmadı

26 loop boyunca aslında bu bug pos'lar daha uzun açık kalmasına neden olmuş olabilir → büyük SL hit'lerin asıl sebebi.

## Backend-dev Fix `c96878f` (361/361 test)

`PositionSafetyOptions` config + MarkToMarketWorker safety net:
1. **SL hit redundancy**: Long `mark <= StopPrice` / Short `mark >= StopPrice` → close
2. **TP hit redundancy**: Long `mark >= TakeProfit` / Short `mark <= TakeProfit` → close
3. **Hard max-hold**: now - OpenedAt >= **120dk** → close (MaxHoldDuration null bile olsa)

Asıl primary monitor bug analizi yapılmadı (SL hit semantik veya signal freshness window 5dk hipotezi). Safety net 2. hat olarak ETH gibi pozisyonları kurtarır.

`appsettings.json`:
```json
"PositionSafety": {
  "Enabled": true,
  "StopLossRedundancyEnabled": true,
  "TakeProfitRedundancyEnabled": true,
  "HardMaxHoldMinutes": 120
}
```

## Korunur (Loop 95-108 fix'leri)
- Status=3 (Active) ✓
- WeightOverrides 7 Short=0 (Long-only)
- BeMoveTriggerPct 0.001 + OffsetPct 0.001 (DB)
- **TpRiskRewardRatio 1.0** (DB UPDATE — seeder 2.0'a döndürmüştü, fix.sql ile geri 1.0)
- RPT 0.01, MaxOpen 3, RS=1, MTF 0.002
- PullbackLimit Enabled false (Loop 108)

## Boot State
- Bot ayakta, port 5188 (yeni binary, PositionSafety aktif)
- Wallet $500, 0 pos
- ResetCount 26, force-closed 1 (ETH 109min hold), deleted 9 + 22 orders + 231 events
- CB Healthy, Strategies Active=3 ✓
- TpRiskRewardRatio=1.0 ✓ (R:R 1:1 simetri)

## Hipotez

Safety net ile:
- BE armed pos'lar SL semantik bug yaşasa bile redundancy ile kapanır
- 120dk hard cap herhangi bir pos'u zorla close eder
- ETH benzeri sonsuz açık kalan pos'lar olmaz

Loop 108'in 1 winner pattern'i (BTC +%0.15 gross) korunur, AMA ADA -$0.626 büyük SL hit gibi sonsuz açık pos asla olmaz.

## Cumulative

28 loop -$26.5+, 1 winning trade var (BTC +$0.05 Loop 108). Loop 109 = SL semantik safety net.

## Sonraki

ScheduleWakeup t30.
