# Loop 106 Boot — UI Fix + R:R 1:1 Devam

Tarih: 2026-05-05 07:38 UTC | Bot port 5188

## Loop 105 → 106 Geçiş (Kullanıcı talebi)

Loop 105 R:R 1:1 simetri test devam ediyordu (1 BTC açık +$0.0015 breakeven), 2 close BE-stop küçük loss. Kullanıcı 2 UI sorunu fark etti, "bu loop'u durdur, Loop 106'ya geç" dedi.

## Fix'ler (commit `d1f356c`)

### Fix #1: UI KOMİSYON Kolonu BUG
- Kapalı İşlemler tab'ında KOMİSYON $0.0000 görünüyordu (gerçek: $0.05/leg = $0.10/trade)
- **Kök sebep**: PositionDto'da `EntryCommission`, `ExitCommission`, `TotalCommission` field'ları eksik. PositionMapper 14-field eski sözleşme.
- **Fix**: DTO + Mapper + frontend `commissionTotal()` helper backward-compatible

### Fix #2: Header SymbolCarousel
- Loop 105 ticker fix doğruydu (BTC/ETH/XRP/SOL/ADA)
- AMA SymbolCarousel (header altı kart grid) BNB+DOGE+vs gösteriyordu
- **Fix**: SymbolCarousel da TICKER_SYMBOLS whitelist'ine indirildi

## Korunan (Loop 95-105)
- Status=3 ✓
- WeightOverrides 7 Short=0 (Long-only)
- TpRiskRewardRatio 1.0 (R:R 1:1 simetri Loop 105'te ayarlandı)
- BeMoveTriggerPct 0.001 + BeMoveOffsetPct 0.001
- RPT 0.01, MaxOpen 3, RS=1
- MTF 0.002, TrailPct 0.003

## Boot State
- Bot ayakta, port 5188 (yeni binary)
- Wallet $500, 0 pos
- ResetCount 22, deleted 5 pos + 10 orders + 90 events
- CB Healthy, Strategies Active=3 ✓

## Bilinen Sürdürülemezlik (Loop 107 backlog)

Loop 105 verisi: 4 trade hepsi gross +$0.05 (=%0.05 of $100 notional), AMA 2× fee $0.10 → net -$0.05.

**Asıl strateji sorun**: Pazar volatility peak +%0.10 max, fee %0.10/trade. Net pozitif için gross > %0.10 gerek. Mevcut TP %0.40 (R:R 1:1) hit oranı düşük → MaxHold timeout +%0.05'te kapanıyor.

Loop 107 düşünce: Pazar canlanma bekle (Pazartesi normal volatility) veya MaxHold uzatma (60→180dk).

## Hipotez Loop 106
UI bug fix doğrulandı (commission/header), strateji parametreleri Loop 105 ile aynı. Pazar canlanırsa R:R 1:1 simetri TP %0.40 hit edebilir.

## Cumulative
26 loop -$24.5+, 0 pozitif loop.

## Sonraki
ScheduleWakeup t30.
