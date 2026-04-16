---
name: binance-trading-strategy-review
description: Kullanıcının önerdiği trading fikrini/stratejisini red flag açısından tarar — likidite, spread, fee, slipaj, risk-per-trade, max-drawdown, funding fee (futures), backtesting bias. binance-expert agent bu checklist üstünden geçer, olumlu/olumsuz rapor verir.
---

# binance-trading-strategy-review

Yeni bir trading fikri gelince kod yazmadan **önce** bu skill'i çalıştır. Hatalı varsayımları en başta yakala.

## Checklist (her soruya cevap iste)

### 1. Varlık & Piyasa
- [ ] Hangi sembol(ler)? (BTCUSDT, ETHUSDT, ...)
- [ ] Spot mu futures mı? (futures'da funding fee var, leverage riski var)
- [ ] 24h hacim — ortalama nedir? Düşük hacimli varlıklarda emirler price'ı hareket ettirir.

### 2. Stratejinin Core'u
- [ ] Entry sinyali nedir? (SMA cross, RSI, breakout, vb.)
- [ ] Exit sinyali? (take-profit seviyesi, trailing stop, time-based)
- [ ] Stop-loss var mı? Max acceptable loss per trade?
- [ ] Position sizing nasıl? (fixed fractional, Kelly, fixed USD)

### 3. Mikro-yapı Red Flag'leri
- [ ] **Spread:** bid-ask spread % olarak; yüksek spread → her entry/exit maliyet.
- [ ] **Slipaj:** limit vs market order; market order'da likidite yoksa fena kaydırır.
- [ ] **Fee:** Binance spot taker 0.10%, maker 0.10% (VIP/BNB ile düşer). Futures farklı.
- [ ] **MIN_NOTIONAL / LOT_SIZE:** strateji çok küçük pozisyon açıyorsa filter'a takılır.
- [ ] **Round-trip cost:** entry + exit + spread + slipaj → net edge > round-trip mi?

### 4. Backtest Sahiciliği
- [ ] **Look-ahead bias:** `close[t]` ile karar verip `open[t]`'de girilmiş mi? (hayır olsa bile check)
- [ ] **Survivorship bias:** delisted coinler excluded mi?
- [ ] **Overfitting:** parametre aşırı optimize mi? Walk-forward test var mı?
- [ ] **Data quality:** kline 1m vs tick data; 1m'da flaş crash görünmeyebilir.

### 5. Operasyonel
- [ ] WS reconnect sırasında sinyal kaçırırsa ne olur?
- [ ] Açık pozisyon varken connection drop → nasıl ele alınır?
- [ ] Partial fill durumu (LIMIT order'da)
- [ ] Testnet'te test edildi mi? (`testnet.binance.vision`)
- [ ] Paper trade mi gerçek para mı başlangıç?

### 6. Risk Yönetimi
- [ ] Max drawdown kabul edilebilir mi? (tarihsel backtest'te en kötü gün)
- [ ] Black swan senaryoda ne olur? (flash crash, exchange outage)
- [ ] Circuit breaker var mı? (N kez ardışık kayıpta dur)

## Çıktı Formatı

```
🔍 Strateji Review — <strategy name>

✅ Güçlü yanlar:
  - ...

⚠️ Red flag'ler:
  - [<hafif|orta|ciddi>] <konu>: <detay>
  
🚫 Blocker (çözülmeden ilerleyemez):
  - <konu>: <neden>

📋 Sorular (kullanıcıdan cevap bekleyen):
  - ...
```

## Kural

- Cevaplar muğlaksa kullanıcıya **sor**, varsayma.
- Ciddi red flag varsa "ilerlemeden önce çöz" de — PM bunu blocker olarak kayıt eder.
- Her mikro-yapı yorumunda Binance resmi doc'undan filtre değerlerini doğrula (binance-research).

## Kaynak

- https://binance-docs.github.io/apidocs/spot/en/#filters
- https://www.binance.com/en/fee/schedule
- https://academy.binance.com/en/articles (general crypto edu)
