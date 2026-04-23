# Loop 36 — Halt Raporu

**Uptime:** 55 dk
**Verdict:** FAIL — realized -$0.382 < -$0.30

## Sonuç

| Metrik | t25 | t39 | t55 (halt) |
|---|---|---|---|
| Trade | 3 | 6 | 9 |
| WR | %33 | %50 | **%37.5** |
| TP hit | 0 | 0 | **0** |
| Realized | -$0.27 | -$0.26 | **-$0.38** |

## 4 Loop Kümülatif Kanıt

| Loop | Param | Trade | TP hit | WR | Realized |
|---|---|---|---|---|---|
| 33 | MaxHold 5-8dk, TP %0.40-0.50 | 7 | 0 | %14 | -$0.26 |
| 34 | MaxHold 15dk, TP %0.30 | 7 | **1** | %29 | -$0.93 |
| 35 | MaxHold 8dk, TP %0.40 | 5 | 0 | %20 | -$0.35 |
| 36 | MaxHold 15dk, TP %0.30 | 9 | 0 | %37.5 | -$0.38 |

**Toplam 28 trade / 1 TP hit = %3.6**. 1m VWAP+EMA scalping + 15dk MaxHold + %0.30 TP **sistematik edge ÜRETMİYOR**.

## Kök Sebep Matematik

1m bar ortalama body = **%0.05** (realized volatility)
15 bar × %0.05 = **%0.75 maksimum potansiyel**
Ama yön dağılımı simetrik değil — rastgele walk eşiği %0.30-0.40 hit olasılığı **%20 altında**.

Fee overhead: $100 trade × %0.075 = $0.075/fill × 2 = **$0.15/round-trip**.
Ortalama kazanç (TimeStop-W): +$0.06 net
Ortalama kayıp (TimeStop-L): -$0.10 net
**Fee+kayıp > kazanç** → negatif expectancy sabit.

## Radikal Pivot — Loop 37

**5m timeframe şart.** MarketIndicatorService şu an sadece 1m/1h/30s buffer destekliyor. Kod değişikliği gerek:
1. `IndicatorRollingBuffer.FiveMinute` ekle
2. `SelectBuffer` dispatch (KlineInterval.FiveMinutes)
3. WS stream `kline_5m` subscription
4. BackfillIntervals "5m" ekle
5. Evaluator `KlineInterval` string → enum dispatch (5m route)

### Beklenen 5m matematik
- 5m × 8 bar = 40 dk MaxHold
- 5m bar body ort %0.15
- 8 bar × %0.15 = **%1.20 potansiyel** (1m'de %0.40'tı)
- TP %0.30 hit olasılığı **%50-60** (1m'de %5-10'du)
- Net expectancy **POZİTİF** olur

## Şimdiki Aksiyon

1. API durduruldu (PID 63 kapandı)
2. backend-dev'e 5m indicator entegrasyonu delege
3. Bittiğinde Loop 37 boot (DB reset + API restart + yeni seed 5m params)
4. Halt + "bu sefer matematik lehe" umuduyla 4h gözlem

## Not (Kullanıcıya)

4 iterasyon = 4 saat / 28 trade / 1 TP = matematiksel olarak "1m scalping paper'da kar getirmiyor" kanıtı.
5m timeframe kod değişikliği (30dk backend-dev iş). Sonrası eğer 5m de ulaşılamazsa:
- Seçenek: stratejiyi mainnet'e taşıma (real edge farklı)
- Seçenek: strateji tipi değişimi (mean reversion, breakout)
- Seçenek: paper trading'i kar değil, feature validation amacıyla kabul
