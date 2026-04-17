# Binance Spot Paper-Fill Simülasyonu — Araştırma

**Amaç:** Paper mode için Binance Spot execution davranışını birebir modelleyecek `PaperFillSimulator`'ı yazacak backend-dev'e referans. Kaynak olarak Binance resmi docs + doğrulanmış endüstri standardı davranışları.

---

## 1. Komisyon (Fee)

### VIP 0 Tier Oranları

| Tier | Maker | Taker |
|------|-------|-------|
| Regular User (VIP 0) | 0.100% | 0.100% |
| VIP 0 + BNB ile ödeme | 0.075% | 0.075% |

BNB fee discount: %25 indirim (0.10 × 0.75 = 0.075). Koşul: 30 günlük hacim < 1M USD ve BNB bakiyesi ≥ 0.

Kaynak: https://www.binance.com/en/fee/schedule

### Commission Asset Kuralı

**Doğrulanmış davranış** (doküman birebir yazmıyor, fills array örnekleri ve test order response'ları gösteriyor):

- **MARKET/LIMIT BUY** (ör. BTCUSDT): `commissionAsset = BTC` (base). Örnek: 1 BTC al → 0.001 BTC komisyon → 0.999 BTC net gelir.
- **MARKET/LIMIT SELL** (ör. BTCUSDT): `commissionAsset = USDT` (quote). Örnek: 1 BTC sat → 30,000 USDT gelmesi gerekirken 29,970 USDT net.
- **BNB discount aktif**: `commissionAsset = BNB`, miktar %25 daha az. Her iki yönde de BNB'den kesilir.

**DİKKAT:** Doküman bu kuralı sözcük sözcük yazmıyor. Gerçek testnet order ile `/api/v3/myTrades` doğrulaması önerilir.

Kaynak: https://developers.binance.com/docs/binance-spot-api-docs/rest-api/trading-endpoints

---

## 2. Order Tipi Fill Kuralları

### 2.1 MARKET BUY / SELL

- **Her zaman taker** — maker fee uygulanmaz.
- İki parametre alternatifi:
  - `quantity`: base asset miktarı (ör. "0.5 BTC al")
  - `quoteOrderQty`: quote asset miktarı (ör. "15,000 USDT harcayarak BTC al"). Engine LOT_SIZE'a göre base qty'yi yuvarlar.
- Order book davranışı: ask tarafını en düşük fiyattan başlayarak tüketir, multi-price fill olabilir, her level ayrı fill kaydı.
- Partial fill: kitap boşalırsa kalan kısım `EXPIRED`. Likit piyasada nadir.

Kaynak: https://developers.binance.com/docs/binance-spot-api-docs/rest-api/trading-endpoints#new-order--trade

### 2.2 LIMIT GTC

`price` + `timeInForce=GTC` zorunlu.

**Spread crossing testi:**
- BUY `price >= bestAsk` → **taker** immediate fill
- BUY `price < bestAsk` → book'a yazar → **maker** bekler
- SELL `price <= bestBid` → **taker** immediate fill
- SELL `price > bestBid` → book'a yazar → **maker** bekler

GTC iptal edilene kadar açık.

### 2.3 LIMIT_MAKER (Post-Only)

Crossing tespit edilirse → **REJECT**, hata `-2010 NEW_ORDER_REJECTED` "Order would immediately match and take."

Crossing yoksa → book'ta maker, her zaman maker fee.

### 2.4 LIMIT IOC (Immediate Or Cancel)

- Mevcut karşı likiditeyle fill (taker), kalan anında `EXPIRED`.
- Partial fill mümkün: `PARTIALLY_FILLED` → `EXPIRED`.
- Hiç fill olmazsa: doğrudan `EXPIRED`.

### 2.5 LIMIT FOK (Fill or Kill)

- Tamamı fill edilebiliyorsa full taker fill.
- Tamamı olmazsa → hiç fill olmaz, `EXPIRED`.
- **Kesinlikle kısmi fill yok.**

### 2.6 STOP_LOSS / TAKE_PROFIT (Market-based)

`stopPrice` tetiklenene kadar bekler (status NEW, matching'de değil). Tetiklenince **MARKET** order olarak çalışır (taker).

- STOP_LOSS BUY: `lastPrice >= stopPrice`
- STOP_LOSS SELL: `lastPrice <= stopPrice`
- TAKE_PROFIT BUY: `lastPrice <= stopPrice`
- TAKE_PROFIT SELL: `lastPrice >= stopPrice`

### 2.7 STOP_LOSS_LIMIT / TAKE_PROFIT_LIMIT

Tetiklenince LIMIT order olarak book'a girer, sonrası LIMIT GTC/IOC/FOK kurallarına tabi.

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/enums.md

---

## 3. Slippage Hesaplama

### MARKET Order Avg Fill Price

Asks (en düşükten):
```
L1: 30,000.00  qty=0.5
L2: 30,001.00  qty=0.3
L3: 30,002.50  qty=0.8
```
1 BTC MARKET BUY:
- L1: 0.5 × 30,000.00 = 15,000.00
- L2: 0.3 × 30,001.00 =  9,000.30
- L3: 0.2 × 30,002.50 =  6,000.50
- Toplam quote: 30,000.80 USDT → avgFillPrice = 30,000.80
- Slippage = 0.80 USDT (%0.00267)

### Yetersiz Likidite

MARKET'ta depth bitince kalan qty `EXPIRED`. `PARTIALLY_FILLED` → `EXPIRED` status zinciri.

### BookTicker Sınırı

`GET /api/v3/ticker/bookTicker` yalnızca **en iyi bid/ask + qty**. Tek level. Slippage için **depth snapshot** (`GET /api/v3/depth`) gerekli (bizim `OrderBookSnapshot` tablomuz bunu tutuyor).

---

## 4. Filtreler

| Filter | Formül | Notlar |
|---|---|---|
| `LOT_SIZE` | `minQty <= qty <= maxQty, (qty-minQty) % stepSize == 0` | Tüm order'lar |
| `MARKET_LOT_SIZE` | Ayrı minQty/maxQty/stepSize | Sadece MARKET |
| `PRICE_FILTER` | `minPrice <= price <= maxPrice, (price-minPrice) % tickSize == 0` | tickSize=0 devre dışı; MARKET'a uygulanmaz |
| `MIN_NOTIONAL` | `price * qty >= minNotional` | `applyToMarket` flag var |
| `NOTIONAL` (yeni) | `minNotional <= price*qty <= maxNotional` | `applyMinToMarket`, `applyMaxToMarket` |

`quoteOrderQty` kullanıldığında engine stepSize'a **aşağı yuvarlar** (ceiling değil — fazla qty filter ihlali olur).

Kaynak: https://developers.binance.com/docs/binance-spot-api-docs/filters

---

## 5. Self-Trade Prevention (STP)

Tek hesap paper simülasyonunda nadiren tetiklenir. Multi-strateji senaryosunda ilgili olabilir.

| Mode | Davranış |
|---|---|
| `NONE` | STP kapalı |
| `EXPIRE_TAKER` | Taker expire, maker kalır |
| `EXPIRE_MAKER` | Maker expire, taker devam |
| `EXPIRE_BOTH` | İkisi de expire |
| `DECREMENT` | Qty'ler karşılıklı azalır |

Tetiklenirse status: `EXPIRED_IN_MATCH`.

**Paper sim öneri:** default `NONE`, ileride gerekirse `EXPIRE_TAKER`.

---

## 6. `/api/v3/order/test` Endpoint

> "Creates and validates a new order but does not send it into the matching engine."

Fill olmaz, bakiye değişmez. Sadece parametre + filter validasyonu.

### Response

Default (`computeCommissionRates=false`): `{}`.

`computeCommissionRates=true` → effective maker/taker rate + BNB discount status döner. Ancak fills array **kesinlikle yok** — virtual fill için kendi mantığımız zorunlu.

### Error

`HTTP 400` + `{"code": -1013, "msg": "Filter failure: LOT_SIZE"}` formatı.

Kaynak: https://developers.binance.com/docs/binance-spot-api-docs/rest-api/trading-endpoints#test-new-order-trade

---

## 7. BookTicker vs Depth for Fill Price

| Order | Fiyat kaynağı | Açıklama |
|---|---|---|
| MARKET BUY | `askPrice` (bestAsk) + depth walking | Slippage var |
| MARKET SELL | `bidPrice` (bestBid) + depth walking | Slippage var |
| LIMIT taker | LIMIT price ya da bestBid/Ask (hangisi daha iyi) | |
| LIMIT maker | LIMIT price, book'ta bekler | Fill tetiği için future trade simülasyonu gerek |

BookTicker tek level → basit fill yaklaşımı (slippage=0) yeterli değil. `OrderBookSnapshot` üzerinden level walking önerilir.

---

## Simülasyon Pseudo-Algoritması (PaperFillSimulator.cs için)

```
FUNCTION SimulateFill(order, bookTicker, depthSnapshot, instrumentFilters, now):

  # ADIM 1: FILTER VALIDATION
  IF order.Type != MARKET AND order.Price != null:
    validate PRICE_FILTER (tickSize, min/maxPrice) → reject if fail
  validate LOT_SIZE (stepSize, min/maxQty) → reject if fail
  IF order.Type == MARKET:
    validate MARKET_LOT_SIZE as well
  validate MIN_NOTIONAL / NOTIONAL (applyToMarket flag'e dikkat) → reject if fail

  # ADIM 2: ORDER TYPE ROUTING
  CASE MARKET:
    goto MARKET_FILL
  CASE LIMIT:
    isCrossing = (side==BUY  && price >= bestAsk)
              OR (side==SELL && price <= bestBid)
    IF NOT isCrossing:
      order.status = NEW (maker)
      return (bekleyen)
    ELSE:
      fillPrice = limitPrice  # veya bestAsk/bid — hangisi daha iyi
      goto TAKER_FILL_AT_PRICE
  CASE LIMIT_MAKER:
    IF isCrossing: order.Reject("-2010 would immediately match"); return
    ELSE: order.status = NEW (maker); return
  CASE STOP_LOSS / TAKE_PROFIT:
    IF NOT triggered (lastPrice vs stopPrice): return (bekle)
    ELSE: goto MARKET_FILL
  CASE STOP_LOSS_LIMIT / TAKE_PROFIT_LIMIT:
    IF NOT triggered: return
    ELSE: create LIMIT order, goto LIMIT routing

  # ADIM 3: MARKET_FILL (DEPTH WALKING)
  levels = (side==BUY) ? depthSnapshot.Asks asc : depthSnapshot.Bids desc
  remainingQty = order.Quantity
  fills = []
  FOR level IN levels WHILE remainingQty > 0:
    fillQty = MIN(level.Qty, remainingQty)
    fills.Add({price: level.Price, qty: fillQty})
    remainingQty -= fillQty

  IF remainingQty > 0:
    IF timeInForce == FOK:
      order.Expire()  # hiç fill yok
      return
    # Partial fill, sonra expired
    ApplyFills(order, fills, now, isMaker=false)
    order.Expire()
    return

  ApplyFills(order, fills, now, isMaker=(LIMIT and NOT crossing))

  # ADIM 4: TIME-IN-FORCE (IOC/FOK LIMIT için)
  # IOC: cross olan kısım doldu, kalan expire (yukarıdaki loop zaten bunu yapıyor)
  # FOK: yetmediyse yukarıda expire edildi

  # ADIM 5: COMMISSION
  FOR each fill IN fills:
    feeRate = 0.001m                   # VIP0, maker veya taker fark etmez
    IF bnbDiscount: feeRate *= 0.75m   # 0.075%
    IF side == BUY:
      commissionAsset = baseAsset      # "BTC"
      commission = fill.Qty * feeRate  # base'den kes
    ELSE:  # SELL
      commissionAsset = quoteAsset     # "USDT"
      commission = fill.Price * fill.Qty * feeRate  # quote'tan kes
    IF bnbDiscount:
      commissionAsset = "BNB"
      commission = (fill.Price * fill.Qty * feeRate) / bnbUsdtPrice

    order.RegisterFill(
      exchangeTradeId: ++virtualTradeCounter,
      price: fill.price,
      quantity: fill.qty,
      commission: commission,
      commissionAsset: commissionAsset,
      filledAt: now)

  # ADIM 6: avgFillPrice = order.CumulativeQuoteQty / order.ExecutedQuantity
```

---

## Kırmızı Bayraklar

1. **BookTicker ile slippage:** BookTicker tek level → slippage=0. `OrderBookSnapshot` depth tablosu kullanılmalı.
2. **Commission asset belirsizliği:** BUY=base, SELL=quote kuralı doğrulanmış ama testnet order ile myTrades validate etmek ideal.
3. **STOP_LOSS trigger lastPrice:** Kline close mu, trade tick mi? Bizde tick yok → kline close ile trigger; bu gecikme simülasyonda yansır.
4. **FOK + yetersiz depth:** fills array oluşturulmamalı; direkt `Expire`.
5. **MIN_NOTIONAL applyToMarket:** flag false ise MARKET'a uygulama, true ise uygula.
6. **quoteOrderQty + LOT_SIZE:** stepSize'a **floor** (aşağı yuvarla).

---

## Kaynaklar

- https://developers.binance.com/docs/binance-spot-api-docs/rest-api/trading-endpoints
- https://developers.binance.com/docs/binance-spot-api-docs/filters
- https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/enums.md
- https://www.binance.com/en/fee/schedule
- https://developers.binance.com/docs/binance-spot-api-docs/rest-api/market-data-endpoints
