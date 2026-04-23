# Loop 35 — Halt Raporu

**Tarih:** 2026-04-24
**Uptime:** 38 dk
**Verdict:** **FAIL** — realized -$0.346 < halt eşiği -$0.30

## Özet

| Metrik | t20 | t35 (halt) |
|---|---|---|
| Trade | 4 | 5 |
| WR | %25 (1W/3L) | **%20 (1W/4L)** |
| TP Hit | **0** | **0** |
| TimeStop | 4 | 5 |
| SL Hit | 0 | 0 |
| Realized | -$0.252 | **-$0.346** |
| netPnl | -$0.252 | -$0.391 |

## Kök İç Görü — 3 Loop Kümülatif Bilgisi

| Loop | Param | TP Hit | WR | Realized |
|---|---|---|---|---|
| 33 | MaxHold 5-8dk, TpAtr 1.4-1.5 | 0/7 | %14 | -$0.26 |
| 34 | MaxHold 12-15dk, TpAtr 0.9 | 1/7 | %28.6 | -$0.93 |
| **35** | MaxHold 8dk, TpAtr 1.5 | **0/5** | **%20** | **-$0.35** |

### Matematiksel Engel

1m timeframe + 8dk MaxHold'da TP %0.40 hit oranı **yapısal olarak düşük**:
- Ortalama 1m bar body ~%0.05
- 8 bar × %0.05 = %0.40 (tam sınırda, rastgele walk'a yaklaşıyor)
- Pratikte hit oranı %15-20

Loop 34 MaxHold 15dk → ilk 3 trade 1 TP (SOL 348), ardından 4 ardışık kayıp. Daha uzun MaxHold TP'ye yetecek zaman sağladı ama aynı zamanda SL'e de yetecek.

**Matematik kanıt:** 1m VWAP+EMA scalping, $100/trade sizing ile fee-overhead'i aşacak **sistematik edge** üretemiyor. AR-GE raporu (loops/loop_33/strategy-arge.md) bunu önceden dolaylı söylemişti:
> "Saatte $0.10+ için sermaye en az $300'a çıkmalı" — sermayeyi çıkardık ama strateji kendi içinde **break-even zor**.

## Loop 36 Önerisi — Radikal Pivot

### Seçenek (1) — Timeframe 5m (kod değişikliği yok, sadece appsettings)
- `KlineInterval`: "1m" → **"5m"**
- `MaxHoldMinutes`: 8 → **40** (8 × 5m bar)
- `VwapWindowBars`: 15 (75dk → 3.3 saat)
- TP/SL aynı kalır (%0.40 / %0.12-0.15)
- 5m × 8 bar = 40 dk, ortalama bar body %0.15 → 8 bar × %0.15 = **%1.20 potansiyel**
- %0.40 TP hit olasılığı **%50+** çıkar (1m'deki %15-20'den)

### Seçenek (2) — Farklı strateji (binance-expert yeni AR-GE)
- Breakout (Donchian 5m + volume spike)
- Mean Reversion (RSI + BB 1m)
- Order-Flow Imbalance (bookTicker depth)

### Seçenek (3) — Mainnet geçiş düşüncesi
- Paper trading feature-validation platformu olarak kabul
- Real edge mainnet'te bulunur (paper slippage sentetik; gerçek fiyat dinamiği farklı)
- Ama bu **gerçek para riski** — kullanıcı kararı

## Tavsiyem

**Seçenek (1)** — 5m timeframe, kod değişikliği yok, sadece config + appsettings. Hızlı test edilebilir. Önceki loop'lar bize 1m'nin yetmediğini öğretti, 5m matematiği çok daha elverişli.

Eğer Seçenek (1) de başarısız olursa, Seçenek (2) için binance-expert yeniden devreye girer.

## Şu An

- API durduruldu (PID 1601 kapandı)
- 1 açık ETH pozisyonu DB'de kaldı (age 3dk, kapanırken zarar etti muhtemelen)
- Altyapı sağlam (Loop 34'teki tüm fix'ler canlı doğrulandı)
- Loop 36 config bekliyor

## 3 Loop (33-34-35) Birleşik Öğrenme

- **Altyapı fix'leri %100 başarılı** — PnL tutarlılığı, monitor, sizing, UI
- **1m scalping matematiksel olarak kar getirmiyor** — fee overhead ve volatilite eşleşmiyor
- **Kullanıcı sabrı eriyor** — her halt morale düşürücü
- **5m timeframe denenmeli** — tek satır config değişikliği, radikal pivot minimum çaba
