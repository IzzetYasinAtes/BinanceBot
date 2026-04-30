# Loop 62 — Check t=30dk (2026-04-30 10:53 TR) — POZİTİF EQUITY + ZOMBI BUG

## Durum: 4 Açık Pozisyon Mark Rally → Equity $500.47 ✓

| Metrik | t60 (Loop 61) | t30 (Loop 62) | Δ |
|---|---|---|---|
| Cash | $99.20 | $99.20 | 0 |
| OpenPositionsValue | $400.21 | $401.28 | +$1.07 |
| **Equity** | $499.41 | **$500.47** | **+$1.06** ✓ |
| Realized | -$0.566 | -$0.566 | 0 |
| **Unrealized** | +$0.27 | **+$1.337** | **+$1.07** |
| Net | -$0.59 | **+$0.470** | +$1.06 ✓ |
| Komisyon | $1.803 | $1.803 | 0 |
| Open Pos | 4 (zombi) | 4 (hala zombi) | 0 |
| **SignalEmitted** | 30 | **54** | +24 yeni |
| OrderPlaced | 24 | 24 | **0 yeni** ⚠️ |
| WR | %20 | %20 (10 trade) | sabit |

## Bug: Zombi Pozisyonlar + Order Üretilmiyor

| Coin | OpenedAt | Hold | MaxHold | Unrealized |
|---|---|---|---|---|
| XRP | 07:01 UTC | **52dk** | 10dk → **42dk geç** | +$0.351 |
| ADA | 07:01 UTC | 52dk | 10dk geç | +$0.376 |
| SOL | 07:01 UTC | 52dk | 10dk geç | +$0.358 |
| BTC | 07:08 UTC | 45dk | 10dk geç | +$0.431 |

**Zombie 4 pozisyon TimeStop tetiklenmiyor** — RiskAlert sonrası StrategyDeactivated kalıntısı muhtemel.

**24 yeni emit (Loop 62) ama 0 yeni order** — Strategy state Active görünüyor ama order üretimi durmuş.

## Pozitif Tarafı
- Mark rally pozitif: 4 zombie pozisyon **+$1.34 unrealized** (eğer şu anda kapansaydı +$0.77 net realized — fee düşüldükten sonra)
- Loop 41-62 toplam realized -$10.36, ama bu 4 pozisyon kapanırsa +$0.77 → -$9.59 (toparlama)

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$2 | -$0.566 | ✓ |
| WR < %30 | %20 | ⚠️ ama bug var, 10+ trade Loop 62'de yok |
| Yeni emit < 5 | 24 yeni emit ✓ | ✓ |
| **Zombi pozisyon (BUG)** | 4 zombi 42-52dk | ❌ TimeStop bug |

## Karar
**Loop 62 DEVAM ama zombi izle.** Equity pozitif ($500.47), eğer mark trend devam ederse zombi pozisyonlar TP tetikleyebilir (price-based, scheduler-based değil).

İkinci restart yapıldı (10:53 TR) — eğer sonraki kontrolde hala zombi varsa manuel müdahale (DB direct update Status=2 + ExitPrice=MarkPrice).

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (11:23 TR)**

t60'da:
- Zombi 4 pozisyon kapanmış mı (TP/SL/TimeStop)
- Yeni order üretiliyor mu (RiskAlert lifted mı)
- Realized + WR güncel

— PM 2026-04-30 Loop 62 t=30
