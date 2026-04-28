# Loop 49 — Check t=360dk (2026-04-28 19:09 TR) — TOPARLAMA TAM GAZ ✓

## 3 Açık Pozisyon HEPSİ POZİTİF (+$0.954 unrealized)

| Metrik | t300 | t360 | Δ |
|---|---|---|---|
| Cash | $198.88 | $204.86 | +$5.98 (yeni signal yok ama equity yukarı) |
| OpenPositionsValue | $300.07 | $300.80 | +$0.73 (mark recovery) |
| Equity | $498.95 | **$505.66** | **+$6.71** ✓ İLK POZİTİF NET |
| Realized | -$1.044 | -$1.044 | 0 (yeni kapanış yok) |
| Unrealized | +$0.215 | **+$0.954** | **+$0.739** ✓ büyük rally |
| Net | -$0.829 | **-$0.091** | +$0.738 (BE'ye çok yakın!) |
| Komisyon | $0.825 | $0.825 | 0 |
| Open Pos | 3 | 3 | 0 |
| Signals | 7 | 7 | 0 (yeni signal yok) |
| WsStateChanged | 51 | 51 | 0 ✓ |

## 3 Açık Pozisyon Detay

| Coin | Entry | Mark | Hold | MaxHold | Unrealized | TP Mesafe |
|---|---|---|---|---|---|---|
| XRP | $1.3718 | $1.3756 (+%0.28) | 68dk | 120dk → 52dk kaldı | **+$0.278** | %35 |
| BTC | $75,838 | $76,080 (+%0.32) | 99dk | 120dk → 21dk kaldı | **+$0.320** | %63 ✓ |
| SOL | $83.23 | $83.53 (+%0.36) | 99dk | 120dk → 21dk kaldı | **+$0.356** | %50 |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$1.044 | ✓ buffer $0.46 |
| 5+ ardışık SL | 3SL+1WIN+1SL (zincir kırıldı) | ✓ |
| WR < %20 (8+ trade) | %25 (4 trade) | ⏳ |
| Zombie | en uzun 99dk (MaxHold 120dk) | ✓ |
| WS / CB | 51 stabil | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK + EQUITY POZİTİFE DÖNDÜ.**

## Senaryo Hesabı (3 açık)

**Best (3 TP):** Tüm 3 pozisyon TP'ye ulaşır → ortalama +$0.50 each = **+$1.50 toplam** - $0.45 fee = **+$1.05** → realized -$1.044 + $1.05 = **+$0.01 net (tam BE!)**

**Mark anki (TimeStop):** unrealized +$0.954 - $0.45 fee = **+$0.50 net** → realized -$1.044 + $0.50 = **-$0.54 net** (halt güvenli mesafede)

**Worst (3 SL):** Tüm 3 reverse, hepsi SL → **-$1.50** → realized -$1.044 - $1.50 = **-$2.54 → HALT**

Mevcut momentum (3 pozitif unrealized) → worst senaryo olasılığı düşük.

## Yorum
ABD piyasa açılışı (UTC 13:30 = TR 16:30) sonrası BTC/SOL/XRP rally — BB MeanRev gevşek **doğru zamanda doğru sinyal verdi**. Bu binance-expert'in beklediği "kalite öncelikli, gerçek bounce yakalama" davranışı.

Eğer 3 pozisyon TimeStop +$0.50 net ile kapanırsa Loop 49 **24h içinde -$0.55 ile bitebilir** (binance-expert "kötü senaryo" -$2.10 beklentisinin çok altında).

## Karar
**Loop 49 DEVAM** — toparlama momentum'u tutuyor, halt eşiği uzak.

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=420dk (20:09 TR)**

t420'de:
- 3 pozisyon kapanmış (BTC+SOL TimeStop ~17:30 UTC, XRP ~17:01 UTC)
- İlk gerçek kapanış sonucu sonrası kümülatif net
- Yeni sinyal var mı

— PM 2026-04-28 Loop 49 t=360
