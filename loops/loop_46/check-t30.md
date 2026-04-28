# Loop 46 — Check t=30dk (2026-04-28 10:35 TR) — FREKANS ✓ AMA WR %0

## HFS Çalışıyor: 10 Sinyal/30dk = 20/saat ✓

| Metrik | Boot | t30 | Δ |
|---|---|---|---|
| Cash | $500 | $298.66 | -$201.34 (2 pos kilit) |
| OpenPositionsValue | $0 | $200.59 | +$200.59 |
| Equity | $500 | $499.24 | -$0.76 |
| Realized | $0 | **-$0.996** | -$0.996 |
| Unrealized | $0 | +$0.386 | +$0.386 |
| Net | $0 | -$0.610 | -$0.610 |
| Komisyon (toplam) | $0 | $0.750 | +$0.750 (10 entry/exit) |
| Open Pos | 0 | **2** | +2 |
| Closed Pos | 0 | **4** | +4 |
| Orders | 0 | 10 | +10 |
| Signals | 0 | **10** | **+10** ✓ |
| Fills | 0 | 10 | +10 |
| WinRate | — | **%0 (0/4)** | ⚠️ |
| SignalSkipped | 0 | 377 | +377 (eval rate %2.6 sinyal) |

## 4 Closed Pozisyon (HEPSI ZARAR)

| Coin | Entry | Hold | Realized | Tip |
|---|---|---|---|---|
| XRP | 07:04 | 8.1dk | **-$0.249** | TimeStop (mark down) |
| ADA | 07:07 | 3.6dk | **-$0.373** | SL (en hızlı, en kötü) |
| DOGE | 07:07 | 8.1dk | **-$0.311** | TimeStop |
| ETH | 07:27 | 8.1dk | **-$0.063** | TimeStop (en az kötü) |

3 TimeStop + 1 SL. Ortalama -$0.249/trade.

## 2 Open Pozisyon (UMUT VAR)

| Coin | Entry | Mark | Hold | Unrealized |
|---|---|---|---|---|
| ADA (yeni) | 07:28 UTC | — | 7dk | **+$0.254** ✓ |
| BTC | 07:29 UTC | — | 6dk | **+$0.133** ✓ |

İki pozitif açık → kapanışta zincir kırılırsa toparlama olabilir.

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.996 | ⚠️ buffer **$0.50** (ZAYIF) |
| 5+ ardışık SL/TimeStop | **4** ardışık | ⚠️ 1 daha = halt |
| Zombie | 7dk + 6dk (MaxHold 8dk) | ✓ |
| Signal akmıyor | 10 sinyal/30dk | ✓ |
| WS / CB | normal | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK ama 2 RİSK SİNYALİ:**
1. Buffer sadece $0.50 kaldı (hedef $1.50)
2. 4 ardışık losing → 5+ olursa zincir halt

## Umut: 2 Açık Pozitif
- ADA unrealized +$0.254, BTC +$0.133 → toplam +$0.386 (komisyon dahil net +$0.236 olur kapanışta)
- En az biri kâr kapanırsa zincir kırılır + realized -$0.996 + $0.13 = -$0.86 → buffer $0.64'e çıkar

## Karar
**Loop 46 DEVAM** (Signals=10 ≥ 5) ama **t60'ta agresif değerlendirme**:
- Realized<-$1.50 olursa Loop 47 boot
- 5+ ardışık SL/TimeStop olursa Loop 47 boot
- Açık 2 pozisyon kapanır kapanmaz buffer durumu kritik

## Önemli Gözlem (strateji yorumu)
4/4 losing pattern, ortalama -$0.25/trade. 1m EmaScalper'ın Loop 41-43 Donchian gibi false-breakout sorunu yaşıyor olabilir:
- EMA9>EMA21 cross sonrası fiyat hemen reversal
- 1m bar gürültülü, 8dk MaxHold yetersiz olabilir
- TP %0.30-0.80 mesafesi 1m'de zor ulaşılır

binance-expert beklentisi %45 WR idi. 4 trade ölçüm değil ama sinyal kötü. **t60'ta WR < %30 kalırsa Loop 47 (filtre güçlendirme: RSI bandı daralt veya MaxHold 8→15dk).**

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (11:05 TR)**

— PM 2026-04-28 Loop 46 t=30
