# Paper vs Live Binance Audit — Loop 84

Tarih: 2026-05-02 | Agent: binance-expert

---

## 1. Mevcut Paper Davranış Özeti

### Fill Mekanizması (PaperFillSimulator.cs)
- Order tipi yönlendirmesi: sadece MARKET ve crossing-LIMIT fill edilir. STOP_LOSS / STOP_LOSS_LIMIT / TAKE_PROFIT anında reject ("unsupported_type").
- Depth walk: OrderBookSnapshot.AsksJson / BidsJson JSON parse, seviyeler ascending/descending sort, kalan miktar tükenene kadar itere. Depth yoksa bookTicker tek seviyeyle fallback.
- Slippage: her fill leg FixedSlippagePct=0.0001 (%0.01, 1bp). BUY price*(1+pct), SELL price*(1-pct).
- Komisyon: PaperFeeSimulator.CalculateCommission(notional, bnbDiscount). Normal: %0.10, BNB indirimli: %0.075. appsettings UseBnbFeeDiscount:true ile paper %0.075 kullanıyor.
- Simüle gecikme: SimulatedLatencyMs=100ms (Task.Delay).
- Filter validation: LOT_SIZE (minQty, maxQty, stepSize) + PRICE_FILTER (tickSize) + MIN_NOTIONAL. MARKET_LOT_SIZE filtresi eksik.

### SL Tetikleme (StopLossMonitorService.cs)
- Polling: her 30 saniye.
- Trigger: bookTicker bid (Long için) / ask (Short için) <= StopPrice esigi.
- Veri kaynagi: DB BookTickers tablosu (WS tickinden son yazilan deger).
- Kapalis: CloseSignalPositionCommand → MARKET reverse order → PaperFillSimulator.

### TP Tetikleme (TakeProfitMonitorService.cs)
- Polling: her 30 saniye.
- Trigger: bookTicker bid (Long) >= TakeProfit.
- SL ile simetrik mimari.

### Break-Even + Trailing (MarkToMarketWorker.cs)
- Polling: her 30 saniye.
- Mid-price = (bid + ask) / 2 ile MarkToMarket.
- BE trigger: entry*(1+TriggerPct=0.002) asilinca SL = entry*(1+OffsetPct=0.002).
- Trailing: BE sonrasi aktif, peak*(1-TrailPct=0.0015) altina dusunce exit.
- Peak guncelleme: 30s tick arasi tepe kaybolabilir.

---

## 2. Live Binance ile Karsilastirma Tablosu

| Konu | Paper (Mevcut) | Live Binance Gercegi | Uyumlu mu? |
|---|---|---|---|
| Komisyon orani | %0.075 (BNB indirimli, config true) | VIP0 taker: %0.10; BNB indirimiyle %0.075 | HAYIR — live BNB bakiyesi ve toggle sarti var |
| Testnet komisyonu | Simulasyon ile override | Testnet response commission=0 (dev.binance.vision #16810) | EVET — paper dogru simule ediyor |
| Slippage modeli | Sabit 1bp / leg | Book-walk ile gercek depth; BTC spread 0.01-1.58bp, spike 5-20bp | KISMI — normal piyasada gercekci, spike yetersiz |
| SL trigger fiyati | BookTicker bid (DB, 30s poll) | Last trade price (WS @trade stream) | HAYIR — live last-trade, paper bookTicker bid |
| SL trigger gecikmesi | 30s tick | WS event-driven 50-200ms | HAYIR — 30s vs <200ms |
| TP trigger fiyati | BookTicker bid (DB, 30s poll) | Last trade price veya bid >= TP | HAYIR — ayni sorun |
| TP trigger gecikmesi | 30s tick | WS event-driven 50-200ms | HAYIR — 30s vs <200ms |
| BE / Trail mark fiyati | Mid-price (bid+ask)/2, 30s | Gercek last-trade; WS surekli push | KISMI — mid iyi proxy ama tick araligi sorun |
| Trailing stop konumu | Lokal hesap (PeakMarkPrice DB) | Spot exchange-side trailing yok; lokal esit | EVET — spot mimari dogru |
| MARKET_LOT_SIZE filtresi | Eksik (sadece LOT_SIZE) | MARKET emirleri icin ayri minQty/step | HAYIR — eksik |
| Order tipi STOP_LOSS | Paper reject (unsupported_type) | Mainnet STOP_LOSS — last-trade trigger, MARKET fill | KASITLI FARK — lokal SL monitoru kullaniliyor |
| WS fill latency | 100ms Task.Delay | REST 80-120ms; WS fill notice 50-200ms | EVET — makul tahmin |
| BNB discount sarti | appsettings statik toggle | BNB bakiyesi > 0 + Binance hesap ayari acik | HAYIR — live dinamik kontrol yok |
| MIN_NOTIONAL (MARKET) | applyToMarket varsayimi ile sabit esik | NOTIONAL filtresi applyMinToMarket bayragina bagli; sembol bazli | KISMI — dinamik degil |
| MaxHold (timestop) | StopLossMonitorService icinde | Mainnet blocker var, paper/testnet aktif | EVET — tasarim dogru |

---

## 3. Eksik / Yanlis Yerler (Oncelik Sirasi)

### Oncelik 1 — Kritik

P1-A: SL/TP Tick Gecikmesi 30s
- Paper SL ve TP her 30s kontrol edilir.
- Volatil hamlede fiyat 30s icinde %0.5-1 hareket edebilir.
- Live exchange kendi SL triggerini ms cinsinden ateesler.
- Sonuc: Paper SL daha gec = gercek kayip buyur; TP daha gec = gercek kar kucuk.
- Duzeltme: Tick 30s -> 5s (en az).

P1-B: Komisyon Toggle Kontrol Eksikligi
- appsettings UseBnbFeeDiscount:true -> paper %0.075 uygular.
- Live BNB yoksa veya toggle kapali ise %0.10.
- 5 coin * 30 islem/h * 2 leg = 300 leg/h; %0.025 fark * 300 = notionalin %7.5 ekstra fee/h.
- Duzeltme: Conservative mod -> UseBnbFeeDiscount:false.

### Oncelik 2 — Onemli

P2-A: Slippage Sabit 1bp — Spike Ortulmuyor
- 1bp BTC normal piyasada gercekci.
- Haber/likidite sokunda bid-ask 10-50bp; book-walk 2-5 seviye yenir.
- Duzeltme: 5bp sabit (worst-case) veya volatilite bazli dinamik.

P2-B: SL Trigger Fiyati Farki (bid vs last-trade)
- Paper bid kullanir; live last-trade kullanir.
- Mevcut bid yaklasimi muhafazakar (daha kotu senaryoyu simule eder); kabul edilebilir.
- Duzeltme: Dokumante edilmesi yeterli, degistirme gerekmiyor.

### Oncelik 3 — Kucuk

P3-A: MARKET_LOT_SIZE Filtresi Eksik
- ValidateFilters LOT_SIZE var, MARKET_LOT_SIZE yok.
- Paper gecen MARKET order live reject alabilir.

P3-B: Trailing Peak Update Araligi
- 30s arasi tepe kayit disi; 5s ile daha dogru peak.

---

## 4. Onerilen Degisiklikler

### A. appsettings.json — Hemen Yapilabilir

PaperFill bolumu:
- FixedSlippagePct: 0.0005  (5bp, spike hedge)
- SimulatedLatencyMs: 120   (mainnet orta deger)
- UseBnbFeeDiscount: false  (konservatif, %0.10 tam fee)

### B. StopLossMonitorService.cs + TakeProfitMonitorService.cs

TickInterval = TimeSpan.FromSeconds(30) --> TimeSpan.FromSeconds(5)

Etki: WS push tam simule edilmez ama 6x daha iyi yaklasim. CPU/DB maliyeti dusuk.

### C. MarkToMarketWorker.cs

Cycle = TimeSpan.FromSeconds(30) --> TimeSpan.FromSeconds(5)

Trailing peak update ve BE move gecikmesi azalir.

### D. PaperFillSimulator.cs — MARKET_LOT_SIZE

ValidateFilters icine Instrument.MarketMinQty / MarketStepSize kontrol eklenecek.
Instrument entity bu alanlari destekliyor mu backend-dev kontrol etmeli.

---

## 5. Sermaye Koruma: WS Fill Latency + Slippage Kritik Notlar

WS Fill Latency:
- Mainnet WS fill notice: 50-200ms.
- Paper SimulatedLatencyMs=100ms: bu araligda, makul.
- Ancak SL/TP exit gecikmesi 30s tick — burst piyasada asil zarar buradan.

Slippage:
- Depth-walk mantigi dogru; sorun slippage faktorunun dusuk olmasi.
- BTC 1bp gercekci; XRP/ADA daha dusuk likidite = 5-10bp spread normal.
- Oneri: minimum 5bp konservatif sabit veya sembol bazli config.

BNB Discount Riski:
- Live hesapta BNB yokken paper %0.075 ile test edilirse, %0.025 fark stratejinin karliligini bozar.
- 5 coin * 30 islem/h * 2 leg = 300 leg/h.
- Kucuk pozisyonlarda fee orani belirleyici hale gelir.

Exchange-Side SL/TP:
- Live STOP_LOSS_LIMIT exchange icinde tutulur, ms gecikmeli last-trade trigger.
- Mevcut mimari lokal SL monitoru — ADR-0012/0013 bilincli erteleme.
- Live gecisin en buyuk riski: 30s poll SL kacirirsa stop cok otesinde fill.
- 5s tick bu riski azaltir ama exchange-side kadar guvenilir olmaz.
- Uzun vadeli hedef: exchange-side STOP_LOSS_LIMIT entegrasyonu (ADR-0013).

---

Kaynak: https://developers.binance.com/docs/binance-spot-api-docs/filters
Kaynak: https://www.binance.com/en/fee/schedule
Kaynak: https://dev.binance.vision/t/binance-testnet-fees-are-zero/16810
