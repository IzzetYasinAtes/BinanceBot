# Loop 33 - Strateji AR-GE Raporu

**Yazar:** binance-expert | **Tarih:** 2026-04-22 | **Task:** loop33-strategy-arge

**Kapsam:** MicroScalperVwapEma30s matematiksel sinir analizi + 5 alternatif strateji + sampiyon secimi + feasibility verdict + Loop 33 implementation roadmap

---

## 1. Mevcut Stratejinin Matematiksel Siniri

### Sabit Parametreler (Loop 32 sonucu)

- Sizing: **$5.10 / trade** (max(equity x 0.01, 5.10))
- Fee: VIP0 %0.1 taker + BNB %25 discount = **%0.075 tek yon** -- round-trip **%0.15**
- Round-trip fee / trade: $5.10 x 0.0015 = **$0.00765**
- Testnet slipaj FixedSlippagePct=0.0001: $5.10 x 0.0001 x 2 = **$0.00102** round-trip
- **Toplam round-trip maliyet: ~$0.00867 / trade**

Kaynak fee: https://www.binance.com/en/fee/schedule (VIP0 taker %0.1, BNB discount %25)
Kaynak WS: https://developers.binance.com/docs/binance-spot-api-docs/web-socket-streams

Not: Mainnet XRPUSDT spread tipik 0.0001 USDT tick (~%0.003). $5.10 sizing ile spread etkisi < $0.001 ihmal edilebilir.

### TP/SL Geometrisi -- Coin Bazinda Net Kar ($5.10 sizing, BNB fee aktif)

| Coin | TP% | SL% | Gross Win | Fee | Net Win | Net Loss | Fee/Gross |
|------|-----|-----|-----------|-----|---------|----------|-----------|
| ETH  | 0.30 | 0.15 | $0.0153 | $0.00867 | **+$0.00663** | **-$0.01632** | 57% |
| BNB  | 0.20 | 0.12 | $0.0102 | $0.00867 | **+$0.00153** | **-$0.01479** | 85% |
| BTC  | 0.25 | 0.15 | $0.0128 | $0.00867 | **+$0.00408** | **-$0.01632** | 68% |
| XRP  | 0.40 | 0.20 | $0.0204 | $0.00867 | **+$0.01173** | **-$0.01887** | 43% |

### Break-Even Win Rate Hesabi

Formul: p = NetLoss / (NetWin + NetLoss)

| Coin | Net Win | Net Loss | Break-Even WR | Loop 32 Gercek WR | Durum |
|------|---------|----------|---------------|-------------------|-------|
| ETH  | $0.00663 | $0.01632 | **71.2%** | 47-48% | KARSIZ |
| BNB  | $0.00153 | $0.01479 | **90.8%** | 50-57% | KARSIZ |
| BTC  | $0.00408 | $0.01632 | **79.9%** | 22% (Paused) | PAUSED |
| XRP  | $0.01173 | $0.01887 | **61.8%** | 55% | MARJINAL |

**Kritik Bulgular:**
- BNB break-even %90.8 -- Loop 32 gercek %57 -- 33 puan acik. Matematiksel olarak surdurulemez.
- ETH break-even %71.2 -- Loop 32 gercek %47-48 -- 23 puan acik. Yapisal kayip.
- XRP tek tutunulan coin, o bile marjinal (%61.8 esik, %55 gercek WR).
- Temel problem: fee gross TP nin %43-85 ini yiyor. BNB: gross $0.0102, fee $0.00867 -- fee/gross **%85**.

### Saatlik EV Hesabi (Loop 32 Gozlemlenen Frekans)

Loop 32 ortalama: 18-22 trade/saat (3 aktif coin), yaklasik 7 trade/coin/saat.

XRP en iyi senaryo (%55 WR gozlemlenen, 7 trade/saat):
  EV/saat = 7 x [0.55 x $0.01173 - 0.45 x $0.01887]
          = 7 x [$0.006452 - $0.008492]
          = 7 x (-$0.00204) = **-$0.0143/saat** (NEGATIF EV)

Matematiksel ust sinir (%75 WR, 20 trade/saat, XRP geometrisi):
  EV/saat = 20 x [0.75 x $0.01173 - 0.25 x $0.01887]
          = 20 x [$0.008798 - $0.004718] = 20 x $0.00408 = **$0.082/saat**
  (Bu bile %75 WR gerektiriyor -- Loop 32 max gercek %55)

**Sonuc:** Mevcut strateji $0.10-0.30/saat hedefine matematiksel olarak ulasamiyor. Problem TP/SL orani degil, feenin gross TP yi yutmasi. Her artirilan frekans kaybi buyutuyor.

---

## 2. Alternatif Stratejiler

### Strateji A: Donchian Breakout + Volume Spike (5m timeframe)

**Mantik:** 5 dakikalik barlar uzerinde 20-bar Donchian kanali. Son kapanis > son 20 bar yuksegi VE volume > 20-bar SMA x 2 ise Long sinyal. Trend-following, momentum tabanli.

**Expected Edge Kaynagi:** Donchian breakout trend-following literaturde temel yapi tasi (PyQuantLab 2024, Schwager Market Wizards). 5m bar 1m ye gore %60-70 daha az noise -- daha secici ama daha kaliteli sinyal.

**Fee/Slippage Matematik ($5.10 sizing):**
- TP hedef %0.50 -- Gross Win: $0.0255; Net Win: **$0.0168**
- SL hedef %0.30 -- Net Loss: **$0.0240**
- Break-even WR: **58.8%**
- EV/saat at 60% WR, 4 trade/saat: **$0.0019/saat**

**Red Flag Taramasi:**
- Likidite: Temiz. Overfitting: YUKSEK (walk-forward zorunlu). Sideways: sik sahte kirilim.

**Beklenen EV/saat:** $0.001-$0.005.
Kaynak: https://pyquantlab.medium.com/a-donchian-channel-breakout-strategy-a-simple-trend-following-approach-18b7b74c4358

---

### Strateji B: Order-Flow Imbalance (bookTicker bid/ask ratio)

**Mantik:** Binance bookTicker WS stream gercek zamanli bid/ask qty guncelleme. Bid qty / (Bid qty + Ask qty) > 0.70 AND fiyat 30 saniye yatay ise Long sinyal.

**Expected Edge Kaynagi:** Akademik literaturde (Glosten-Milgrom 1985) order-flow imbalance kisa vadeli fiyat tahmininde prediktif. HFT 1-5ms icerisinde tuketir. Normal API/WS latency 50-200ms -- sinyal zaten islenmis olur.

**Fee/Slippage Matematik ($5.10 sizing):**
- Hedef hareket %0.10-0.15 -- Gross Win: $0.0051 -- Net Win: **-$0.0036** (fee grosu asiyor)
- Hicbir WR bu geometride karli yapamaz

**Red Flag Taramasi:**
- KRITIK Latency: 50-200ms HFT penceresini kapatiyor -- edge tukenmis
- Boyut sorunu: $5.10 sizing ile hedef hareket fee nin altinda -- fiziksel imkansiz

**Verdict: ELEME.** $5.10 sizing + Binance API latency ile matematiksel olarak karsiz.
Kaynak: https://bookmap.com/blog/can-real-time-order-flow-give-you-an-edge-in-scalp-trading

---

### Strateji C: 5m Timeframe VWAP + EMA21 (Gurultu Filtresi)

**Mantik:** Mevcut MicroScalper mantiginin 5 dakikalik bar versiyonu. VWAP 15-bar rolling, EMA21 slope, volume confirm. Her 5 dakikada bir degerlendirme. Loop 32 kodunun minimum degisiklikle evolutionu.

**Expected Edge Kaynagi:** 5m bar 1m ye gore daha az noise. VWAP kurumsal referans noktasi -- buyuk oyuncular buy/sell zone kullanir (Morpher 2025). 5m timeframe algoritmalarin dominant oldugu frekans bandi.

**Fee/Slippage Matematik ($5.10 sizing):**
- TP %0.40; Net Win: **$0.01173** -- SL %0.20; Net Loss: **$0.01887** -- Break-even WR: **61.8%**
- EV/saat at 65% WR, 12 trade/saat (3 coin): **$0.0119/saat**

**Red Flag Taramasi:** Sideways sahte sinyal, parametre tasima gerektiriyor.

**Beklenen EV/saat:** $0.006-$0.015. Mevcut stratejiden 3-5x iyilesme ama $0.10 hedefinin altinda.
Kaynak: https://www.morpher.com/blog/vwap-indicator
Kaynak: https://www.cryptowisser.com/guides/fibonacci-vwap-ema-crypto-scalping/

---

### Strateji D: Yuksek Volatiliteli Altcoin Rotasyonu (SOL + ADA)

**Mantik:** ETH/BNB yerine daha yuksek ATR li semboller: SOL (saatlik ATR tipik %0.3-0.8), ADA (saatlik %0.3-0.6). Ayni VWAP+EMA mantigi ancak ATR-tabanli dinamik TP/SL. Buyuk gross TP fee etkisini kokluca dusuruyor.

**Expected Edge Kaynagi:** Yuksek volatilite buyuk gross TP saglar. Fee/gross: SOL %24 (BNB %85 idi). Break-even WR %49.5 -- ulasabilir. ATR-tabanli TP piyasanin volatilite rejimine adaptif.

**Fee/Slippage Matematik ($5.10 sizing -- SOL, ATR 1m %0.70):**
- TP %0.70 -- Gross: $0.0357; Net Win: **$0.0270**
- SL %0.35 (2:1 RR) -- Net Loss: **$0.0265**
- Break-even WR: **49.5%**
- EV/saat at 53% WR (8 trade/saat): **$0.0148/saat**
- EV/saat at 58% WR (8 trade/saat): **$0.036/saat**

ADA ornegi (TP %0.60): Net Win $0.02193, Net Loss $0.02397, Break-even WR: **52.2%**

**Red Flag Taramasi:**
- Likidite: SOL, ADA Binance de gunluk $500M+ -- temiz
- Spread: Yuksek volatilite biraz genis. SOL/ADA 1-2 tick -- $5.10 sizing de ihmal edilebilir
- DOGE MIN_NOTIONAL KIRMIZI BAYRAK: ~$0.15 fiyat, LOT_SIZE round-down sonrasi notional Binance min altina dusebilir -- reject riski. DOGE kapsam disi.
- LOT_SIZE: SOL 0.036 SOL gecis. ADA 10.2 ADA gecis.
- Overfitting: TpAtrMultiplier hassas -- MinTpPct/MaxTpPct clip ile kontrol altina alinmali.

**Beklenen EV/saat:** $0.015-$0.036. Mevcut stratejinin 7-10 kati potansiyeli.
Kaynak: https://atozmarkets.com/news/trend-trading-vs-scalping-altcoins/
Kaynak: https://www.altcointrading.net/strategy/scalping/

---

### Strateji E: Mean Reversion RSI + Bollinger Bands (1m)

**Mantik:** 1m barlar: RSI(14) < 25 VE fiyat alt Bollinger Band (20, 2.0) altinda ise Long. TP: orta bant, SL: %0.20. Asiri satim sonrasi ortalamaya donus prensibine dayanir.

**Expected Edge Kaynagi:** RSI extreme mean-reversion icin istatistiksel kanit (QuantifiedStrategies 2024: RSI < 25 geri donus bircok asset seti %65-70+ WR). BB alt band 2-sigma olayi.

**Fee/Slippage Matematik ($5.10 sizing):**
- TP %0.40: Net Win: **$0.01173** -- SL %0.20: Net Loss: **$0.01887** -- Break-even WR: **61.7%**
- Sinyal frekansi: RSI(14) < 25 nadir -- gunde 5-15 sinyal toplamda 3 coinde
- EV/saat: **~$0.001/saat** (dusuk frekans)

**Red Flag Taramasi:**
- Sinyal frekansi cok dusuk: $0.10/saat icin yetersiz hacim
- Trending piyasa: falling knife riski. Trend filtresi zorunlu.
- Stop hunt: Whale ler kasitli RSI < 25 yaratip tersine doner

**Beklenen EV/saat:** $0.001-$0.008. Yuksek WR ama dusuk frekans -- yetersiz.
Kaynak: https://www.quantifiedstrategies.com/rsi-trading-strategy/

---

## 3. Sampiyon Secim

### Karsilastirma Matrisi

| Strateji | Break-even WR | Gercekci WR | Trade/saat | Net EV/saat | Uygulama |
|----------|--------------|-------------|------------|-------------|----------|
| Mevcut 1m VWAP+EMA | 61.8-90.8% | 47-57% | 18-22 | -$0.015..$0.005 | Mevcut |
| A. Donchian 5m | 58.8% | 50-55% | 6-10 | $0.001-$0.005 | Orta |
| B. Order-Flow | Neg. mat. | N/A | N/A | Negatif | ELENDI |
| C. 5m VWAP+EMA | 61.8% | 58-65% | 6-12 | $0.006-$0.019 | Dusuk |
| **D. Yuksek-Vol Altcoin** | **49.5%** | **52-58%** | **6-10** | **$0.015-$0.036** | **Orta** |
| E. RSI+BB Mean Rev | 61.7% | 60-70% | 0.5-2 | $0.001-$0.008 | Dusuk |

### Sampiyon: Strateji D -- SOL + ADA, AtrScalperVwapEma1m Evaluator

**Neden D secildi:**

Tek gercek cikis yolu gross TP yi buyutmek. $5.10 sizing + %0.15 round-trip fee ile anlamli net EV ancak gross TP >= %0.50-0.60 ile saglanir. ETH break-even %71.2, BNB break-even %90.8 bu esige ulasamiyor. SOL ve ADA bu hareket miktarini normal piyasa kosullarinda sagliyor.

Kilit metrikler:
- SOL break-even WR: **%49.5** -- XRP nin %61.8 inden 12 puan asagida, cok daha ulasabilir
- Fee/gross orani: SOL %24, BNB %85 -- 3.5x iyilesme
- ATR-tabanli TP piyasaya adaptif -- sideways de kucuk, volatil de buyuk

**Coin Kararlari:**

- XRP-MicroScalper: **KORU** -- Loop 32 t270 +$0.043 net, %55 WR, en iyi coin. Dokunulmaz.
- SOL-AtrScalper: **EKLE** -- TP %0.70, SL %0.35, MaxHold 6dk, VwapTol 0.008, VolMult 0.5
- ADA-AtrScalper: **EKLE** -- TP %0.60, SL %0.30, MaxHold 8dk, VwapTol 0.010, VolMult 0.3
- ETH-MicroScalper: **REVIZE** -- TpGrossPct 0.003->0.005, StopPct 0.0015->0.0025. Yeni break-even %56.0
- BNB-MicroScalper: **DONDUR** -- Break-even %90.8, matematiksel surdurulemez. Paused (BTC gibi)

---

## 4. Feasibility Verdict

### Saatte net $0.10-0.30, $100 sermayede: **ZOR**

**Matematiksel kanit:**

Saatte $0.10 kazanmak icin (SOL %55 WR, EV/trade = $0.00292):
- Gereken trade: $0.10 / $0.00292 = **34 trade/saat -- tek coin**
- 4 coin toplam 34 trade/saat = ~8-9 trade/coin/saat
- Loop 32 gercek: 7 trade/coin/saat -- 1.3x artis gerekiyor
- VWAP+EMA filtresi ile bu frekansa ulasmak icin ya parametreler gevsetilir ya sembol sayisi arttirilir

**ROI analizi:**
- $0.10/saat = $2.40/gun = **%2.4/gun ROI**
- Aylik bilesik: ~%72
- Bitcoin 2025 Sharpe: 2.42 (yillik). Gunluk %2.4 surdurulebilir Sharpe ile uyumsuz.
- Akademik Kelly/Sharpe cercevesinde %2.4/gun sistemik strateji ile degil, yuksek volatilite anlari icin gecerli.

**Kelly Criterion:**
SOL Tam Kelly: K = (p x b - q) / b = (0.55 x 1.019 - 0.45) / 1.019 = **%10.8 sermaye**
$100 x %10.8 = $10.8/trade -- mevcut $5.10 Yarim Kelly altinda, saglikli ve ihtiyatli.
Kaynak: https://coinmarketcap.com/academy/article/what-is-the-kelly-bet-size-criterion-and-how-to-use-it-in-crypto-trading

**Gercekci hedef (binance-expert kanati):**
- Saatte **$0.03-$0.06**
- Gunluk **$0.72-$1.44** (%0.72-1.44/gun ROI)
- Aylik **$21-$43** (%21-43 ROI -- standart kripto hedge fonun 2-4 kati)
- Saatte $0.10 an lik mumkun (yuksek vol pencerelerinde) ama mekanik stratejiyle surdurulebilir degil
- $0.10/saat icin: sermaye $300+ a ciksin VEYA per-trade sizing $15+ a yukselsin

---

## 5. Implementation Roadmap (Loop 33)

### Yeni Evaluator Sinifi

Isim onerisi: AtrScalperVwapEmaEvaluator
StrategyType enum: AtrScalperVwapEma1m (yeni deger, eski tipler backward-compat icin korunur)

Mevcut MicroScalperVwapEma30sEvaluator kodunun ~%80 i korunur. Degisen kisim:
- Parameters class: AtrPeriod, TpAtrMultiplier, StopAtrMultiplier, MinTpPct, MaxTpPct eklenir
- TP/SL hesaplama: sabit yuzde yerine Atr14 x multiplier, Min/Max ile clip
- VWAP reclaim, EMA slope, volume confirm mantigi degismez -- aynen aktarilir

### Gerekli Indicator Genisletmesi

1. MicroScalperIndicatorSnapshot record una Atr14 decimal field eklenir (default 0, non-breaking)
2. MarketIndicatorService.TryGetMicroScalperSnapshot metodunda Atr14 hesabi:
   var atr14 = Evaluators.Indicators.Atr(klines, 14);
3. Indicators.Atr() zaten mevcut -- Indicators.cs satir 68-92. Sifir ek is yok.
4. Warmup esigi: Atr14 icin 14+1=15 bar minimum. Mevcut 21-bar esigi karsilar.

### appsettings.json Parametre Sablonu

Binance.Symbols: SOLUSDT ve ADAUSDT ekle.

SOL-AtrScalper: Type=AtrScalperVwapEma1m, Symbols=[SOLUSDT]
  KlineInterval=1m, EmaPeriod=20, VwapWindowBars=15, VwapTolerancePct=0.008
  VolumeMultiplier=0.5, SlopeTolerance=-0.003, AtrPeriod=14
  TpAtrMultiplier=1.5, StopAtrMultiplier=0.75, MinTpPct=0.005, MaxTpPct=0.012, MaxHoldMinutes=6

ADA-AtrScalper: Type=AtrScalperVwapEma1m, Symbols=[ADAUSDT]
  KlineInterval=1m, EmaPeriod=20, VwapWindowBars=15, VwapTolerancePct=0.010
  VolumeMultiplier=0.3, SlopeTolerance=-0.003, AtrPeriod=14
  TpAtrMultiplier=1.4, StopAtrMultiplier=0.70, MinTpPct=0.004, MaxTpPct=0.010, MaxHoldMinutes=8

ETH-MicroScalper guncelleme: TpGrossPct 0.003->0.005, StopPct 0.0015->0.0025
BNB-MicroScalper: Activate=false (Paused)

### Risk ve Edge Caseler

1. MIN_NOTIONAL / LOT_SIZE: SOL stepSize=0.00001, gecis. ADA ~$0.50 ile 10.2 ADA, gecis. DOGE borderline reject riski -- kapsam disi.

2. ATR sifir riski: Flat piyasada ATR yaklasir sifira. MinTpPct clip ile cozumlu.

3. Backfill: SOLUSDT/ADAUSDT eklendiklerinde 1440 bar REST backfill otomatik calisir. BinanceOptions.Symbols guncellemesi yeterli.

4. Simultaneous positions: XRP+SOL+ADA+ETH(revize) = max 4 acik. MaxOpenPositions=6 koruyor.

5. BNB: Silinmemeli (backward-compat/DB), sadece Activate=false veya Status=Paused.

6. XRP koruma: Loop 32 t270 +$0.043 net, %55 WR. TpGrossPct=0.004, StopPct=0.002 DOKUNULMAZ.

---

## Ozet (150 kelime)

**Sampiyon: Strateji D -- SOL + ADA Yuksek Volatiliteli Altcoin, AtrScalperVwapEma1m Evaluator**

Mevcut BNB/ETH geometrisi matematiksel olarak kirik: BNB break-even WR %90.8, ETH %71.2. Sadece XRP tutunuyor (Loop 32 t270: +$0.043 net, %55 WR). Cozum gross TP yi buyutmek -- bu da daha yuksek ATR li sembol gerektirir.

SOL ve ADA, ayni VWAP+EMA mantikiyla %0.60-0.80 TP hedefi sunar. Fee etkisi brut kazancin %24 une iner (BNB de %85 idi). SOL break-even WR %49.5 -- ulasabilir esik.

**Beklenen EV:** Saatte $0.015-$0.036 (4 coin: XRP+SOL+ADA+ETH revize). Gercekci aylik $21-$43 (%21-43 ROI).

**Feasibility Verdict:** Saatte net $0.10-0.30 **ZOR** -- $100 sermaye + $5.10 sizing matematigi yetmiyor. Gercekci hedef saatte **$0.03-$0.06**. Saatte $0.10+ icin sermaye en az 3x artmali ($300+).

**Sonraki Adim:** Backend-dev AtrScalperVwapEma1m evaluator + SOL/ADA sembol ekleme + BNB Pause + ETH TP revizyon.

---

## Kaynaklar

- [Binance WebSocket Streams -- kline interval, bookTicker](https://developers.binance.com/docs/binance-spot-api-docs/web-socket-streams)
- [Binance REST Market Data -- order book, kline endpoint](https://developers.binance.com/docs/binance-spot-api-docs/rest-api/market-data-endpoints)
- [Binance Fee Schedule -- VIP0 %0.1, BNB %25 discount](https://www.binance.com/en/fee/schedule)
- [Order Flow Scalping Edge -- Bookmap 2026](https://bookmap.com/blog/can-real-time-order-flow-give-you-an-edge-in-scalp-trading)
- [Donchian Channel Breakout -- PyQuantLab 2024](https://pyquantlab.medium.com/a-donchian-channel-breakout-strategy-a-simple-trend-following-approach-18b7b74c4358)
- [VWAP Trading Strategy -- Morpher 2025](https://www.morpher.com/blog/vwap-indicator)
- [RSI Win Rate Backtest -- QuantifiedStrategies](https://www.quantifiedstrategies.com/rsi-trading-strategy/)
- [Kelly Criterion Crypto -- CoinMarketCap Academy](https://coinmarketcap.com/academy/article/what-is-the-kelly-bet-size-criterion-and-how-to-use-it-in-crypto-trading)
- [$100 Scalping Reality -- AltcoinTrading.NET](https://www.altcointrading.net/strategy/scalping/)
- [VWAP+EMA 5m Confluence -- CryptoWisser 2026](https://www.cryptowisser.com/guides/fibonacci-vwap-ema-crypto-scalping/)
- [Altcoin Scalping vs Trend -- AtoZMarkets](https://atozmarkets.com/news/trend-trading-vs-scalping-altcoins/)
- [Bollinger Bands Mean Reversion -- FMZQuant](https://medium.com/@FMZQuant/bollinger-bands-mean-reversion-trading-strategy-dc80a7ff7a4f)
