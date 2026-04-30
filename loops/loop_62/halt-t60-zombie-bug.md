# Loop 62 — Halt @ t=60dk (2026-04-30 11:25 TR) — ZOMBI BUG

## Halt Sebebi
RiskAlert (Loop 61) sonrası bot iç state bozuk:
1. **4 zombi pozisyon** 1h22m açık (MaxHold 10dk olmasına rağmen TimeStop tetiklenmiyor)
2. **70 SignalEmitted ama 24 OrderPlaced sabit** — yeni order üretilmiyor
3. 2 restart yapıldı, fayda etmedi → derin internal state bug

## Final Durum
| Metrik | Değer |
|---|---|
| Cash | $99.20 (4 zombi kilit) |
| Equity | $500.20 (+$0.20 mark trend) |
| Realized | -$0.566 (Loop 61 history) |
| Unrealized | +$1.07 (zombi mark up — gerçekleşemez) |
| WR | %20 (10/2) |
| **RiskAlert** | **1** (DB'de, internal state) |
| **StrategyDeactivated** | **6** events |

## Zombi Pozisyonlar (kapatılamıyor)
| Coin | OpenedAt | Hold | Unrealized |
|---|---|---|---|
| XRP | 07:01 | **1h24m** | +$0.27 |
| ADA | 07:01 | 1h24m | +$0.17 |
| SOL | 07:01 | 1h24m | +$0.30 |
| BTC | 07:08 | 1h17m | +$0.33 |

## Bug Root Cause (PM gözlem)
RiskAlert tetiklendiğinde:
- Strategy.Status DB'de "Deactivated" olur
- PositionMonitor (TimeStop scheduler) bu strateji'lerin pozisyonlarını izlemez
- OrderProcessor yeni emit'lere order üretmez

Restart sonrası:
- Strategy seed appsettings'ten "Active" olarak yüklenir (DB Status="Active" yazılır)
- AMA internal RiskAlert flag/scheduler state restored olmaz → bot yarı-çalışır halde

**Backend-dev için issue:** RiskAlert reset endpoint veya restart'ta internal state cleanup gerekli.

## Karar
**DB reset + Loop 63 boot.** Manuel SQL ile zombi kapatma riskli (cash sync). Tam reset ile temiz başlangıç.

**Kayıp:** -$0.566 realized kabul + +$1.07 unrealized zombi kar hayalı (gerçekleşemez).

Loop 63: aynı param (Loop 62), sadece DB reset → temiz $500.

— PM 2026-04-30 Loop 62 halt @ t=60 (zombie bug)
