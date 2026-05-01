# Loop 75 — Halt @ t=150dk (2026-05-01 15:20 TR) — Eşik -$2.50 Geçti, Loop 76 PIVOT

## Halt Sebebi: Realized -$2.61 < -$2.50 → Loop 76 binance-expert KESIN

BE feature başarılı (8 BE move + 4 TP) AMA BE'ye varmayan pozisyonlar hala büyük loss üretiyor. Toplam 19 closed:
- TP HIT: 4 (BTC +$0.055, XRP +$0.033, ETH +$0.086, XRP +$0.033)
- BE save (≈breakeven): 2 (ADA -$0.009, BTC +$0.001)
- Küçük TP (ADA): 1 (-$0.009)
- Küçük-orta loss: 7 (-$0.04 ile -$0.16)
- BÜYÜK loss (BE'den önce): 5 (-$0.23 ile -$0.41)
- **TOTAL: -$2.61** (-$2.50 eşik geçti)

## Final Trade Tablosu (19 closed)
| Win/Loss Tipi | Sayı | Toplam |
|---|---|---|
| TP HIT (BE move sonrası) | 4 | +$0.207 |
| BE save (breakeven) | 2 | -$0.008 |
| Küçük loss (BE applied, sonra timestop/stop) | 6 | -$0.547 |
| BE'den önce büyük loss | 5 | -$1.776 |
| Küçük TP (ADA) | 1 | -$0.009 |
| Diğer büyük loss (BE'ye varmadı) | 1 | -$0.480 |

→ **WR: 4/19 = %21**, BE öncesi 5 loss + Loop 75 sonradan 1 loss (-$0.37 ADA) toplam -$2.25 büyük loss yörüngesi.

## Loop 76 PIVOT Plan

### binance-expert overhaul:
1. **Trailing stop**: Peak'ten %X geri çekme (BE'den daha akıllı kar koruma — BE move sonrası BE stop yerine trail)
2. **EMA200 trend gate**: Long sadece `closePrice > EMA200` (trend yukarı zorunlu)
3. **BBW regime filter**: Bollinger Band Width düşük → choppy market → emit sustur (entry kalitesi)
4. Genel yaklaşım: BE öncesi büyük loss'ları önle (entry kalitesini artır), BE sonrası küçük kar'ı maksimize et (trailing)

### backend-dev implement:
- `Position.MoveTrailingStop()` domain method
- `MarkToMarketWorker` trailing logic
- `IMarketIndicatorService.TryGetEma200()` + EMA200 indicator
- `IMarketIndicatorService.TryGetBbw()` + BBW snapshot
- `KmsMomentumEvaluator` EMA200 trend gate + BBW regime filter
- Tests + migration

## Şimdiki Plan
1. binance-expert + backend-dev paralel çağır (background)
2. Bot çalışmaya devam etsin (0 açık, güvenli — yeni emit fill yapar)
3. Realized iyileşme veya kötüleşme izleme
4. backend-dev iş bitince → Loop 76 boot (bot kill + migration + restart)

## Cumulative Yörünge (5 loop)
- L71: **+$0.85** ✓ (tek pozitif loop)
- L72: -$0.54
- L73: -$0.39
- L74: -$0.98
- L75: -$0.69
- **TOTAL: -$1.75** (StartingBalance $500'den ~%0.35 erime)

— PM 2026-05-01 Loop 75 halt @ t=150 (Loop 76 trailing+EMA200+BBW pivot)
