# Loop 94 Boot — Peak Tracking + Wallet Semantic + Margin Wiring + MaxOpen=5

Tarih: 2026-05-03 10:56 UTC | Bot port 5188

## Loop 93 → 94 Geçiş

Loop 93 t60 halt: 3 yeni bug (ExtremeMarkPrice=0 peak tracking, Wallet Spot semantik, MaxOpen=3 darboğaz). Loop 94 backend-dev 3 commit:

### Commit 1 — `9b85cfa` ExtremeMarkPrice Peak Tracking BE-Bağımsız (KRİTİK)

**Root cause**: Peak tracking `BreakEvenAppliedAt is null` gate'i arkasında dormant kalıyordu. BE eşiğine ulaşamayan pozisyonlarda extreme hiç güncellenmiyor — trailing/BE hiç tetiklenmiyor.

**Fix**: Position.UpdatePeakAndCheckTrailing — peak refresh artık BE'den bağımsız her tick (Long max, Short sentinel-min). Trailing exit kararı yine `beArmed` gate sonrası (TR önemli).

### Commit 2 — `362136a` Futures Cash Semantic + AllocateMargin Wiring

- FuturesPaperFillSimulator: `RealizedCashDelta = -fee` only (signedNotional kaldırıldı). Wallet sadece commission ile değişir.
- OrderFilledPositionHandler: open path `paperBalance.AllocateMarginForPosition(notional)`, same-side AddFill marginDelta + AllocateMargin, close path `paperBalance.ReturnMarginAndApplyPnl(originalMargin, gross)`.
- Net hesap: `wallet -= fees + gross = startBalance + RealizedPnl` ✓

### Commit 3 — `457d3a3` MaxOpenPositions 3 → 5

- appsettings.json `MaxOpenPositions: 5` (RiskProfileSeeder boot'ta reconcile)

## Test/Build

- 341/341 test pass (335 + 6 yeni)
- Build 0 hata 0 uyarı

## Boot State

- Bot port: 5188 ayakta
- VirtualBalance: Wallet=$500, AllocatedMargin=$0, UnrealizedPnl=$0, Equity=$500
- CB: Healthy, ConsecutiveLosses=0
- Open positions: 0 (force-closed 3 + deleted)
- SystemEvents reset: 81 silindi
- ResetCount: 8
- MaxOpenPositions: 5 (yeni)
- 5 coin × 17 detector (10 Long + 7 Short)

## KPI / Halt Eşikleri

- Halt: realizedPnl < -$1.50
- 0 emit > 1h → pivot
- Frekans hedefi: saatte 30+ trade

## Beklenti (t30)

- 5 pozisyon olabilir (max açıldı 5'e)
- ExtremeMarkPrice ilk tick'te mark price'a güncellenir (Long max, Short min)
- Wallet açılışta sadece -$0.05/pos commission ($499.75 toplam 5 pos sonrası)
- AllocatedMargin = Σ notional (default leverage 1x)
- Trailing/BE pozisyon kazanca geçtiğinde tetiklenebilir

## Sonraki

ScheduleWakeup t30 → DB sayım + check-t30.md.

## Git

- Implementation: `9b85cfa..457d3a3`
