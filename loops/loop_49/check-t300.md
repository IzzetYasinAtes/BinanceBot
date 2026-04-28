# Loop 49 — Check t=300dk (2026-04-28 18:07 TR)

## ABD Pre-Market Sinyal Akışı + Halt Eşiğine Yaklaşma

| Metrik | t240 | t300 | Δ |
|---|---|---|---|
| Cash | $499.45 | $198.88 | -$300.57 (3 pos açıldı) |
| OpenPositionsValue | $0 | $300.07 | +$300.07 |
| Equity | $499.45 | $498.95 | -$0.50 |
| Realized | -$0.547 | **-$1.044** | -$0.497 (XRP SL #4) |
| Unrealized | $0 | +$0.215 | +$0.215 |
| Net | -$0.547 | -$0.829 | -$0.282 |
| Komisyon (toplam) | $0.450 | $0.825 | +$0.375 (4 entry+1 exit) |
| Open Pos | 0 | **3** | +3 |
| Closed Pos | 3 | 4 | +1 (XRP yeni SL) |
| Signals | 3 | **7** | +4 (BTC, SOL, XRP×2 ABD pre-market burst) |
| WinRate | %33 | **%25** (1/4) | -%8 |

## 4 Closed Trade (Tarihsel)

| # | Coin | Hold | Realized | Tip |
|---|---|---|---|---|
| 1 | BTC | 100dk | -$0.489 | SL |
| 2 | XRP | 70dk | -$0.546 | SL |
| 3 | **ETH** | **31dk** | **+$0.488** ✓ | **TP** |
| 4 | XRP | 9dk | -$0.497 | SL (hızlı) |

## 3 Açık Pozisyon

| Coin | Entry | Mark | Hold | Unrealized |
|---|---|---|---|---|
| XRP | $1.3718 | $1.3706 | 6dk | **-$0.094** |
| BTC | $75,838 | $75,893 | 37dk | **+$0.073** ✓ |
| SOL | $83.23 | $83.30 | 37dk | **+$0.092** ✓ |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$1.044 | ⚠️ buffer **$0.46** (kritik) |
| 5+ ardışık SL | 1 win zincir kırdı (3SL+1WIN+1SL) | ✓ |
| WR < %25 (5+ trade) | %25 (4 trade) | ⏳ 5+ trade'de tetiklenebilir |
| Zombie | en uzun 37dk (MaxHold 120dk) | ✓ |
| WS / CB | 51 stabil | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK ama eşiğe çok yakın ($0.46 buffer).**

## 3 Açık Pozisyon Senaryo

**Best (3 TP):** +$0.50 + $0.50 + $0.50 - $0.30 fee = **+$1.20 net** → realized -$1.044 + $1.20 = +$0.16 ✓

**Worst (3 SL):** -$0.30 - $0.30 - $0.30 - $0.30 fee = **-$1.20 net** → realized -$1.044 - $1.20 = **-$2.24 → HALT**

**Mixed (2 TP, 1 SL):** ~+$0.50 → realized -$0.54

**Mevcut unrealized (+$0.215) gösterge:** mark anki seviyede TimeStop olursa ~+$0.06 net → realized -$1.044 + $0.06 = **-$0.98** (halt eşiğine yakın ama altında).

## Karar
**Loop 49 DEVAM** ama t360'ta KRİTİK kontrol — 3 pozisyon kapanır kapanmaz halt durumu netleşecek.

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=360dk (19:07 TR)**

t360'ta:
- 3 açık pozisyon kapanmış (TimeStop ya TP ya SL)
- Halt değerlendirmesi
- Yeni sinyal var mı

— PM 2026-04-28 Loop 49 t=300
