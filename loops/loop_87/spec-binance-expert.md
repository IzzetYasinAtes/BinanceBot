# Loop 87 Binance Expert Spec 2026-05-02

## 1. Post-Mortem: 6 Yeni Param Emit Analizi

L85/L86 yeni param 6/6 emit kotu baslangic. Ort -0.36 USD/trade.

L85 Emitler (Hard-gate OFF doneminde):
- XRP-2: hold 5min, peak=0, SL -0.709 USD. Entry hemen tersine, momentum sifir.
- SOL: hold 26min, peak=0, SL -0.712 USD. 5m signal; sonraki 5 bar aleyhe.
- XRP-3: hold 11min, peak +0.42pct, BE-stop -0.171 USD. BE sigmadi.

L86 Emitler (Hard-gate ON ile bile):
- ADA: hold 30min+, peak negatif, -0.31 USD. VolumeSurgeGate gecti ama 5m breakout revert.
- SOL: hold 29min+, peak negatif, -0.16 USD. 5m kanal kiriyor, 15-30m yon tersine.
- BTC: hold 20min+, peak negatif, -0.17 USD. Buyuk TF downtrend icinde 5m entry.

L84 Carryover Karsilastirma:
- ETH: 329min hold, peak +1.08pct, +0.856 USD
- BTC: 333min hold, peak +0.89pct, +0.653 USD
Carryover 5h+ tutuldu, buyuk TF trend yakaladi.
Yeni param 5m sinyal buyuk TF yonune aykiri girdi.

Analiz Sonucu:
- XRP/ADA/SOL 4/4 = yuzde 100 SL.
- BTC L86 yeni emit -0.17 USD: buyuk coin de kurtarmiyor. Sorun coin ozelligi degil.
- Kok neden: buyuk TF yon onayi eksik. 5m sinyal gurultusu.
- Hard-gate gerekli ama yeterli degil.

---

## 2. Senaryo Karsilastirmasi

A. Multi-Timeframe Confirmation (5m pattern + 15m EMA slope)

Nasil:
  BarSnapshot-a Ema21_15m ve Ema21Prev5_15m eklenir.
  MarketIndicatorService 15m buffer (200 bar = 50h tarih) tutar.
  PatternCompositeEvaluator emit oncesi slope15m > 0 kontrolu yapar.

Binance API (dogrulandi):
  REST GET /api/v3/klines?interval=15m&limit=200
  Weight=2/istek; 5 coin warmup=10 weight (limit 1200/dk sorunsuz).
  WS stream btcusdt@kline_15m destekli (format: symbol@kline_interval).
  Bar kapanis x=true; 2000ms guncelleme; +5 stream toplam ~10 (max 1024).

Trade-off:
  Filtre: GUCLU (15m EMA makro yonu temsil eder).
  Frekans: trend donuslerinde 3-6 bar gecikme beklenir.
  Kod: orta karmasiklik (3 dosya).

Kirmizi bayrak: 15m slope pozitif olmasi 5m lokale reversal-i tam engellemez.
Makro yonu dogrular; tek basina garantili degil.

B. 1-Bar Momentum Onay
Kirmizi bayrak: IStrategyEvaluator stateful olmali. Clean Architecture ihlali. TERCIH EDILMIYOR.

C. Per-Coin Enable/Disable
Kirmizi bayrak: CLAUDE.md kural 12 - 5 coin minimum. YASAK.

D. RSI Filtre (RSI > 75 skip)
Nasil: snapshot.Rsi14 > options.RsiMaxEmit ise skip (75 default).
Trade-off: asiri alim breakout eler; minimal frekans etkisi; minimal kod.
Kirmizi bayrak: Muhtemelen RSI 50-65 bandinda (ani breakout). Tek basina yetersiz.

E. 5m to 15m Timeframe
Kirmizi bayrak: Saatlik emit ~15 e duser (kural 30+). Scalping biter. YASAK.

---

## 3. Onerilen Senaryo: A + D Kombine

MTF Confirmation + RSI Cap birlikte uygulanir.

Neden A secildi:
- Peak=0 koku: 5m sinyal buyuk TF yon tersine giris.
- L84 carryover vs yeni param farki bu hipotezi guclendiriyor.
- 15m klines REST+WS Binance destekli; ekstra altyapi minimal.
- 5m buffer dokunulmaz; 15m buffer paralel eklenir.

Neden D:
- Minimal degisiklik, ek guvenlik katmani.
- RSI 75+ breakout daha yuksek revert riski.

Frekans riski: MTF filtre yuzde 30-40 emit dususu beklenir.
Yeterli kalmazsa: RequiredScore 3-e indir veya CooldownBarsAfterSignal=1.

---

## 4. Kod Degisikligi

4.1 KlineInterval Enum
    FifteenMinutes degeri ekle (yoksa).

4.2 MarketIndicatorService
    Dosya: src/Infrastructure/Strategies/Indicators/MarketIndicatorService.cs
    - SymbolState-e FifteenMinute buffer (IndicatorRollingBuffer capacity=200)
    - WarmupAsync: her symbol icin KlineInterval.FifteenMinutes warmup
    - RunAsync consumer: FifteenMinutes payload FifteenMinute.Upsert

4.3 BarSnapshot
    Dosya: src/Application/Strategies/Patterns/BarSnapshot.cs
    - Ema21_15m (decimal): 15m EMA21 son bar
    - Ema21Prev5_15m (decimal): 15m EMA21 5 bar once (slope icin)

4.4 PatternCompositeEvaluator
    Dosya: src/Infrastructure/Strategies/Evaluators/PatternCompositeEvaluator.cs
    Snapshot null kontrolunden sonra, composer.Compose oncesi:
    var slope15m = snapshot.Ema21_15m - snapshot.Ema21Prev5_15m;
    if (slope15m <= 0m) return null; // MTF gate
    if (snapshot.Rsi14 > options.RsiMaxEmit) return null; // RSI cap

4.5 PatternComposerOptions
    Dosya: src/Application/Strategies/Patterns/PatternComposerOptions.cs
    Yeni: public decimal RsiMaxEmit { get; set; } = 75m;

4.6 appsettings.json
    - KlineIntervals: ["5m","15m"]
    - Her strategy ParametersJson-a "RsiMaxEmit":75 ekle
    - RequiredScore: 4 (degismez)

---

## 5. Loop 87 KPI

Yeni emit peak=0 orani: hedef < yuzde 30 (mevcut yuzde 83)
Ort hold suresi yeni emit: hedef > 15min (mevcut ~10min)
BE armed orani yeni emit: hedef > yuzde 40 (mevcut yuzde 0)
Realized 4h: hedef >= 0 USD (mevcut -0.17 USD)
WR yeni param: hedef >= yuzde 35 (mevcut yuzde 0)
Saatlik emit: hedef >= 5 (mevcut ~2-3/h)
Ardisik SL: hedef < 4 (L85 CB tetigi 3 ile)

Halt Kriterleri:
- Realized < -1.50 USD: halt + Loop 88
- 0 emit 90dk+: MTF filtre cok kati; RsiMaxEmit 80 yap veya slope esigi -0.0002 indir
- 4+ ardisik SL: spec revize

---

## 6. Teknik Dogrulama (Binance API)

REST 15m klines:
- GET /api/v3/klines?interval=15m&limit=200
- Weight 2/istek; 5 coin = 10 weight (limit 1200/dk sorunsuz)
- 200 bar = 50 saat tarih
- Desteklenen interval-lar: 1s 1m 3m 5m 15m 30m 1h 2h 4h 6h 8h 12h 1d 3d 1w 1M

WebSocket 15m klines:
- Stream btcusdt@kline_15m (format: symbol@kline_interval)
- Bar kapanis x=true (mevcut consumer zaten bu kontrolu yapiyor)
- Update sikligi 2000ms (1s haric tum TF-ler icin)
- Max 1024 stream/connection; +5 stream toplam ~10; sorunsuz
- Connection omru 24h; server ping 20s (WsPingIntervalMs:20000 uyumlu)

Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/web-socket-streams.md
Kaynak: https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md
