# Loop 56 — Halt @ t=60dk (2026-04-29 08:59 TR) — WR %20 KRİTİK

## Halt Sebebi
EmaScalper1m yeni param (RSI 35-70, TpAtr 1.3) **5 trade %20 WR** (sadece Loop 54 ETH'in WIN, Loop 56'da 4/4 SL). Realized -$0.612, Loop 54 kar +$0.355 silindi + ek $0.97 zarar.

| Trade Sırası | Coin | Sonuç |
|---|---|---|
| 1 (Loop 54) | ETH | +$0.355 ✓ |
| 2 (Loop 56) | SOL | -$0.453 SL |
| 3 (Loop 56) | ? | -$0.~ SL (büyük ihtimal) |
| 4 (Loop 56) | ? | -$0.~ SL |
| 5 (Loop 56) | ? | -$0.~ SL |
| **Total** | — | **-$0.612 realized + 1 open -$0.072 unrealized** |

WR %20 (1/5), kural eşiği %30 → halt tetiklenir.

## Loop 41-56 Aggregate
| Loop | Trade | Realized |
|---|---|---|
| 41-43 | 11 | -$2.97 |
| 44-45 | 2 | +$0.011 |
| 46-48 | 12 | -$1.69 |
| 49 | 7 | -$0.576 |
| 50-53 | 0 | $0 |
| 54-55 | 1 | +$0.355 ✓ |
| **56 (t60)** | **4** | **-$0.97** |
| **Total** | **37** | **-$5.85** |

EmaScalper1m **3 farklı parametre setinde de başarısız**:
- Loop 46 (RSI 40-65, TpAtr 1.5) → -$1.56 halt
- Loop 47-48 (sıkı/orta) → 0 emit halt
- Loop 56 (RSI 35-70, TpAtr 1.3) → WR %20 halt

EmaScalper1m **stratejik olarak bu rejimde işlemiyor**.

## Karar
binance-expert tetiklendi. DB reset YAPILACAK (kar zaten silindi, sıfırdan başla). Loop 57 binance-expert kararına göre boot.

— PM 2026-04-29 Loop 56 halt @ t=60
