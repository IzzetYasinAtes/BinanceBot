# Loop 107 Boot — Pullback Limit Order (ADR-0026 §A)

Tarih: 2026-05-05 10:19 UTC | Bot port 5188

## Loop 106 → 107 Architectural Pivot

Loop 106 t120 halt: realized -$0.92 + UPnL -$0.72 = netPnl -$1.87 (eşik aşılma yakın). 26 loop boyunca parametrik tune yetmedi (-$26.5+ cumulative).

**Kök sorun**: Bar close anında market emit → bar zirvede yakalanma → mark düşüş → SL hit -$0.6.

**Çözüm (ADR-0026 §A)**: Limit Order @ bar_close × (1 - %0.10) offset, 5dk timeout.

## Backend-Dev 4 Commit (354/354 test)

| Commit | Hash | Konu |
|---|---|---|
| 1 | `b8820e4` | Domain: Order.PlaceLimit factory + LimitPrice/ExpiresAt + 6 test |
| 2 | `41121b2` | Migration: AddLimitPriceAndExpiresAtToOrder + filtered index |
| 3 | `6b7da3a` | Pullback emit handler + PendingLimitTimeoutWorker (30sn cycle) + 7 test |
| 4 | `077fa84` | appsettings PullbackLimit + 5 strateji ParametersJson DML migration |

## Davranış

- **PatternComposite emit**: bar_close anlamlı sinyalde:
  - Long: `limitPrice = ask × (1 - 0.001)` floor tick aligned, GTC, ExpiresAt=now+5dk
  - Short: `limitPrice = bid × (1 + 0.001)` ceil tick aligned (Loop 95+ Long-only ama kod hazır)
- **Paper sim**: Limit + ExpiresAt set → `Status=New` pending tut. Mark price limit'i geçince fill@limit price.
- **Live testnet**: GTC limit order, mark çekildiğinde Binance match
- **PendingLimitTimeoutWorker** 30sn cycle:
  - Status=New + Type=Limit + ExpiresAt < now → Order.Expire() + IExchangeClient.CancelLiveOrderAsync
  - Status=New + Type=Limit + Mode=Paper + (ask≤limit Long / bid≥limit Short) → fill@limit price

## Boot State

- Bot ayakta, port 5188
- Wallet $500, 0 pos
- ResetCount 24, force-closed 3, deleted 5 pos + 11 orders + 165 events
- CB=Healthy, Strategies Active=3 ✓
- DB ParametersJson Strategy 901: `RS=1, TpRiskRewardRatio=2.0` (R:R 1:2 geri — pullback ile uyumlu), `PullbackOffsetPct=0.001`, `PullbackTimeoutMinutes=5`, BE 0.001 ✓, AdxMul 1.0 ✓, Cooldown=1 ✓, WO Long-only ✓

**NOT**: TpRiskRewardRatio 2.0 (Loop 105'teki 1.0 → backend-dev migration commit 4'te seed restore). Pullback entry sonrası peak entry üstüne çıkma ihtimali artar → R:R 1:2 mantıklı.

## Hipotez

Pullback Limit Order ile:
- Pos açılışı bar zirvede DEĞİL geri çekilme noktasında (-%0.10 offset)
- Peak entry üstüne çıkma ihtimali yüksek (mark düştü, sonra geri yükseldi)
- BE arm peak +0.10% yakın olabilir
- TP %0.40 (R:R 1:2) hit olasılığı yükselir
- 26 loop pattern'i kırılması beklenir

**İlk pozitif loop hedefi (26 loop sonra)**.

## Cumulative

26 loop -$26.5+, 0 pozitif loop. Loop 107 = ADR-0026 §A architectural pivot.

## Sonraki

ScheduleWakeup t30 — pullback fill verify, ilk pos açılışı.
