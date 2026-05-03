# Loop 95 Boot — Long-only Emit + R:R Tune + Frekans Gevşetme

Tarih: 2026-05-03 19:12 UTC | Bot port 5188

## Loop 94 → 95 Geçiş

Loop 94 mekanik fix'leri (peak/Wallet/AllocateMargin/MaxOpen) doğrulandı. AMA stratejik 3 sorun → Loop 95 parametrik tune (kod değişikliği minimal):

### Tune Detay (2 commit)

**Commit `a671541`** — appsettings + MTF threshold:
- `appsettings.json:61` TrailPct 0.005 → **0.003** (winning trade'e pencere)
- `appsettings.json:56` TriggerPct 0.002 → **0.003** (BE geç arm)
- `PatternCompositeEvaluator.cs:118` MTF threshold 0.001m → **0.0005m** (frekans gevşetme)

**Commit `bbb7a6a`** — WeightOverrides migration `Loop95LongOnlyWeightOverrides`:
- 5 PatternComposite strateji ParametersJson'a `WeightOverrides` eklendi
- 7 Short detector weight = 0 (snake_case key: bearish_engulfing, shooting_star, bollinger_upper_reversal, bollinger_squeeze_break_down, rsi_overbought_pullback, ema9_slope_down, donchian_breakdown)
- **Etki**: Long-only emit (Short signal aslında yok — composer Short bucket score 0 kalır, RequiredScore=5 aşılamaz)

### Test/Build
- 341/341 pass, 0 hata 0 uyarı

## Boot State

- Bot port 5188 ayakta
- VirtualBalance: Wallet=$500, Margin=$0, UPnL=$0, Equity=$500
- Open positions: 0 (force-closed 2 + deleted 6 + 11 orders + 165 SystemEvents silindi)
- ResetCount: 9
- CB: Healthy
- 5 coin × 10 Long detector aktif (7 Short detector ağırlık 0)
- Long-only mode

## KPI / Halt Eşikleri

- Halt: realizedPnl < -$1.50
- 0 emit > 1h → pivot
- Frekans hedef: saatte 30+ trade

## Beklenti (t30)

- Long emit (sadece Long composer çalışır)
- 5 coin'den emit (frekans MTF gevşetme ile artmalı)
- Pozisyonlar Long, BE +%0.30 eşiğine geç arm, trailing %0.3 daha geniş
- Pazar uptrend ise Long pos kazanca dönmeli
- Pazar yatay ise frekans korunabilir (MTF gate gevşek)

## Hipotez Test

Loop 94 → 95 hipotezi: Long-only + R:R tune + frekans gevşetme = 12 loop'ta sürekli zarar pattern'i kırılır mı?

- Loop 80-91: -$17.04 (Spot long-only — sermaye koruma ile sıkı param)
- Loop 92: -$117 (commission bug)
- Loop 93: -$0.65 (gerçek değer küçük zarar, Wallet UI bug)
- Loop 94: -$1.16 realized (Short bias toxic + R:R 1:14)
- **Loop 95**: hedef ≥ -$0.50 realized veya pozitif

## Sonraki

ScheduleWakeup t30 → DB sayım + check-t30.md.

## Git

- Implementation: `a671541..bbb7a6a`
