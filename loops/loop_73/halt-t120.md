# Loop 73 — Halt @ t=120dk (2026-05-01 11:25 TR) — Circuit Breaker (TEKRAR)

## Halt Sebebi: 5 Ardışık SL → Circuit Breaker (Loop 72 ile AYNI PATTERN)

Param tune (TP 1.8→1.2 / MaxHold 45→30) **yetmedi**. 5 trade hepsi `order_timestop`. UPnl pozitifteyken bile MaxHold çıkıp loss aldı.

## Trade Sonucu (5 closed)
| # | Symbol | PnL | t90 UPnl | Δ |
|---|---|---|---|---|
| 1 | BTC | -$0.008 | -$0.008 | (zaten kapalı SL hit) |
| 2 | XRP | -$0.097 | **+$0.081** | **-$0.18 ters dönüş** |
| 3 | ADA | -$0.170 | **+$0.050** | **-$0.22 ters dönüş** |
| 4 | SOL | -$0.075 | **+$0.103** | **-$0.18 ters dönüş** |
| 5 | ETH | -$0.044 | **+$0.144** | **-$0.19 ters dönüş** |
| **TOTAL** | | **-$0.394** | | |

**KEŞİF**: Tüm pozisyonlar entry'den +%0.10-0.14 yukarı gitti, **TP %0.3'e ulaşamadı**, sonra geri dönüp timestop oldu. SL %0.2 da hit etmedi (fiyat o kadar düşmedi). "Slow bleed timestop" pattern.

## Cumulative
- L71: **+$0.850** (4 trade, 2 TP HIT — tek başarılı loop)
- L72: -$0.542 (8 trade, hepsi timestop, CB)
- L73: -$0.394 (6 trade, hepsi timestop, CB tekrar)
- **Total: -$0.086 NEGATIF**

## Pattern Analizi
**Loop 71 vs Loop 72/73 fark**:
- L71'de ETH +$0.56 ve BTC +$0.45 TP HIT etti (TP %0.5-1.8 ATR)
- L72/L73'te hiçbir TP hit yok (TP daraldıkça paradoksal sonuç)
- Pazar koşulu değişmiş olabilir (volatilite düşük, yön belirsiz)
- Ya da KMS RSI Zone entry yanlış noktada giriyor

## Loop 74 Plan: binance-expert Algoritma Overhaul

**SORUNLAR:**
1. Pozisyon entry'den hareket ediyor ama TP/SL hit etmiyor — TP geometrisi yanlış
2. MaxHold timestop oluyor — pozisyonlar süre dolduğunda Realized loss
3. Param tune (Loop 72: 1.8 ATR / 45dk → Loop 73: 1.2 ATR / 30dk) yetersiz
4. Loop 71 KAR formülü Loop 72/73'te tekrar edilemedi

**binance-expert'a soracaklar:**
- "Slow-bleed timestop" pattern için çözüm — TP/SL/MaxHold yerine başka bir exit mantığı?
- Trailing TP (yarı yolda kar koruma)?
- Dynamic MaxHold (volatiliteye göre değişen)?
- Entry filter sıkılaştırma — RsiNeutralCeiling 60→50 (Loop 71 patterni geri)?
- Market regime detect — "trending" vs "choppy"?

## Şimdiki Plan
1. Loop 74 binance-expert agent (algoritma overhaul önerisi)
2. backend-dev implement (gerekirse)
3. CB reset (API), Strategies reactivate, restart
4. Loop 74 boot rapor
5. ScheduleWakeup t30

**ŞU AN BOT DEAKTİF** — strategies Status=2, emit gelmiyor (güvenli).

— PM 2026-05-01 Loop 73 halt @ t=120 (CB tekrar tripped)
