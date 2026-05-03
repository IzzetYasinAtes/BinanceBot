# Loop 93 Boot — Futures + Accounting Fix

Tarih: 2026-05-03 09:36 UTC | Bot port 5188 | Endpoint: demo-fapi.binance.com (Futures testnet)

## Loop 92 → 93 Geçiş

Loop 92 t30 halt edildi (32dk koştu, 5 coin emit + 1 Short emit OK, AMA 2 accounting bug). Loop 93'te 2 fix:

1. **Position.EntryCommission BUY multiplier** (commit `2cec99a`): `OrderFilledPositionHandler` ADR-0020 §20.7 Spot semantiği BUY tarafına `Commission × Price` çarpanı uyguluyordu. Futures'ta her iki leg USDT-quote → multiplier yanlış. Fix: `SUM(f.Commission)` only. ETH $117.29 → $0.05.

2. **PortfolioSummary Futures formülü** (commit `065b975`): `GetPortfolioSummaryQuery` Loop 84 ledger formülü Spot kalıntısıydı (cash = start - notional - commission). Futures'ta notional MARGIN'da locked tutulur. Fix: `CurrentCash = WalletBalance` (single source of truth), `TrueEquity = Wallet + Σ UnrealizedPnl`.

Loop 94'e ertelenen: `AllocateMarginForPosition` wiring (FuturesPaperFillSimulator semantic refactor gerekli — tek satır wiring tutarsız state üretir).

## Boot State

- VirtualBalance: Wallet=$500, AllocatedMargin=$0, UnrealizedPnl=$0, Equity=$500
- Open positions: 0 (force-closed 3 + deleted)
- Closed trades: 0
- SystemEvents reset: 47 silindi
- CB: Healthy
- 5 coin × 17 detector (10 Long + 7 Short)
- ResetCount: 7 (papertrade reset history)

## KPI / Halt Eşikleri (aynı)

- Halt: realizedPnl < -$1.50 → Loop 94 spec
- 0 emit > 1h → pivot
- Frekans hedefi: saatte 30+ trade

## Beklenti

- t30/t60'ta cash hesabı doğru görünür (Wallet $500 - sadece komisyon, ~$0.05 × pozisyon sayısı)
- Position.EntryCommission tüm pozisyonlarda ≈ $0.05 (notional × %0.05)
- Pazar yönüne göre Long+Short emit (12 emit/30dk = 24/h Loop 92'de gözlemlendi)

## Sonraki

ScheduleWakeup t30 → DB sayım + check-t30.md.

## Git

- Loop 93 implementation: `2cec99a..065b975`
- Build 0 hata, test 335/335 pass
