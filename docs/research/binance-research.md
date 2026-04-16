# Binance Arastirma — BinanceBot

**Durum:** One-shot master plan adim 1/6 (binance-expert).
**Hedef:** BTC/USDT, ETH/USDT, BNB/USDT Spot sembollerinde REST + WebSocket tuketimi ve ileride otomatik trade. Testnet-first; production key boot-time reddedilir.
**Terim kullanimi:** docs/glossary.md ubiquitous language ile tutarli (Kline, Order, BookTicker, Depth, CombinedStream, Stream, Slippage, Spread, MaxDrawdown, Aggregate).

Bu belge; [docs/plan.md](../plan.md), ADR ler ve `docs/features/*.md` slice lari tarafindan referans alinir. Her iddianin sonunda **Kaynak:** satiri vardir.

---

## 1. Binance Spot REST API

### 1.1 Endpoint Envanteri ve Weight

Binance Spot REST tabaninda her endpoint bir `weight` ile isaretlidir; kullanicinin 1 dakikalik `REQUEST_WEIGHT` kuyugundan bu agirlik duser. Bizim BTC/ETH/BNB izleme + ileride trade senaryosu icin kritik endpointler:

| Endpoint | HTTP | Weight | Notu |
|---|---|---|---|
| `/api/v3/time` | GET | 1 | Clock sync icin sart; boot + saatte bir cagir. |
| `/api/v3/exchangeInfo` | GET | 20 | Tum filterlar (LOT_SIZE/PRICE_FILTER/NOTIONAL) burada. Gunde 1-2 kez cachele. |
| `/api/v3/klines` | GET | 2 | OHLCV gecmisi; default 500, max 1000 bar/istek. Warmup + indikator seed. |
| `/api/v3/depth` | GET | **5-250** | `limit` e gore skale: limit <= 100 -> 5, 101-500 -> 25, 501-1000 -> 50, 1001-5000 -> **250**. |
| `/api/v3/trades` | GET | 25 | Son 500 trade. |
| `/api/v3/historicalTrades` | GET | 25 | API key gerektirir. |
| `/api/v3/ticker/24hr` | GET | **2-80** | Tek sembol: 2; `symbol` bos ise 80 (tum semboller — pahali). |
| `POST /api/v3/order` | POST | 1 | TRADE; order-rate limitine (UID) de duser. |
| `POST /api/v3/order/test` | POST | 1 veya 20 | `computeCommissionRates=true` -> 20. Matching engine e gitmez, sadece filter validasyonu. |
| `DELETE /api/v3/order` | DELETE | 1 | |
| `GET /api/v3/order` | GET | 2 | |
| `GET /api/v3/account` | GET | 10 | |
| `GET /api/v3/myTrades` | GET | 10 | |

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md

### 1.2 Rate Limit Modeli

Binance 3 ayri butce tutar: **REQUEST_WEIGHT**, **ORDERS**, **RAW_REQUESTS**. Butceler 1 dakikalik ve 1 gunluk pencerelerde hesaplanir. Guncel kullanim her response un header inda doner:

- `X-MBX-USED-WEIGHT-1M` — su anki dakika agirlik toplami
- `X-MBX-ORDER-COUNT-10S` / `X-MBX-ORDER-COUNT-1D` — order rate

Asilirsa HTTP **429 Too Many Requests** (+ `Retry-After` header). 429 ignore edilip istek atilmaya devam edilirse HTTP **418 I am a Teapot** -> IP ban. Ban suresi 2 dakikadan baslar, tekrarlanirsa 3 gune kadar katlanir.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md

**Sonuc — BinanceBot icin:** 3 sembol x `kline_1m` stream i zaten WebSocket uzerinden akiyor; REST i sadece (a) warmup klines, (b) depth snapshot, (c) exchangeInfo cache, (d) order lifecycle icin kullan. REST polling ile fiyat cekme anti-pattern (`-1003 TOO_MANY_REQUESTS`). Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/errors.md

### 1.3 Signed Endpoint (HMAC-SHA256)

Guvenlik tipleri: `NONE` (public), `TRADE`, `USER_DATA`, `USER_STREAM`. `TRADE` ve `USER_DATA`, imzali (`SIGNED`) endpoint. Kural:

1. Query string + body yi sirayla birlestir (percent-encoded).
2. `timestamp` (ms, int64) ekle; opsiyonel `recvWindow` (varsayilan 5000ms, **max 60000ms**).
3. HMAC-SHA256(secretKey, payload) -> hex string.
4. `signature` parametresi olarak ekle.
5. `X-MBX-APIKEY: <apiKey>` header zorunlu.

Sunucu `timestamp` tarihini su aralikta kabul eder: `serverTime - recvWindow <= timestamp <= serverTime + 1000ms`. Aralik disi istek -> error **-1021 INVALID_TIMESTAMP**. Cozum: periyodik `GET /api/v3/time` ile local clock offset tut ve her imzali istekte `timestamp = now() - offset` kullan.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md
Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/errors.md

### 1.4 Symbol Filter lari

Her sembol icin `exchangeInfo` altinda zorunlu filterlar var. Order gonderirken ihlal -> matching engine order i reddeder (**-1013 INVALID_MESSAGE / -2010 NEW_ORDER_REJECTED**).

| Filter | Alanlar | Anlam |
|---|---|---|
| `PRICE_FILTER` | `minPrice`, `maxPrice`, `tickSize` | Fiyat `tickSize` in tam kati olmali. |
| `PERCENT_PRICE_BY_SIDE` | `bidMultiplierUp/Down`, `askMultiplierUp/Down`, `avgPriceMins` | Limit price weighted avg den cok uzak olamaz. |
| `LOT_SIZE` | `minQty`, `maxQty`, `stepSize` | Miktar `stepSize` kati. |
| `MARKET_LOT_SIZE` | `minQty`, `maxQty`, `stepSize` | Market emri icin ayri. |
| `MIN_NOTIONAL` / `NOTIONAL` | `minNotional`, `maxNotional`, `applyMinToMarket`, `applyMaxToMarket`, `avgPriceMins` | `price * qty` bu aralikta. |
| `ICEBERG_PARTS` | `limit` | Iceberg segment sayisi. |
| `MAX_NUM_ORDERS` | - | Sembol basina acik order sayisi. |
| `MAX_NUM_ALGO_ORDERS` | - | STOP_LOSS, TAKE_PROFIT gibi algo order tavani. |
| `MAX_POSITION` | - | Base asset maksimum pozisyon. |
| `TRAILING_DELTA` | min/max | Trailing stop parametresi siniri. |

Yuvarlama kurali: `value % interval == 0`. Uygulama: quantity `stepSize` a **round-down**, price `tickSize` a **round-down** (slippage e karsi konservatif). Exchange-wide filter lar da hesap bazli: `EXCHANGE_MAX_NUM_ORDERS`, `EXCHANGE_MAX_NUM_ALGO_ORDERS`.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/filters.md

---

## 2. Binance Spot WebSocket

### 2.1 Base Endpointler

| Tur | URL |
|---|---|
| Raw stream (prod) | `wss://stream.binance.com:9443/ws/<streamName>` |
| Raw stream (alt port) | `wss://stream.binance.com:443/ws/<streamName>` |
| Combined (prod) | `wss://stream.binance.com:9443/stream?streams=<s1>/<s2>/<s3>` |
| Testnet raw | `wss://stream.testnet.binance.vision/ws/<streamName>` |
| Testnet combined | `wss://stream.testnet.binance.vision/stream?streams=...` |

Stream adlari daima **kucuk harf**: `btcusdt@kline_1m`, `ethusdt@bookTicker`, `bnbusdt@depth@100ms`.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/web-socket-streams.md
Kaynak: https://testnet.binance.vision/

### 2.2 Stream Tipleri

| Stream | Isim | Guncelleme | Onemli alanlar | Kullanim |
|---|---|---|---|---|
| aggTrade | `<s>@aggTrade` | realtime | `a` agg trade id, `p` price, `q` qty, `f`/`l` first/last trade id, `T` time | Hacim, fiyat akisi. |
| trade | `<s>@trade` | realtime | `t` trade id, `p`, `q`, `m` buyer-is-maker | Her tick. |
| kline | `<s>@kline_<interval>` | ~1-2s | `k` objesi: `t`/`T` open/close time, `o`/`c`/`h`/`l`, `v`, `x` closed flag | Indikator hesaplama. `x=true` oldugunda bar finalize. |
| miniTicker | `<s>@miniTicker` | 1s | `c`, `o`, `h`, `l`, `v`, `q` | 24h ozet. |
| ticker | `<s>@ticker` | 1s | 24h rolling: `p` change, `P` %, `w` weighted avg, `b`/`a` best bid/ask | Dashboard metrikleri. |
| bookTicker | `<s>@bookTicker` | realtime | `u` updateId, `b`/`B` best bid price/qty, `a`/`A` best ask price/qty | En ucuz spread + basit slipaj tahmini. |
| partial depth | `<s>@depth<5,10,20>[@100ms]` | 100ms veya 1s | `lastUpdateId`, `bids[][]`, `asks[][]` | Snapshot tarzi order book. |
| diff depth | `<s>@depth[@100ms]` | 100ms veya 1s | `U`/`u` first/final update id, `b`, `a` | Tam derinlik (snapshot + diff ile). |
| userData | `<listenKey>` | event driven | `outboundAccountPosition`, `balanceUpdate`, `executionReport`, `listStatus` | Order lifecycle. |

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/web-socket-streams.md

### 2.3 Ping/Pong ve 24 Saat Kurali

- Server **her 20 saniyede bir ping frame** gonderir.
- Client 1 dakika icinde pong ile yanit vermezse baglanti kapatilir.
- Tek connection max **24 saat**; Binance otomatik kapatir -> reconnect sart.
- **5 msg/sn** incoming mesaj limiti.
- **1024 stream/connection**, **300 connection / 5 dk per IP**.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/web-socket-streams.md
Kaynak: https://www.binance.com/en/academy/articles/what-are-binance-websocket-limits

### 2.4 Depth Snapshot + Diff Senkronizasyonu

1. `wss://stream.binance.com:9443/ws/<symbol>@depth[@100ms]` connect, event leri buffer a yaz. Ilk event in `U` degerini not et.
2. `GET /api/v3/depth?symbol=<SYMBOL>&limit=5000` snapshot cek (weight 250).
3. Snapshot `lastUpdateId` < ilk buffer event in `U` suysa -> 1 e don.
4. Buffer dan `u <= lastUpdateId` olan event leri at.
5. Ilk uygulanacak event icin `U <= lastUpdateId + 1 <= u` olmali; degilse 1 e don.
6. Local book u snapshot tan seed et; her event te `qty > 0` -> upsert, `qty == 0` -> delete. `lastUpdateId = event.u`.
7. Sonraki event `U` su beklenen `prev_u + 1` den buyukse **gap** -> 1 e don.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/web-socket-streams.md

### 2.5 User Data Stream

| Adim | HTTP | Endpoint | Weight | Notu |
|---|---|---|---|---|
| Olustur | POST | `/api/v3/userDataStream` | 1 | listenKey 60 dk gecerli. |
| Keepalive | PUT | `/api/v3/userDataStream?listenKey=...` | 1 | **30 dakikada bir cagirmali**. |
| Kapat | DELETE | `/api/v3/userDataStream?listenKey=...` | 1 | |
| Baglan | WSS | `wss://stream.binance.com:9443/ws/<listenKey>` | - | |

Eventler: `outboundAccountPosition`, `balanceUpdate`, `executionReport`, `listStatus`.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/user-data-stream.md

### 2.6 Reconnect + Replay + Idempotent Handler Pattern

1. **Exponential backoff** + jitter (1s -> 2 -> 4 -> 8 -> 30s cap), Polly v8.
2. **Connection rotator**: 23 saatte bir preemptive reconnect; 24h cut tan once failover.
3. **Replay on reconnect**: kline icin son N bar REST warmup; depth icin snapshot+diff resync.
4. **Idempotent handler**: kline `k.t` + `k.x` ile upsert; trade `t` id ile dedup.
5. **`Channel<T>` producer/consumer**: bounded + `DropOldest` backpressure.
6. **Clock offset watcher**: saatte bir `GET /api/v3/time`; offset > 2s alarm.

Kaynak: https://learn.microsoft.com/en-us/dotnet/standard/threading/channels
Kaynak: https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience

---

## 3. Order Turleri + timeInForce

### 3.1 Enumlar

- **OrderType**: `LIMIT`, `MARKET`, `STOP_LOSS`, `STOP_LOSS_LIMIT`, `TAKE_PROFIT`, `TAKE_PROFIT_LIMIT`, `LIMIT_MAKER`.
- **OrderSide**: `BUY`, `SELL`.
- **timeInForce**: `GTC` (Good Til Canceled), `IOC` (Immediate Or Cancel), `FOK` (Fill Or Kill).
- **OrderStatus**: `NEW`, `PARTIALLY_FILLED`, `FILLED`, `CANCELED`, `PENDING_CANCEL`, `REJECTED`, `EXPIRED`, `EXPIRED_IN_MATCH`.
- **SelfTradePreventionMode**: `NONE`, `EXPIRE_MAKER`, `EXPIRE_TAKER`, `EXPIRE_BOTH`, `DECREMENT`, `TRANSFER`.
- **Kline intervals** (case-sensitive): `1s`, `1m`, `3m`, `5m`, `15m`, `30m`, `1h`, `2h`, `4h`, `6h`, `8h`, `12h`, `1d`, `3d`, `1w`, `1M`.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/enums.md
Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md

### 3.2 Order Tipi ve Zorunlu Parametre Haritasi

| Type | Zorunlu parametreler | timeInForce zorunlu mu? |
|---|---|---|
| `MARKET` | `quantity` **ya da** `quoteOrderQty` | Hayir |
| `LIMIT` | `timeInForce`, `quantity`, `price` | **Evet** |
| `STOP_LOSS` | `quantity`, `stopPrice` **ya da** `trailingDelta` | Hayir |
| `STOP_LOSS_LIMIT` | `timeInForce`, `quantity`, `price`, `stopPrice` veya `trailingDelta` | **Evet** |
| `TAKE_PROFIT` | `quantity`, `stopPrice` veya `trailingDelta` | Hayir |
| `TAKE_PROFIT_LIMIT` | `timeInForce`, `quantity`, `price`, `stopPrice` veya `trailingDelta` | **Evet** |
| `LIMIT_MAKER` | `quantity`, `price` | Hayir (taker olursa reddedilir) |

`newOrderRespType`: `ACK` / `RESULT` / `FULL`. `MARKET` ve `LIMIT` default `FULL`, digerleri default `ACK`.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md

### 3.3 Order List Endpointleri

- `POST /api/v3/order/oco/new` — One-Cancels-Other. Eski `/api/v3/order/oco` **deprecated**.
- `POST /api/v3/order/oto/new` — One-Triggers-Other.
- `POST /api/v3/order/otoco/new` — Kombine.
- `POST /api/v3/order/opo/new` — One-Placed-Other.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md

### 3.4 Test Order Disiplini

`POST /api/v3/order/test` matching engine e gitmez ama **tum filter validasyonunu** yapar. Weight 1 (default), 20 (`computeCommissionRates=true`). Kural: `OrderSubmitCommand` hem production hem staging de otomatik olarak `/order/test` ten gecer; production-mode da testnet bayragi guard i zorunlu.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md

### 3.5 Testnet

- REST: `https://testnet.binance.vision/api`
- WS: `wss://stream.testnet.binance.vision/ws` ve `/stream`
- Auth: GitHub login + API key (HMAC / RSA / Ed25519)
- Sadece `/api/*`; `/sapi/*` yok
- Rate limit ve filter lar production ile ayni
- Bakiyeler virtuel, aylik reset (API key korunur)

Boot-time guard onerisi: `useTestnet: true` config flag + base URL ayrimi + `ASPNETCORE_ENVIRONMENT=Production && !useTestnet` ise startup throw.

Kaynak: https://testnet.binance.vision/

---

## 4. Populer Trading Stratejileri — Karlilik + Risk Analizi

### 4.1 Kaynak Ekosistem (GitHub)

| Repo | Yildiz (~) | Odak | Alinacak pattern |
|---|---|---|---|
| [freqtrade/freqtrade](https://github.com/freqtrade/freqtrade) | 48.8k | Python bot + hyperopt + dry-run + FreqAI | Strategy interface, backtest disiplini. |
| [jesse-ai/jesse](https://github.com/jesse-ai/jesse) | 7.7k | Python framework + 300+ indikator + Monte Carlo | No look-ahead bias, walk-forward. |
| [hummingbot/hummingbot](https://github.com/hummingbot/hummingbot) | 18.2k | Market making / HFT | Avellaneda-Stoikov, pure MM. |
| [ccxt/ccxt](https://github.com/ccxt/ccxt) | 41.9k | Unified exchange API (Node/Python/PHP/.NET/Go) | `fetchOHLCV`, `createOrder`, `watchTrades` abstraction. |
| [tiagosiebler/TriangularArbitrage](https://github.com/tiagosiebler/TriangularArbitrage) | - | Binance tri-arb scanner | Edge teshisi. |

Kaynak: https://github.com/freqtrade/freqtrade
Kaynak: https://github.com/jesse-ai/jesse
Kaynak: https://github.com/hummingbot/hummingbot
Kaynak: https://github.com/ccxt/ccxt

### 4.2 Akademik + Sektor Ozet

- **Momentum (Bitcoin)**: Cross-sectional + time-series momentum crypto da daha guclu calisir; intraday momentum + reversal karisik evidence. Kaynak: https://www.sciencedirect.com/science/article/abs/pii/S1062940821000590
- **Factor momentum**: Winner faktorler crypto da persistent outperform. Kaynak: https://open.icm.edu.pl/server/api/core/bitstreams/86a51c47-8cd3-4201-88ee-42f44fb89227/content
- **Market microstructure / LOB imbalance**: OBI kisa vade fiyat tahmininde guclu sinyal; yuksek noise. ML baseline -> XGBoost -> DeepLOB spektrum. Kaynak: https://arxiv.org/html/2506.05764v2
- **Triangular arbitrage (retail)**: Binance te 4879 teorik firsat tespit edildi; fee + spread + rekabet sonrasi **net kar kalmiyor**. Kaynak: https://www.sciencedirect.com/science/article/pii/S154461232401537X
- **Kelly criterion**: Full Kelly 50%+ drawdown yapar; **Half Kelly** growth un %75 ini alip volatiliteyi %25 e dusurur; crypto da **Quarter Kelly** onerilir. Kaynak: https://cryptogambling.com/guides/sports-betting/fractional-kelly-practical

### 4.3 Pattern Katalogu

Her pattern icin: **kosul / edge / risk / round-trip maliyet / uygun rejim / tipik PnL**.

#### 4.3.1 Grid Trading

- **Kosul:** Range-bound piyasa; alt ve ust bant.
- **Edge:** Volatilite emilim — ayni aralikta tekrarlayan bid/ask osilasyonu.
- **Risk:** Trend baslarsa range terk edilir, pozisyon sisiyor. Leveraged grid kullanicilarinin **%78 i likidasyon** yasar.
- **Round-trip:** 2 x 0.1% (maker) = 0.2%; BNB indirimi ile 0.15%.
- **Uygun rejim:** Dusuk ATR, yatay trend.
- **Tipik PnL:** Konservatif aylik %3-8 (no leverage); fee/slipaj sonrasi gunluk ham %1 -> net %0.2 seviyesine dusebilir.

Kaynak: https://zignaly.com/crypto-trading/algorithmic-strategies/grid-trading
Kaynak: https://www.gate.com/news/detail/13225882

#### 4.3.2 DCA (Dollar Cost Averaging)

- **Kosul:** Uzun vadeli long bias; giris timing ini pasiflestirmek.
- **Edge:** Volatilite normalize -> ortalama maliyet duser.
- **Risk:** Surekli dususte sermaye yakma.
- **Round-trip:** Taker 0.1% (cogu DCA market).
- **Uygun rejim:** Uzun vadeli uptrend beklentisi.

Kaynak: https://medium.com/@alsgladkikh/comparing-strategies-dca-vs-grid-trading-2724fa809576

#### 4.3.3 Market Making (Bid-Ask Capture)

- **Kosul:** Yuksek likidite; dusuk latency.
- **Edge:** Spread capture.
- **Risk:** Adverse selection (toxic flow); inventory risk.
- **Round-trip:** Maker-maker -> 0.2%; VIP rebate ile daha dusuk.
- **Uygun rejim:** Yatay, derin defter.
- **Tipik:** Retail icin pratik degil; Avellaneda-Stoikov gerektirir.

Kaynak: https://github.com/hummingbot/hummingbot
Kaynak: https://hftbacktest.readthedocs.io/en/latest/tutorials/Market%20Making%20with%20Alpha%20-%20Order%20Book%20Imbalance.html

#### 4.3.4 Trend Following (MA Crossover / Donchian / SuperTrend)

- **Kosul:** Yeterli momentum (>ATR threshold).
- **Edge:** Serial autocorrelation; crypto da akademik dogrulama.
- **Risk:** Whipsaw (yatay piyasada iki yonlu zarar). Dusuk win-rate (%30-40) ama yuksek R ratio.
- **Round-trip:** Taker 0.2% + stop-loss 0.1% = 0.3%.
- **Uygun rejim:** Guclu trend, orta-yuksek ATR.
- **Tipik:** Sharpe ~0.8-1.2, MDD %20-40.

Kaynak: https://assets.super.so/e46b77e7-ee08-445e-b43f-4ffd88ae0a0e/files/9c27aa78-9b14-4419-a53d-bc56fa9d43b2.pdf
Kaynak: https://www.sciencedirect.com/science/article/abs/pii/S1062940821000590

#### 4.3.5 Mean Reversion (Bollinger + RSI)

- **Kosul:** RSI < 30 + close < BB_lower -> long.
- **Edge:** Short-term reversal.
- **Risk:** Trend piyasasinda surekli yanlis sinyal.
- **Round-trip:** Limit entry maker 0.1% + market exit taker 0.1% = 0.2%.
- **Uygun rejim:** Range-bound.
- **Tipik:** Yuksek win-rate (%55-65), kucuk R.

Kaynak: https://medium.com/@redsword_23261/rsi-macd-bollinger-bands-and-volume-based-hybrid-trading-strategy-fb1ecfd58e1b

#### 4.3.6 Funding-Rate Arbitrage (Futures)

**NOT_IN_SCOPE** — BinanceBot Spot-only. Futures ileride ayri workstream.

Kaynak: https://www.cfbenchmarks.com/blog/revisiting-the-bitcoin-basis-how-momentum-sentiment-impact-the-structural-drivers-of-basis-activity

#### 4.3.7 Triangular Arbitrage (BTC/ETH/BNB Kross)

- **Kosul:** BTCUSDT x ETHBTC x ETHUSDT fiyat dongusu sapmasi.
- **Edge:** Pure arbitrage.
- **Risk:** Fee 0.1% x 3 leg = 0.3% minimum round-trip; firsat %0.3 ten kucukse kayip. Front-running botlari retail i ezer.
- **Tipik:** Retail icin **NOT VIABLE** (akademik dogrulama). Ileri faz.

Kaynak: https://www.sciencedirect.com/science/article/pii/S154461232401537X

#### 4.3.8 Breakout (ATR-Filtered)

- **Kosul:** N-bar high kirilmimi (Donchian) + ATR > threshold.
- **Edge:** Volatility expansion.
- **Risk:** False breakout lar %60+ oraninda; zaman filtresi gerek.
- **Round-trip:** Taker 0.2%.
- **Tipik:** Sharpe 0.5-1.0; orta MDD.

Kaynak: https://www.tradingview.com/scripts/averagetruerange/

---

## 5. Kritik Indikatorler

| Indikator | Default | Formul ozeti | Zayif nokta | Hangi stratejiye uyar |
|---|---|---|---|---|
| RSI | 14 period | `100 - 100/(1+RS)` — RS = avg gain / avg loss | Flat piyasada 50 civari yapisir, yanlis sinyal. | Mean reversion, divergence. |
| MACD | 12-26-9 | `EMA12 - EMA26 = macd; signal = EMA9(macd); hist = macd-signal` | Lagging; crypto da gec sinyal. | Trend confirmation. |
| Bollinger Bands | 20 SMA, 2 sigma | `middle +/- 2*stdev(close, 20)` | Regime degistiginde walk-the-band false exit. | Mean reversion, volatility squeeze. |
| ATR | 14 period | `EMA(max(H-L, abs(H-PrevC), abs(L-PrevC)), 14)` | Trend yon bilgisi vermez. | Risk sizing, stop-loss, breakout filter. |
| VWAP | intraday session | `Sum(price * volume) / Sum(volume)` | Sessiz piyasada volume distorsiyonu. | Intraday mean, institutional benchmark. |
| OBV | cumulative | `OBV += close>prev ? +vol : close<prev ? -vol : 0` | Volume manipulasyonu etkiler. | Volume/price divergence. |
| Order Book Imbalance | 1-5 seviye | `(bidQty - askQty) / (bidQty + askQty)` | Cok gurultulu, 100ms-1s pencere gerekir. | Market making, kisa vade. |
| Funding Rate | Spot a gelmez | Perp futures sigorta | Spot-only BinanceBot NOT_IN_SCOPE. | Futures arbitrage. |
| Open Interest | Futures agregat | Acik pozisyon sayisi | Spot-only NOT_IN_SCOPE. | Trend confirmation (futures). |

Kaynak: https://dex.gate.com/crypto-wiki/article/how-to-use-macd-rsi-and-bollinger-bands-for-crypto-trading-success-20260123
Kaynak: https://www.altrady.com/crypto-trading/technical-analysis/average-true-range
Kaynak: https://towardsdatascience.com/price-impact-of-order-book-imbalance-in-cryptocurrency-markets-bf39695246f6/

---

## 6. Red Flag Taramasi

binance-trading-strategy-review skill i: her strateji onerisi asagidaki filtreden gecer.

### 6.1 Likidite

- **BTC/USDT, ETH/USDT, BNB/USDT**: Binance in en likid 3 pair i; spread tipik 1-2 bps.
- Small-cap: thin order book, surekli slippage. Kapsamda degil.

Kaynak: https://www.mdpi.com/1911-8074/18/3/124

### 6.2 Spread + Fee

- Regular user: **0.1% maker / 0.1% taker**; BNB ile %25 indirim -> 0.075%.
- Round-trip: Grid/MM maker-maker 0.15-0.2%; Trend/breakout taker-taker 0.2%.
- VIP0 retail icin firsat > 0.3% olmayan strateji fee yuzunden yenir.

Kaynak: https://www.binance.com/en/fee/trading

### 6.3 Slippage (Market Order Etkisi)

- Kucuk order (< $10k BTC/USDT): spread icinde, slipaj < 1 bps.
- Buyuk order: order book walk simulasyonu zorunlu.
- BookTicker + depth5@100ms ile canli spread izleme sart.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/web-socket-streams.md

### 6.4 Look-Ahead Bias

- Indikator hesabi **close da** olmali. Kline stream de `k.x == true` olmadan bar final degil. Partial bar da sinyal yasak.
- Backtest te `t_signal = t_bar_close`, fill `t_bar_close + 1`.

Kaynak: https://www.blockchain-council.org/cryptocurrency/backtesting-ai-crypto-trading-strategies-avoiding-overfitting-lookahead-bias-data-leakage/

### 6.5 Overfitting

- 888 Quantopian calismasi: in-sample Sharpe out-of-sample ile **R^2 < 0.025**.
- **Walk-forward analysis** + **CPCV** zorunlu. Tek parametre set backtest yasak.
- MVP: 6 ay train / 3 ay test, 3 fold rolling.

Kaynak: https://www.sciencedirect.com/science/article/abs/pii/S0950705124011110
Kaynak: https://blog.quantinsti.com/walk-forward-optimization-introduction/

### 6.6 Survivorship Bias

- BTC/ETH/BNB icin sorun yok; evren genislerse kontrol gerek.

### 6.7 Black Swan Senaryolari

- **May 19, 2021 crash**: BTC %30 dustu, Binance futures halted, likidite 7x dusus. Dip emirler fill olmadi. Kaynak: https://acfr.aut.ac.nz/__data/assets/pdf_file/0009/686754/6b-Tim-Baumgartner-May19.pdf
- **Aralik 2025 BTC/USD1 flash**: 1-2 BTC satisi $87k -> $24k wick; thin pair order book bos. Kaynak: https://cryptopotato.com/bitcoin-didnt-crash-to-24k-binance-wick-on-illiquid-pair-explained/
- **FTX collapse (Nov 2022)**: Sistemik guven soku.

Onlem:
- Circuit breaker: tek bar %X hareket -> otomatik pause + alarm.
- Max position size hard cap.
- Stop-loss lar server-side `STOP_LOSS_LIMIT` (client-side degil — baglanti koparsa calismaz).
- Daily drawdown kill-switch (orn. %5).

### 6.8 Risk-Per-Trade

- Konsensus: **%1-2 per trade**.
- Kelly `f = (W*R - (1-W)) / R`; crypto volatilitesinde Quarter Kelly (f/4).

Kaynak: https://medium.com/@tmapendembe_28659/kelly-criterion-vs-fixed-fractional-which-risk-model-maximizes-long-term-growth-972ecb606e6c

### 6.9 Max Drawdown

- Trend following: %20-40 tipik.
- Mean reversion (siki stop): %10-20.
- Grid (range icinde): %5-15; range kirilirsa sinirsiz.
- Kill-switch MDD > 25% -> trading durdur.

---

## 7. BinanceBot Icin Ilk 3 Uygulanabilir Strateji

Asagidaki 3 strateji 6. Bolum red-flag taramasindan temiz gecti; BTC/USDT + ETH/USDT + BNB/USDT likidite evrenine uygun; fee sonrasi pozitif beklenti olasiligi makul.

### 7.1 Grid Trading (Range-Bound BTC/USDT)

| Alan | Deger |
|---|---|
| **Giris kosulu** | ATR(14, 1h) < 1% x close **ve** son 48 saat price range %3 icinde. |
| **Grid konfig** | 10 level, +/-1.5% spread, LIMIT_MAKER emirler. |
| **Cikis kosulu** | Price %3 range disina cikarsa tum acik emirleri iptal + manual review. |
| **Stop-loss** | Aggregate loss %5 -> kill-switch. |
| **Risk per trade** | Her grid level notional portfoyun %1 i; toplam grid exposure %10. |
| **Rebalance** | Gunde 1 kez grid merkezini guncel VWAP a cek. |

### 7.2 Trend Following (MA Crossover + ATR Stop)

| Alan | Deger |
|---|---|
| **Giris kosulu** | EMA(20) > EMA(50) **ve** ADX(14) > 25 **ve** yeni close EMA(20) ustune kapanis. |
| **Cikis kosulu** | EMA(20) < EMA(50) (trend donusu) **veya** trailing stop. |
| **Stop-loss** | Server-side `STOP_LOSS_LIMIT`; 2xATR(14) mesafede. Trailing stop pozitif PnL de. |
| **Risk per trade** | Sermayenin **%1 i**; pozisyon = (%1 x equity) / (entry - stop). |
| **Semboller** | BTC/USDT, ETH/USDT (BNB korelasyon yuksek, ayri filtre). |
| **Timeframe** | 1h kline primary, 15m confirmation. |

### 7.3 Mean Reversion (Bollinger + RSI)

| Alan | Deger |
|---|---|
| **Giris kosulu (long)** | RSI(14) < 30 **ve** close < BB_lower(20, 2 sigma) **ve** 1h EMA(200) duz veya yukari. |
| **Cikis kosulu** | Close BB_middle a dondu **veya** RSI > 55. Time-stop: 12h sonra hala acik -> kapat. |
| **Stop-loss** | `STOP_LOSS_LIMIT`; entry - 1.5xATR(14). |
| **Risk per trade** | %1. Position = (%1 x equity) / (entry - stop). |
| **Yasak rejim** | EMA(200) dik asagi -> filtrele. |
| **Semboller** | Hepsi; BNB de pozisyon boyut cap %50. |

**Paper trade disiplini:** 3 strateji de production a cikmadan once minimum **30 gun testnet** + **30 gun paper (prod market data + test endpoint)** + walk-forward OOS gecis sart. Her emir `POST /api/v3/order/test` ile once validate; production a ancak go-live toggle ile gecis.

---

## Kaynaklar (Kumulatif)

### Binance Resmi

- [binance-spot-api-docs — rest-api.md](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md)
- [binance-spot-api-docs — web-socket-streams.md](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/web-socket-streams.md)
- [binance-spot-api-docs — user-data-stream.md](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/user-data-stream.md)
- [binance-spot-api-docs — filters.md](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/filters.md)
- [binance-spot-api-docs — enums.md](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/enums.md)
- [binance-spot-api-docs — errors.md](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/errors.md)
- [developers.binance.com — Spot docs portal](https://developers.binance.com/docs/binance-spot-api-docs)
- [developers.binance.com — WebSocket streams](https://developers.binance.com/docs/binance-spot-api-docs/web-socket-streams)
- [testnet.binance.vision](https://testnet.binance.vision/)
- [binance.com fee schedule (trading)](https://www.binance.com/en/fee/trading)
- [Binance Academy — WebSocket limits](https://www.binance.com/en/academy/articles/what-are-binance-websocket-limits)

### Framework / Client Referans

- [freqtrade/freqtrade](https://github.com/freqtrade/freqtrade)
- [jesse-ai/jesse](https://github.com/jesse-ai/jesse)
- [hummingbot/hummingbot](https://github.com/hummingbot/hummingbot)
- [ccxt/ccxt](https://github.com/ccxt/ccxt)
- [tiagosiebler/TriangularArbitrage](https://github.com/tiagosiebler/TriangularArbitrage)
- [binance/binance-connector-net](https://github.com/binance/binance-connector-net)

### .NET Platformu

- [Microsoft — System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/standard/threading/channels)
- [Microsoft — HTTP resilience (Polly v8)](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)

### Akademik / Sektor

- [Dynamic time series momentum of cryptocurrencies — ScienceDirect](https://www.sciencedirect.com/science/article/abs/pii/S1062940821000590)
- [Cryptocurrency Factor Momentum](https://open.icm.edu.pl/server/api/core/bitstreams/86a51c47-8cd3-4201-88ee-42f44fb89227/content)
- [Momentum and trend following for currencies and bitcoin (PDF)](https://assets.super.so/e46b77e7-ee08-445e-b43f-4ffd88ae0a0e/files/9c27aa78-9b14-4419-a53d-bc56fa9d43b2.pdf)
- [Exploring Microstructural Dynamics in Cryptocurrency LOBs — arXiv 2506.05764](https://arxiv.org/html/2506.05764v2)
- [Price Impact of Order Book Imbalance — Towards Data Science](https://towardsdatascience.com/price-impact-of-order-book-imbalance-in-cryptocurrency-markets-bf39695246f6/)
- [Triangular arbitrage exploitability — ScienceDirect](https://www.sciencedirect.com/science/article/pii/S154461232401537X)
- [Order Book Liquidity on Crypto Exchanges — MDPI](https://www.mdpi.com/1911-8074/18/3/124)
- [Bitcoin Flash Crash May 19, 2021 (AUT Research)](https://acfr.aut.ac.nz/__data/assets/pdf_file/0009/686754/6b-Tim-Baumgartner-May19.pdf)
- [Backtest overfitting CPCV study — ScienceDirect](https://www.sciencedirect.com/science/article/abs/pii/S0950705124011110)
- [Walk-Forward Optimization — QuantInsti](https://blog.quantinsti.com/walk-forward-optimization-introduction/)

### Strateji / Indikator Endustri Yazilari

- [Zignaly — Grid trading guide 2025](https://zignaly.com/crypto-trading/algorithmic-strategies/grid-trading)
- [Gate News — Crypto Trading Bot Pitfalls 2025](https://www.gate.com/news/detail/13225882)
- [Medium — DCA vs. Grid Trading](https://medium.com/@alsgladkikh/comparing-strategies-dca-vs-grid-trading-2724fa809576)
- [Gate wiki — MACD/RSI/Bollinger](https://dex.gate.com/crypto-wiki/article/how-to-use-macd-rsi-and-bollinger-bands-for-crypto-trading-success-20260123)
- [Altrady — ATR guide](https://www.altrady.com/crypto-trading/technical-analysis/average-true-range)
- [TradingView — ATR scripts](https://www.tradingview.com/scripts/averagetruerange/)
- [Medium — Hybrid RSI/MACD/BB](https://medium.com/@redsword_23261/rsi-macd-bollinger-bands-and-volume-based-hybrid-trading-strategy-fb1ecfd58e1b)
- [hftbacktest — OBI market making](https://hftbacktest.readthedocs.io/en/latest/tutorials/Market%20Making%20with%20Alpha%20-%20Order%20Book%20Imbalance.html)
- [blockchain-council — Backtesting AI safely](https://www.blockchain-council.org/cryptocurrency/backtesting-ai-crypto-trading-strategies-avoiding-overfitting-lookahead-bias-data-leakage/)
- [Kelly Criterion fractional](https://cryptogambling.com/guides/sports-betting/fractional-kelly-practical)
- [Kelly Criterion vs Fixed Fractional](https://medium.com/@tmapendembe_28659/kelly-criterion-vs-fixed-fractional-which-risk-model-maximizes-long-term-growth-972ecb606e6c)
- [BTC/USD1 flash crash analysis — cryptopotato](https://cryptopotato.com/bitcoin-didnt-crash-to-24k-binance-wick-on-illiquid-pair-explained/)
- [Revisiting Bitcoin Basis (funding rate) — CFB](https://www.cfbenchmarks.com/blog/revisiting-the-bitcoin-basis-how-momentum-sentiment-impact-the-structural-drivers-of-basis-activity)
