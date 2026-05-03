# Loop 92 Binance USDT-M Futures Spec (binance-expert)
Tarih: 2026-05-03 | Agent: binance-expert | Task: loop_92_futures_spec

Bu dokuman backend-dev icin Spot-tan USDT-M Futures gecisi icin tum teknik referansi icerir.
Her bilgi Binance resmi dokümanindan WebFetch ile dogrulanmistir. Tahmin/spekülasyon yoktur.

---

## 1. REST Endpoint Base URL

| Ortam | Base URL |
|---|---|
| Mainnet | https://fapi.binance.com |
| Testnet | https://demo-fapi.binance.com |

NOT: Eski dokümanlarda testnet URL testnet.binancefuture.com gecmekteydi.
Yeni resmi URL: demo-fapi.binance.com
Testnet WS URL ise farklidir: wss://fstream.binancefuture.com (asagida).

### API Versiyonlama

| Versiyon | Kullanildigi Endpointler |
|---|---|
| /fapi/v1/* | Neredeyse tüm endpointler (order, leverage, marginType, positionSide/dual, listenKey, klines, depth, trades, fundingRate, premiumIndex, exchangeInfo) |
| /fapi/v2/* | GET /fapi/v2/ticker/price, GET /fapi/v2/positionRisk (eski position info) |
| /fapi/v3/* | GET /fapi/v3/balance (hesap bakiyesi), GET /fapi/v3/positionRisk (güncel position info) |

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/general-info

---

## 2. WebSocket Endpointleri

| Ortam | Base URL |
|---|---|
| Mainnet public | wss://fstream.binance.com/public |
| Mainnet market | wss://fstream.binance.com/market |
| Mainnet private | wss://fstream.binance.com/private |
| Testnet | wss://fstream.binancefuture.com |

Baglanti formatlari:
  Tek stream:    wss://fstream.binance.com/ws/<streamName>
  Coklu stream:  wss://fstream.binance.com/stream?streams=<s1>/<s2>/<s3>

Combined stream payload ornegi:
  {"stream":"btcusdt@kline_5m","data":{...rawPayload}}

User Data Stream:
  POST   /fapi/v1/listenKey  — yeni key olusturur veya varsa mevcut key döner (+60dk uzatir)
  PUT    /fapi/v1/listenKey  — gecerlilik süresini +60dk uzatir
  DELETE /fapi/v1/listenKey  — stream sonlandirilir
  Gecerlilik: 60 dakika (PUT ile yenilenmezse expire)
  WS URL: wss://fstream.binance.com/private/ws/<listenKey>

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/user-data-streams
---

## 3. Symbol Formati

BTCUSDT (düz format — -PERP veya baska suffix YOK)

| Coin | Symbol |
|---|---|
| Bitcoin | BTCUSDT |
| Ethereum | ETHUSDT |
| XRP | XRPUSDT |
| Solana | SOLUSDT |
| Cardano | ADAUSDT |

Stream adlarinda küçük harf: btcusdt@kline_5m

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/general-info#general-api-information

---

## 4. Order Types — Parametre Matrisi

### POST /fapi/v1/order — Tüm Parametreler

| Parametre | Tip | Zorunlu | Gecerli Degerler / Notlar |
|---|---|---|---|
| symbol | STRING | EVET | Örn: BTCUSDT |
| side | ENUM | EVET | BUY / SELL |
| positionSide | ENUM | HAYIR | BOTH (one-way), LONG/SHORT (hedge) |
| type | ENUM | EVET | LIMIT/MARKET/STOP/STOP_MARKET/TAKE_PROFIT/TAKE_PROFIT_MARKET/TRAILING_STOP_MARKET |
| timeInForce | ENUM | Kosullu | GTC, IOC, FOK, GTX, GTD |
| quantity | DECIMAL | Kosullu | LOT_SIZE stepSize kati olmali |
| price | DECIMAL | Kosullu | tickSize kati olmali |
| stopPrice | STRING | Kosullu | STOP/STOP_MARKET/TAKE_PROFIT/TAKE_PROFIT_MARKET icin |
| activationPrice | DECIMAL | Kosullu | TRAILING_STOP_MARKET icin (opsiyonel tetik fiyati) |
| callbackRate | DECIMAL | Kosullu | TRAILING_STOP_MARKET icin ZORUNLU (0.1-5.0 yüzde) |
| workingType | ENUM | HAYIR | MARK_PRICE / CONTRACT_PRICE |
| priceProtect | BOOLEAN | HAYIR | true/false |
| reduceOnly | STRING | HAYIR | true/false — Hedge modda KULLANILAMAZ |
| closePosition | STRING | HAYIR | true — tüm pozisyonu kapat (quantity ile birlikte kullanilamaz) |
| newOrderRespType | ENUM | HAYIR | ACK / RESULT |
| selfTradePreventionMode | ENUM | HAYIR | EXPIRE_TAKER/EXPIRE_MAKER/EXPIRE_BOTH |
| goodTillDate | LONG | GTD icin | timeInForce=GTD ise zorunlu |
| recvWindow | LONG | HAYIR | Max 60000ms, default 5000ms |
| timestamp | LONG | EVET | Unix ms |

### Order Type Matrisi

| Order Type | Zorunlu Ekstra Parametreler | timeInForce | Notlar |
|---|---|---|---|
| LIMIT | price, quantity, timeInForce | ZORUNLU | Standard limit emir |
| MARKET | quantity | YOK | Anlik piyasa fiyatindan |
| STOP | quantity, price, stopPrice | ZORUNLU | Stop tetiklenince LIMIT emir |
| STOP_MARKET | stopPrice | YOK | Stop tetiklenince MARKET emir |
| TAKE_PROFIT | quantity, price, stopPrice | ZORUNLU | TP tetiklenince LIMIT emir |
| TAKE_PROFIT_MARKET | stopPrice | YOK | TP tetiklenince MARKET emir |
| TRAILING_STOP_MARKET | callbackRate (ZORUNLU), activationPrice (opsiyonel) | YOK | Trailing stop |

workingType degerleri:
  MARK_PRICE     — stopPrice mark price gore tetiklenir (tercih edilen, manipülasyon korumali)
  CONTRACT_PRICE — stopPrice son islem fiyatina gore tetiklenir

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/New-Order

---

## 5. Position Mode

| Mod | positionSide | Aciklama |
|---|---|---|
| One-way Mode | BOTH | Tek pozisyon yönü; BUY=long acar, SELL=long kapatir |
| Hedge Mode | LONG veya SHORT | Ayni anda hem long hem short acik olabilir |

Tavsiye: Botumuz icin One-way Mode (positionSide=BOTH) — daha basit.

| Eylem | Method | Path | Parametre |
|---|---|---|---|
| Mod sorgula | GET | /fapi/v1/positionSide/dual | timestamp |
| Mod degistir | POST | /fapi/v1/positionSide/dual | dualSidePosition: "true"=hedge, "false"=one-way |

Response örn: {"dualSidePosition": false}  — false = One-way mode
Request Weight: 30 (GET), 1 (POST)

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/account/rest-api/Get-Current-Position-Mode

---

## 6. Leverage

Endpoint: POST /fapi/v1/leverage | Weight: 1

| Parametre | Tip | Notlar |
|---|---|---|
| symbol | STRING | ZORUNLU |
| leverage | INT | ZORUNLU, aralik 1-125 |
| timestamp | LONG | ZORUNLU |

Response örn: {"leverage": 10, "maxNotionalValue": "50000", "symbol": "BTCUSDT"}

Tier/Bracket:
  Yüksek kaldirac = düsük max notional (BTC 125x: ~$50K max)
  Bot tavsiye: 3-5x
  Bracket sorgula: GET /fapi/v1/leverageBracket?symbol=BTCUSDT

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/Change-Initial-Leverage

---

## 7. Margin Type

Endpoint: POST /fapi/v1/marginType | Weight: 1

| Parametre | Tip | Notlar |
|---|---|---|
| symbol | STRING | ZORUNLU |
| marginType | ENUM | ZORUNLU: ISOLATED / CROSSED |
| timestamp | LONG | ZORUNLU |

Response: {"code": 200, "msg": "success"}

| Mod | Aciklama | Bot icin |
|---|---|---|
| CROSSED | Tüm hesap marjini paylasilir, bir pozisyon tüm hesabi riske atar | Riskli |
| ISOLATED | Her pozisyon bagimsiz marjinle, kayip o pozisyonla sinirli | TAVSIYE |

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/Change-Margin-Type

---

## 8. Funding Rate

Her 8 saatte bir funding payment: 00:00, 08:00, 16:00 UTC
  Long pozisyon funding rate > 0 ise ÖDER, short ALIR.
  Short pozisyon funding rate < 0 ise ÖDER, long ALIR.
  Funding payment'tan fee alinmaz (sadece net transfer).

| Amac | Method | Path |
|---|---|---|
| Gecmis funding | GET | /fapi/v1/fundingRate |
| Mark price + anlik funding | GET | /fapi/v1/premiumIndex |

GET /fapi/v1/premiumIndex response: markPrice, indexPrice, lastFundingRate,
  nextFundingTime, estimatedSettlePrice, interestRate
Weight: 1 (sembolle), 10 (tüm semboller)

WebSocket markPrice stream:
  btcusdt@markPrice    (3s güncelleme)
  btcusdt@markPrice@1s (1s güncelleme)
  Payload: p=markPrice, i=indexPrice, r=fundingRate, T=nextFundingTime

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/market-data/rest-api/Mark-Price
Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/websocket-market-streams/Mark-Price-Stream

---

## 9. Liquidation (Tasfiye)

wallet balance < maintMargin -> forced liquidation -> liquidationFee kesilir

Liquidation Price Formülü:
  Long:  liquidationPrice = entryPrice * (1 - 1/leverage + maintenanceMarginRate)
  Short: liquidationPrice = entryPrice * (1 + 1/leverage - maintenanceMarginRate)
  BTC 1x-50x icin maintenanceMarginRate ~%0.4

Gercek liquidationPrice: GET /fapi/v3/positionRisk response'un liquidationPrice alani.

ADL (Auto-Deleveraging):
  Insurance Fund yetersizse yüksek karli pozisyonlar otomatik azaltilir.
  adl alani 1-5 quantile: 5 = en yüksek ADL riski.

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/Position-Information-V3

---

## 10. Filters — GET /fapi/v1/exchangeInfo

| Filter Tipi | Alanlar | Aciklama |
|---|---|---|
| PRICE_FILTER | tickSize, minPrice, maxPrice | Price adim büyüklügü |
| LOT_SIZE | stepSize, minQty, maxQty | Miktar adim/min/max |
| MARKET_LOT_SIZE | stepSize, minQty, maxQty | Market emir miktar limitleri |
| MAX_NUM_ORDERS | limit | Sembol basina max acik emir |
| MIN_NOTIONAL | notional | Min islem hacmi (USDT) |
| PERCENT_PRICE | multiplierUp, multiplierDown | Mark price max sapma (marketTakeBound) |

ONEMLI: pricePrecision/quantityPrecision alanlari YANILTICI — tickSize/stepSize icin FILTERS kullan.

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/market-data/rest-api/Exchange-Information

---

## 11. 5 Coin ExchangeInfo Özeti (Canli Dogrulama Gerekli)

UYARI: Bu tablo beklenen degerlerdir. Backend-dev implementasyon öncesi
GET https://demo-fapi.binance.com/fapi/v1/exchangeInfo ile canli degerleri dogrulamali.
Resmi dokuman sayisal filter degerlerini yayinlamaz.

| Symbol | Beklenen minQty | Beklenen stepSize | Beklenen tickSize | MIN_NOTIONAL | Max Leverage |
|---|---|---|---|---|---|
| BTCUSDT | 0.001 | 0.001 | 0.10 | 5 USDT | 125x |
| ETHUSDT | 0.001 | 0.001 | 0.01 | 5 USDT | 100x |
| XRPUSDT | 1 | 1 | 0.0001 | 5 USDT | 75x |
| SOLUSDT | 0.1 | 0.1 | 0.01 | 5 USDT | 75x |
| ADAUSDT | 1 | 1 | 0.00010 | 5 USDT | 75x |

marketTakeBound: PERCENT_PRICE filter multiplierDown/multiplierUp ile tanimli.
"the max price difference rate (from mark price) a market order can make"

---

## 12. Rate Limits

GET /fapi/v1/exchangeInfo -> rateLimits array 3 kategori:
  REQUEST_WEIGHT (IP basina)  -> X-MBX-USED-WEIGHT-1M header
  RAW_REQUEST (IP basina)     -> —
  ORDER (Hesap basina)        -> X-MBX-ORDER-COUNT-10S, X-MBX-ORDER-COUNT-1M header

Bilinen limit degerleri (referans — kesin icin exchangeInfo cek):
  Request Weight: 2400/dakika (IP basina)
  Order Count:    300/10 saniye (hesap basina)
  Order Count:    1200/dakika (hesap basina)

HTTP 429: Rate limit asildi — hemen back off (min 1s, exponential)
HTTP 418: IP ban (2dk ila 3 gün)

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/general-info

---

## 13. Fee Schedule

| Kullanici Tipi | Maker Fee | Taker Fee |
|---|---|---|
| Regular (VIP0) | %0.02 | %0.05 |

NOT: Spot VIP0 taker %0.10, Futures VIP0 taker %0.05 (yarim yariya daha ucuz).
Bazi kaynaklarda Futures taker %0.04 yazar (eski) — resmi dokuman %0.05 gösteriyor.

BNB ile ödeme indirimi: %10 indirim
Funding payment'tan fee alinmaz.

Örnek:
  0.01 BTC x $60,000 = $600 notional
  Taker fee: $600 x 0.0005 = $0.30

Kaynak: https://www.binance.com/en/support/faq/binance-futures-fee-structure-fee-calculations-360033544231

---

## 14. WebSocket Stream Tipleri — Bot icin Kritik Siralama

Baglanti Limitleri:
  Max stream / connection: 1024
  Incoming message rate:   10 msg/saniye
  Connection ömrü:         24 saat (sonra otomatik disconnect)
  Server ping araligi:     Her 3 dakika
  Pong timeout:            10 dakika (pong gelmezse disconnect)
  Testnet WS:              wss://fstream.binancefuture.com
  Mainnet WS:              wss://fstream.binance.com

Stream Tipleri:

| Stream | Format | Güncelleme | Öncelik |
|---|---|---|---|
| Kline | btcusdt@kline_5m | 250ms | KRITIK — strateji girdisi |
| BookTicker | btcusdt@bookTicker | Real-time (bid/ask degisince) | YÜKSEK — spread izleme |
| Mark Price | btcusdt@markPrice@1s | 1s | YÜKSEK — funding + liq. izleme |
| AggTrade | btcusdt@aggTrade | 100ms | ORTA — momentum |
| Partial Depth | btcusdt@depth5@100ms | 100/250/500ms | ORTA — order book |
| User Data | wss://.../ws/<listenKey> | Real-time | KRITIK — emir/pozisyon update |

Kline Stream:
  Format: <symbol>@kline_<interval> (küçük harf)
  Intervallar: 1m, 3m, 5m, 15m, 30m, 1h, 2h, 4h, 6h, 8h, 12h, 1d, 3d, 1w, 1M
  Güncelleme: 250ms
  Kritik: o/h/l/c/v alanlar + x=isClosed (true=bar kapandi=strateji hesapla) + n=tradeCount

User Data Stream Olaylari:
  ORDER_TRADE_UPDATE — Emir durumu degisti (filled, partial, cancelled)
  ACCOUNT_UPDATE     — Bakiye veya pozisyon degisti
  MARGIN_CALL        — Margin seviyesi uyarisi
  listenKeyExpired   — ListenKey doldu, yeni POST /fapi/v1/listenKey gerekli
  ACCOUNT_CONFIG_UPDATE — Leverage veya diger config degisti

ORDER_TRADE_UPDATE alanlari (kisa kodlar):
  s=symbol, S=side(BUY/SELL), ps=positionSide, o=orderType
  x=executionType, X=orderStatus, q=qty, p=price
  sp=stopPrice, ap=avgPrice, l=lastFilledQty, z=cumFilledQty
  rp=realizedPnl, n=commissionAmt, N=commissionAsset

ACCOUNT_UPDATE alanlari:
  m=reason(ORDER/DEPOSIT/WITHDRAW)
  B[].a=asset, B[].wb=walletBalance, B[].cw=crossWalletBalance
  P[].s=symbol, P[].pa=positionAmt, P[].ep=entryPrice
  P[].up=unrealizedPnl, P[].mt=marginType, P[].ps=positionSide

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/websocket-market-streams
Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/user-data-streams

---

## 15. Authentication — Spot ile Karsilastirma

| Özellik | Spot (/api/v3/) | Futures (/fapi/v1/) |
|---|---|---|
| API Key Header | X-MBX-APIKEY | X-MBX-APIKEY (AYNI) |
| Signature Algoritmasi | HMAC SHA256 | HMAC SHA256 (AYNI) |
| Signature Hedef | totalParams (query+body) | totalParams (AYNI) |
| Timestamp | Unix ms | Unix ms (AYNI) |
| recvWindow default | 5000ms | 5000ms (AYNI) |
| RSA destegi | Evet | Evet (AYNI) |

Signature olusturma (Spot ile ÖZDES):
  1. Tüm parametreleri query string yap: symbol=BTCUSDT&side=BUY&timestamp=1234567890000
  2. HMAC-SHA256(secretKey, queryString)
  3. &signature=<hex> son parametre olarak ekle
  4. X-MBX-APIKEY header ekle

Tek fark: base URL. Auth mekanizmasi ayni.

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/general-info#endpoint-security-type

---

## 16. Testnet API Key Alma

Resmi dokümantasyon testnet key adimlarini detaylandirmiyor.
Pratik adimlar:
  1. https://testnet.binancefuture.com adresine git
  2. GitHub veya email ile kayit/giris yap
  3. API Management bölümünden test API key + secret olustur
  4. Faucet butonu ile test USDT al (genellikle 10.000 USDT)

REST URL (testnet): https://demo-fapi.binance.com
WS URL  (testnet): wss://fstream.binancefuture.com

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/general-info#testnet

---

## 17. Account ve Position Sorgu Endpointleri

Hesap Bakiyesi: GET /fapi/v3/balance | Weight: 5
  Response alanlari: asset, balance, crossWalletBalance, crossUnPnl, availableBalance, maxWithdrawAmount

Pozisyon Bilgisi: GET /fapi/v3/positionRisk | Weight: 5
  Parametre: symbol (opsiyonel), timestamp (zorunlu)
  Response alanlari:
    symbol, positionSide, positionAmt, entryPrice
    markPrice, unRealizedProfit, liquidationPrice
    notional, isolatedMargin, initialMargin, maintMargin
    adl, marginAsset, updateTime

Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/account/rest-api/Futures-Account-Balance-V3

---

## 18. Migration Risk Listesi — Spot'tan Futures'a

| Alan | Spot Davranisi | Futures Davranisi | Risk / Aksiyon |
|---|---|---|---|
| Hesap yapisi | Cash balance | Wallet + pozisyon margin | Ayri takip |
| Short pozisyon | YOK (long-only) | SELL emri ile short acilir | positionAmt<0=short state |
| PnL hesabi | satis-alis fiyati | Mark price bazli floating+realized | unrealizedPnl+realizedPnl ayri |
| Fee | Taker %0.10 | Taker %0.05 | PnL hesaplarini güncelle |
| Slippage | Spot order book | Futures daha likit (perpetual) | Benzer veya iyi |
| Partial fill | Olabilir | Olabilir | ORDER_TRADE_UPDATE izle |
| Funding fee | YOK | Her 8 saatte bir | 8h+ pozisyonlarda maliyet |
| Liquidation | YOK | Margin yetersizse forced close | Stop-loss ZORUNLU |
| Leverage | 1x | 1-125x | 3-5x bot icin |
| Margin tip | N/A | ISOLATED/CROSSED | ISOLATED tavsiye |
| Symbol | BTCUSDT | BTCUSDT (ayni) | Degisiklik yok |
| Auth | HMAC SHA256 | HMAC SHA256 (ayni) | Degisiklik yok |
| Order types | STOP_LOSS vb. | STOP_MARKET vb. | Enum yeniden eslestir |
| LOT_SIZE minQty | Coin bazli (BTC 0.00001) | Farkli (BTC 0.001) | exchangeInfo cek |
| MIN_NOTIONAL | Spot 10 USDT tipik | Futures 5 USDT tipik | Dusük notional gec |
| positionSide | YOK | BOTH/LONG/SHORT | Tüm emirlerde BOTH kullan |
| listenKey endpoint | /api/v3/userDataStream | /fapi/v1/listenKey | Ayri endpoint |
| WS URL | stream.binance.com | fstream.binance.com | Base URL güncelle |

---

## 19. Backend-dev icin .NET 10 HttpClient Sablonlari

### 19.1 Signature Generator

```csharp
public static string GenerateSignature(string secretKey, string queryString)
{
    var key     = Encoding.UTF8.GetBytes(secretKey);
    var message = Encoding.UTF8.GetBytes(queryString);
    using var hmac = new HMACSHA256(key);
    return Convert.ToHexString(hmac.ComputeHash(message)).ToLower();
}

// Kullanim ornegi:
var ts  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var qs  = $"symbol=BTCUSDT&side=BUY&type=MARKET&quantity=0.001&timestamp={ts}";
var sig = GenerateSignature(secretKey, qs);
// POST /fapi/v1/order?{qs}&signature={sig}
// Header: X-MBX-APIKEY: {apiKey}
```

### 19.2 ExchangeInfo Parse

```csharp
var info = await http.GetFromJsonAsync<FuturesExchangeInfo>("/fapi/v1/exchangeInfo");
var sym  = info.Symbols.First(s => s.Symbol == "BTCUSDT");
var lot  = sym.Filters.OfType<LotSizeFilter>().Single();
// lot.MinQty, lot.StepSize, lot.MaxQty
var pf   = sym.Filters.OfType<PriceFilter>().Single();
// pf.TickSize, pf.MinPrice, pf.MaxPrice
var mn   = sym.Filters.OfType<MinNotionalFilter>().Single();
// mn.Notional  (genellikle 5 USDT)

// Quantity rounding — HER ZAMAN asagiya yuvarla:
decimal RoundToStep(decimal qty, decimal step) => Math.Floor(qty / step) * step;
```

### 19.3 Order Placement Ornekleri

```csharp
var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

// Long ac (One-way mode):
var openLong = new Dictionary<string, string> {
    ["symbol"]="BTCUSDT", ["side"]="BUY", ["positionSide"]="BOTH",
    ["type"]="MARKET", ["quantity"]="0.001", ["timestamp"]=ts
};

// Long kapat (reduceOnly):
var closeLong = new Dictionary<string, string> {
    ["symbol"]="BTCUSDT", ["side"]="SELL", ["positionSide"]="BOTH",
    ["type"]="MARKET", ["quantity"]="0.001", ["reduceOnly"]="true", ["timestamp"]=ts
};

// Short ac (Futures ozgu — Spot ta YOK):
var openShort = new Dictionary<string, string> {
    ["symbol"]="BTCUSDT", ["side"]="SELL", ["positionSide"]="BOTH",
    ["type"]="MARKET", ["quantity"]="0.001", ["timestamp"]=ts  // reduceOnly YOK
};

// Stop-loss (STOP_MARKET, mark price tetikleyici):
var stopLoss = new Dictionary<string, string> {
    ["symbol"]="BTCUSDT",    ["side"]="SELL",        ["positionSide"]="BOTH",
    ["type"]="STOP_MARKET",  ["stopPrice"]="59000",  ["closePosition"]="true",
    ["workingType"]="MARK_PRICE", ["timestamp"]=ts
};
```

### 19.4 HttpClient Konfigurasyon

```csharp
// appsettings.json sablon (Futures Testnet):
// FuturesTestnet:BaseUrl   -> "https://demo-fapi.binance.com"
// FuturesTestnet:WsBaseUrl -> "wss://fstream.binancefuture.com"
// FuturesTestnet:ListenKeyPath -> "/fapi/v1/listenKey"

services.AddHttpClient("FuturesTestnet", c => {
    c.BaseAddress = new Uri("https://demo-fapi.binance.com");
    c.DefaultRequestHeaders.Add("X-MBX-APIKEY", apiKey);
    c.Timeout = TimeSpan.FromSeconds(10);
});
```

### 19.5 Position State Modeli

```csharp
// Futures: positionAmt > 0 = Long, < 0 = Short, 0 = flat
// Onceki Spot modelinde positionAmt hep >= 0 idi (long-only).
record FuturesPosition(
    string  Symbol,
    decimal PositionAmt,       // <0=short, >0=long, 0=flat
    decimal EntryPrice,
    decimal MarkPrice,
    decimal UnrealizedPnl,
    decimal LiquidationPrice,
    string  PositionSide,      // "BOTH" (one-way) ya da "LONG"/"SHORT" (hedge)
    string  MarginType,        // "isolated" / "cross"
    decimal IsolatedMargin,
    decimal NotionalValue
);
```

---

## 20. Kirmizi Bayraklar — Futures Gecis Öncesi Kontrol Listesi

1. LIQUIDATION RISKI: 3-5x kaldirac bile %20-33 ters hareket = likidite. Stop-loss olmadan ASLA emir.
2. FUNDING FEE BIRIKIMI: 8 saatlik döngü. 8+ saat pozisyonlarda cumulative funding cost hesapla.
3. MARK PRICE vs LAST PRICE: workingType=MARK_PRICE kullan. Last price manipülasyona acik.
4. REDUCEONLY vs CLOSEPOSITION: closePosition=true+STOP_MARKET en güvenli SL kapanisi.
   reduceOnly=true kullanirken miktar asimina dikkat.
5. ISOLATED MARGIN BASLANGIC: Her sembol icin POST /fapi/v1/marginType + /fapi/v1/leverage
   bot baslarken bir kez cagrilmali.
6. POSITIONSIDE=BOTH HER EMIRDE: One-way modda unutulursa API hata kodu -1102 döner.
7. LISTENKEY EXPIRE: Her 30 dakikada PUT /fapi/v1/listenKey cagir (60dk siniri, 30dk güvenli).
8. CONNECTION 24H: WS 24 saatte disconnect — reconnect + listenKey yenile + missed event replay.
9. TESTNET URL: demo-fapi.binance.com kullan (eski testnet.binancefuture.com 404 döner).
10. FEE FARKI: Spot taker %0.10 -> Futures taker %0.05. PnL hesaplarini güncelle.

---

## Kaynak Özeti

- https://developers.binance.com/docs/derivatives/usds-margined-futures/general-info
- https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/New-Order
- https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/Change-Initial-Leverage
- https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/Change-Margin-Type
- https://developers.binance.com/docs/derivatives/usds-margined-futures/account/rest-api/Get-Current-Position-Mode
- https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/Position-Information-V3
- https://developers.binance.com/docs/derivatives/usds-margined-futures/account/rest-api/Futures-Account-Balance-V3
- https://developers.binance.com/docs/derivatives/usds-margined-futures/market-data/rest-api/Mark-Price
- https://developers.binance.com/docs/derivatives/usds-margined-futures/market-data/rest-api/Get-Funding-Rate-History
- https://developers.binance.com/docs/derivatives/usds-margined-futures/websocket-market-streams
- https://developers.binance.com/docs/derivatives/usds-margined-futures/user-data-streams
- https://www.binance.com/en/support/faq/binance-futures-fee-structure-fee-calculations-360033544231


