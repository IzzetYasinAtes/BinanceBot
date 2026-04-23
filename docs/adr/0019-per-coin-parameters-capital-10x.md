# 0019. Per-Coin Parameters + Capital 10x + Deprecated Toggle Endpoints

Date: 2026-04-19
Status: Accepted
Supersedes strategy parameter layer of: [ADR-0018 Micro-Scalping 30s VWAP Reclaim](./0018-micro-scalping-30s-vwap-reclaim.md) §18.3 / §18.7 / §18.13
Relates to: [ADR-0011 Equity-Aware Sizing](./0011-equity-aware-sizing-and-risk-tracking.md), [ADR-0017 TimeStop/Duplicate/Sizing](./0017-timestop-mapping-duplicate-protection-sizing.md)

> Bu ADR dört eksende karar verir: (A) **per-coin parametre seti** — 4 `MicroScalperVwapEma30s` seed'i aynı evaluator sınıfını paylaşır ama her sembol kendi TP/SL/MaxHold/VwapTolerance profiliyle çalışır; (B) **sermaye 10x** — paper `VirtualBalance.StartingBalance` $100 → $1000; `EquityFraction = 1%` ve `FloorUsd = 5.10` korunur, ama $10 target sizing floor üstüne oturarak $5.39 avg notional $10'a çıkar; (C) **strateji toggle endpoint'leri deprecated** — `/api/strategies/{id}/activate` + `/deactivate` artık `410 Gone` döner; strateji durumu sadece `appsettings.Seeds[].Activate` üzerinden kod tarafında yönetilir; (D) **fee path doğrulama** — `PaperFillSimulator` + `VirtualBalance.ApplyFill` + `GetPortfolioSummaryQuery` commission zinciri birim testlerle doğrulanır (binance-expert §5 flag). Yeni aggregate, yeni domain event, yeni evaluator sınıfı **yok** — değişiklik yüzeyi config + migration + 2 route deprecation.

## Context

### 19.1 Loop 29 Veri Kanıtı — ETH Taşıyıcı, Diğerleri Küçük

Loop 29 (ADR-0018 parametreleri ilk yumuşatma — `TpGrossPct=0.003`, `StopPct=0.0015`, `MaxHoldMinutes=8`, `SlopeTolerance=-1.0` direction-agnostic, `VolumeMultiplier=0.5`). 4.5 saat canlı paper, 40 trade, 2 sembol aktif (BTC/XRP `Activate=false`):

| Sembol | Trade | W/L | WR | Toplam PnL | Avg PnL | Hold ort |
|---|---|---|---|---|---|---|
| ETH | 22 | 11/11 | %50 | **+$0.051** | +$0.0023 | 394 sn |
| BNB | 18 | 8/10 | %44 | **−$0.003** | −$0.0001 | 474 sn |

Hold süresi dağılımı (40 trade, MaxHold=8 dk = 480 sn):

| Bucket | Trade | Avg PnL | Yorum |
|---|---|---|---|
| 60–180 sn | 3 | −$0.0006 | Erken SL küçük kayıp |
| 180–360 sn | 4 | −$0.0036 | Orta pencere — zararlı |
| 360–480 sn | 4 | +$0.0112 | En karlı bucket (TP tetikleyenler) |
| 480+ sn (MaxHold) | **29** (%72) | +$0.0007 | Çoğu — marjinal; TP erişmiyor |

**Bulgu:** TP %0.30 çok uzak; trade'lerin %72'si MaxHold'a dolaşıp marjinal kapanıyor. BNB MaxHold ort 474 sn — 8 dk sınırında, TP hiç tetiklenmiyor. ETH 394 sn — daha yakın ama aynı sorun.

Sizing tarafı: avg notional $5.39 — `max($100 × 0.01, $5.10) = $5.10`'a LOT_SIZE precision buffer ile $5.39 oturuyor. EV hesabı:

- Saatte ~9 trade × $5.39 × %0.0023 ort PnL (ETH) = $0.0001/saat net — çok küçük.
- 4.5 saatte +$0.051 (ETH) + (−$0.003 BNB) = +$0.048. Bu ölçekte anlamlı kâr imkansız; parametre + sermaye birlikte değişmeli.

### 19.2 Kullanıcı Talimatı (Loop 30 boot)

Birebir kullanıcı notları:
- "stratejileri panelden ben belirlemicem kodlarda belirlenecek aç kapa var kaldır"
- "sizing artır, daha çok kar lazım"
- "her coin farklı strateji çalışsın, 4 coin"
- "UI tablo olsun kart değil, 4 küsürat her yerde"

PM filter: strateji durumu **appsettings-driven**; UI'dan toggle kaldırıldı (frontend-dev Loop 30). Sermaye 10x, per-coin TP/SL, BTC + XRP yeniden aktif.

### 19.3 binance-expert Canlı Veri (Loop 30 research)

Kaynak: [`loops/loop_30/research.md`](../../loops/loop_30/research.md) — canlı `GET /api/v3/exchangeInfo` (2026-04-19).

Kritik kanıtlar:

| Alan | Değer |
|---|---|
| minNotional (BTC/ETH/BNB/XRP hepsi) | 5.00 USD |
| VIP0 fee (spot) | %0.100 maker / %0.100 taker |
| VIP0 + BNB discount | %0.075 maker / %0.075 taker |
| Rate limit ORDERS (10 sn) | 100 → 42 order/saat hedefte **%0.7 kullanım** |
| stepSize BTC | 0.00001 → 10 USD sizing = 0.00013 adım → $9.75 gerçek notional (%2.5 aşağı yuvarlama) |

**Sizing S1 kararı:** Starting balance $100 → $1000. `EquityFraction = 1%` sabit. Target = $1000 × 0.01 = $10 → FloorUsd $5.10 üstünde → **$10 fiili sizing**. Max exposure = 6 × $10 = $60 = equity'nin %6.

**Fee red flag:** 21 trade/saat × $10 × 0.0015 (round-trip %0.15 BNB) = **$0.315/saat fee**. Loop 29 gross kar linear scale edilirse ~$0.25/saat. **Net negatif risk** — backend-dev fee zincirini birim testlerle doğrulamak zorunda (§19.5).

### 19.4 Mevcut Kod Envanteri

Kontrol edildi:

| Kaynak | Durum |
|---|---|
| `StrategyType.MicroScalperVwapEma30s = 2` | VAR (ADR-0018 §18.6) — **değişmez** |
| `MicroScalperVwapEma30sEvaluator` | VAR — `ParametersJson`'u `EvaluatorParameterHelper.TryParse<Parameters>` ile row bazında okuyor (`Evaluators/MicroScalperVwapEma30sEvaluator.cs:57`) |
| `SnowballSizing.FloorUsd = 5.10m`, `EquityFraction = 0.01m` | VAR (ADR-0018 §18.10) — **değişmez** |
| `appsettings.Strategies.Seed[]` | 4 seed VAR (BTC=false, ETH=true, BNB=true, XRP=false) — `ParametersJson` **tüm 4'te aynı** — per-coin ayrışım burada yapılacak |
| `appsettings.Binance.Symbols` | `["BTCUSDT","ETHUSDT","BNBUSDT","XRPUSDT"]` zaten var |
| `appsettings.RiskProfile.Defaults.MaxOpenPositions` | 4 → **6** |
| `appsettings.RiskProfile.Defaults.MaxConsecutiveLosses` | 10 → **8** |
| `PaperFill.UseBnbFeeDiscount` | `true` (ADR-0018 §18.11) |
| `/api/strategies/{id}/activate`, `/deactivate` endpoint'leri | VAR — Loop 30'da deprecated olacak |
| `VirtualBalance` aggregate | VAR — `StartingBalance` kolonu paper seed'de $100 |

**Evaluator sınıfı bölme YAGNI** (alt-C red — §19.10). Aynı evaluator, farklı `ParametersJson`. Zaten böyle çalışıyor, sadece seed'lerdeki JSON farklılaşacak.

## Decision

### 19.5 Per-Coin Parametre Setleri

Her sembolün **ayrı** `ParametersJson` taşır. 4 seed, aynı `Type = MicroScalperVwapEma30s`, farklı payload. Evaluator run-time'da row'un `ParametersJson`'ını parse ediyor (mevcut davranış), kod değişmiyor.

**BE_WR formülü:** `(SL + fee_RT) / (TP + SL + 2 × fee_RT)`. BNB discount `fee_RT = %0.150`.

#### ETH-MicroScalper — Kanıtlanmış, Değişmez

Loop 29'da WR %50, +$0.051 net, 394 sn hold ort. Parametreler zaten iyi, pozitif beklenti kanıtlanmış (WR %50 > BE %40).

| Param | Değer |
|---|---|
| `TpGrossPct` | 0.003 (%0.30) |
| `StopPct` | 0.0015 (%0.15) |
| `MaxHoldMinutes` | **8** |
| `VwapTolerancePct` | 0.005 |
| `VolumeMultiplier` | **0.4** (0.5 → 0.4, +%20 emit) |
| `SlopeTolerance` | **-1.0** (direction-agnostic korun) |
| `VwapWindowBars` | 15 |
| `VolumeSmaBars` | 20 |
| `EmaPeriod` | 20 |
| `KlineInterval`, `EmaTimeframe` | `"1m"` |

**BE_WR:** `(0.15 + 0.15) / (0.30 + 0.15 + 0.30) = 0.30 / 0.75 = %40`. Gerçek WR %50 → EV pozitif.

#### BNB-MicroScalper — Reform (TP Yakın, MaxHold Kısa)

Loop 29'da WR %44, −$0.003 net, 474 sn hold — sınırda dolanıyor. TP %0.30 çok uzak, çoğu trade MaxHold'a dayanıp marjinal kapanıyor. Reform: TP'yi yakınlaştır, SL'i sıkılaştır, MaxHold'u kısalt (hızlı çevrim).

| Param | Değer (Loop 29) | Değer (Loop 30) |
|---|---|---|
| `TpGrossPct` | 0.003 | **0.002** (%0.20) |
| `StopPct` | 0.0015 | **0.0012** (%0.12) |
| `MaxHoldMinutes` | 8 | **5** |
| `VwapTolerancePct` | 0.005 | 0.005 |
| `VolumeMultiplier` | 0.5 | **0.4** |
| `SlopeTolerance` | -1.0 | -1.0 |

**BE_WR:** `(0.12 + 0.15) / (0.20 + 0.12 + 0.30) = 0.27 / 0.62 = %43.5`. Loop 29 WR %44 = BE %43.5 marjinal; TP yakınlaşma ile WR artması beklenir (daha kolay tetiklenir).

#### BTC-MicroScalper — Yeniden Aktif, Konservatif

Loop 27'de WR %30 < BE %42.9 — kanıtlanmış negatif. Sermaye 10x ile slipaj **oranı** değişmez (hâlâ %2.5, research §2), ama daha sık aktiflik gözlemi için yeniden açılır. Parametreler sıkılaştırılır; SymbolCircuitBreaker (§19.7) BTC için 5 ardışık loss → 2 saat pause tetiklenir (henüz implement edilmemiş, ayrı task).

| Param | Değer (Loop 27) | Değer (Loop 30) |
|---|---|---|
| `TpGrossPct` | 0.003 | **0.0025** (%0.25) |
| `StopPct` | 0.0015 | 0.0015 (%0.15) |
| `MaxHoldMinutes` | 8 | **6** |
| `VwapTolerancePct` | 0.005 | **0.004** |
| `VolumeMultiplier` | 0.5 | **0.4** |
| `SlopeTolerance` | -1.0 | -1.0 |

**BE_WR:** `(0.15 + 0.15) / (0.25 + 0.15 + 0.30) = 0.30 / 0.70 = %42.9`. Risk devam ediyor; Loop 30'da ilk 2 saat izlenecek. WR %42.9'un altına düşerse SymbolCircuitBreaker veya seed `Activate=false` ile deaktif.

#### XRP-MicroScalper — Yeniden Aktif, Geniş Parametre

Loop 28'de WR %27 < BE %38.9. XRP volatilitesi yüksek; geniş TP + geniş VwapTolerance + geniş SL kombinasyonu Loop 28'de test edilmemişti.

| Param | Değer (Loop 28) | Değer (Loop 30) |
|---|---|---|
| `TpGrossPct` | 0.003 | **0.004** (%0.40) |
| `StopPct` | 0.0015 | **0.002** (%0.20) |
| `MaxHoldMinutes` | 8 | **7** |
| `VwapTolerancePct` | 0.005 | **0.008** |
| `VolumeMultiplier` | 0.5 | **0.4** |
| `SlopeTolerance` | -1.0 | -1.0 |

**BE_WR:** `(0.20 + 0.15) / (0.40 + 0.20 + 0.30) = 0.35 / 0.90 = %38.9`. Aynı risk — SymbolCB zorunlu.

### 19.6 Sizing — S1 Sermaye 10x

`SnowballSizing.FloorUsd = 5.10m` ve `EquityFraction = 0.01m` **değişmez** (ADR-0018 §18.10 korunur).

Tek değişiklik: paper `VirtualBalance.StartingBalance` $100 → **$1000**.

| Metric | Loop 29 | Loop 30 |
|---|---|---|
| Starting balance (paper) | $100 | **$1000** |
| `equity × 0.01` | $1.00 | $10.00 |
| `FloorUsd` | $5.10 | $5.10 |
| Target notional | $5.10 (floor devrede) | **$10.00** (floor üstünde) |
| Max open positions | 4 | **6** |
| Max exposure | 4 × $5.10 ≈ $20 (%20 equity) | 6 × $10 = $60 (%6 equity) |

**Kartopu eşiği:** ADR-0018 §18.10 tablosu korunur — $1000 equity'de %1 = $10 > $5.10 → kartopu devrede. $5000 equity'de $50 sizing; %1 oransal büyüme doğal çalışır.

**Migration:** `UPDATE VirtualBalances SET StartingBalance = 1000, CurrentBalance = 1000, Equity = 1000, ResetCount = ResetCount + 1 WHERE Mode = 1;` (Mode=1 = Paper).

### 19.7 Global Risk Parametreleri

`appsettings.RiskProfile.Defaults` güncellenir:

| Param | Loop 29 | Loop 30 |
|---|---|---|
| `MaxOpenPositions` | 4 | **6** |
| `MaxConsecutiveLosses` | 10 | **8** (daha erken CB tetik) |
| `RiskPerTradePct` | 0.02 | 0.02 (değişmez) |
| `MaxPositionSizePct` | 0.40 | 0.40 (değişmez) |
| `MaxDrawdown24hPct` | 0.20 | 0.20 (değişmez; $1000 × %20 = $200 bütçe) |

**SymbolCircuitBreaker (BTC + XRP için — YENİ mekanizma, NOT):**

5 ardışık loss → 2 saat pause. Bu mekanizma **şu an kodda yok**. Loop 30 scope dışı; ayrı backend-dev task olarak ele alınacak. Bu ADR sadece **gereksinim** olarak bildirir — BTC/XRP aktif edildiği için risk yönetimi amaçlı planlı. Geçici çözüm: eğer BTC/XRP Loop 30 ilk 2 saatte WR < BE_WR görünürse, ops elle `Activate=false` yapar ve reboot — toggle UI yok, appsettings override.

### 19.8 Strateji Toggle Endpoint'leri — Deprecated

Kullanıcı talimatı: stratejileri UI'dan açıp kapatmayacak, kodda seed düzeyinde belirlenecek.

**Karar:**
- `POST /api/strategies/{id}/activate` → **`410 Gone`** döner; response body: `{ "error": "deprecated", "replacement": "Set appsettings Strategies.Seed[].Activate and restart." }`.
- `POST /api/strategies/{id}/deactivate` → aynı.
- Endpoint kodu **silinmez** (audit/logging devamlılığı, ADR-0015 Loop 19 pattern'i — enum korunur/route korunur, davranış neutralize edilir).
- Swagger/OpenAPI `[Obsolete]` attribute + summary güncellenir.
- Strateji durumu yalnızca `StrategySeeder` boot'ta okur:
  - `Activate = true` → `Status = Active`.
  - `Activate = false` → `Status = Draft` (`MarkDraft()` domain method).
- Frontend toggle butonları Loop 30'da zaten kaldırıldı (frontend-dev); backend 410 cevabı "savunma derinliği" (endpoint doğrudan çağrılırsa bile durum değişmez).

**Alternatif (alt-B red):** Endpoint fiziksel olarak silmek. Red — telemetri/log izi kaybolur, eski client hataları `404` yerine `410` anlamlı mesaj vermez. `410 Gone` semantiği RFC 7231 §6.5.9 gereği "artık yok + geri gelmeyecek" — doğru sinyal.

### 19.9 Fee Zinciri Doğrulama — Birim Test Zorunluluğu

binance-expert (research §5) "fee hesabı çelişkisi" uyarıyor: 21 trade/saat × $10 × 0.0015 = $0.315/saat fee; Loop 29 linear projeksiyon $0.25/saat gross. Pozitif Loop 29 sonucu fee path'te sessiz bypass varsa yanıltıcıdır.

**backend-dev sorumluluğu (Loop 30 task'ı):**

| Katman | Test |
|---|---|
| `PaperFillSimulator` (veya `PaperFillService`) | `CalculateCommission(notional, bnbDiscount)` doğru: BNB=true → notional × 0.00075; BNB=false → notional × 0.001. |
| `VirtualBalance.ApplyFill` | Commission argümanı balance'tan düşer; `Debit(commission)` sonrası `CashBalance`, `Equity`, `TotalCommissionPaid` tutarları beklenen. |
| `PaperFillService` çağrı sırası | `PaperFeeSimulator.CalculateCommission` çağrılıyor, `Orders.Commission` kolonu UPDATE ediliyor, `VirtualBalance.ApplyFill` commission'ı alıyor. |
| `GetPortfolioSummaryQuery` | `TotalCommissionPaid = OrderFills.Sum(Commission)` (ADR-0018 §18.11 fee persistence ile Loop 23 blocker fix sonrası yapılıyor). Integration test: 2 trade açıp kapatınca `TotalCommissionPaid = entry + exit commission`. |

Başarı kriteri: mevcut `PaperFillSimulatorTests`, `VirtualBalanceTests`, `GetPortfolioSummaryQueryTests` suite'lerinde **her dosyada en az 1 yeni test** `bnbDiscount=true` + `false` senaryosu için. Testler yeşilse fee çelişkisi reddedilir; kırmızıysa blocker — Loop 30 halt + backend-dev fix.

### 19.10 Config Değişiklikleri — Backend-Dev Kopya-Uygula

`src/Api/appsettings.json` — **tam örnek:**

```json
{
  "Binance": {
    "Symbols": [ "BTCUSDT", "ETHUSDT", "BNBUSDT", "XRPUSDT" ],
    "KlineIntervals": [ "1m" ],
    "BackfillIntervals": [ "1m" ]
  },
  "PaperFill": {
    "FixedSlippagePct": 0.0001,
    "SimulatedLatencyMs": 100,
    "UseBnbFeeDiscount": true
  },
  "RiskProfile": {
    "Defaults": {
      "RiskPerTradePct": 0.02,
      "MaxPositionSizePct": 0.40,
      "MaxDrawdown24hPct": 0.20,
      "MaxDrawdownAllTimePct": 0.40,
      "MaxConsecutiveLosses": 8,
      "MaxOpenPositions": 6
    }
  },
  "Strategies": {
    "Seed": [
      {
        "Name": "BTC-MicroScalper",
        "Type": "MicroScalperVwapEma30s",
        "Symbols": [ "BTCUSDT" ],
        "ParametersJson": "{\"KlineInterval\":\"1m\",\"EmaTimeframe\":\"1m\",\"EmaPeriod\":20,\"VwapWindowBars\":15,\"VwapTolerancePct\":0.004,\"VolumeSmaBars\":20,\"VolumeMultiplier\":0.4,\"SlopeTolerance\":-1.0,\"TpGrossPct\":0.0025,\"StopPct\":0.0015,\"MaxHoldMinutes\":6}",
        "Activate": true
      },
      {
        "Name": "ETH-MicroScalper",
        "Type": "MicroScalperVwapEma30s",
        "Symbols": [ "ETHUSDT" ],
        "ParametersJson": "{\"KlineInterval\":\"1m\",\"EmaTimeframe\":\"1m\",\"EmaPeriod\":20,\"VwapWindowBars\":15,\"VwapTolerancePct\":0.005,\"VolumeSmaBars\":20,\"VolumeMultiplier\":0.4,\"SlopeTolerance\":-1.0,\"TpGrossPct\":0.003,\"StopPct\":0.0015,\"MaxHoldMinutes\":8}",
        "Activate": true
      },
      {
        "Name": "BNB-MicroScalper",
        "Type": "MicroScalperVwapEma30s",
        "Symbols": [ "BNBUSDT" ],
        "ParametersJson": "{\"KlineInterval\":\"1m\",\"EmaTimeframe\":\"1m\",\"EmaPeriod\":20,\"VwapWindowBars\":15,\"VwapTolerancePct\":0.005,\"VolumeSmaBars\":20,\"VolumeMultiplier\":0.4,\"SlopeTolerance\":-1.0,\"TpGrossPct\":0.002,\"StopPct\":0.0012,\"MaxHoldMinutes\":5}",
        "Activate": true
      },
      {
        "Name": "XRP-MicroScalper",
        "Type": "MicroScalperVwapEma30s",
        "Symbols": [ "XRPUSDT" ],
        "ParametersJson": "{\"KlineInterval\":\"1m\",\"EmaTimeframe\":\"1m\",\"EmaPeriod\":20,\"VwapWindowBars\":15,\"VwapTolerancePct\":0.008,\"VolumeSmaBars\":20,\"VolumeMultiplier\":0.4,\"SlopeTolerance\":-1.0,\"TpGrossPct\":0.004,\"StopPct\":0.002,\"MaxHoldMinutes\":7}",
        "Activate": true
      }
    ]
  }
}
```

**Not:** `KlineInterval` alanı seed JSON'unda `"1m"` kalır (mevcut `MicroScalperVwapEma30sEvaluator` 1m bar'larda çalışıyor, 30s WS stream ADR-0018 §18.11 ertelendi). Sınıf adı ve enum değeri tarihsel olarak `30s` etiketini taşıyor; bu tutarsızlık Loop 30 scope dışı (rename = ayrı ADR + refactor).

### 19.11 Migration — `Loop30CapitalScale10x`

```bash
dotnet ef migrations add Loop30CapitalScale10x --project Infrastructure --startup-project Api
```

Up SQL:

```sql
-- 1. Strategy reset (per-coin yeni ParametersJson basılacak, StrategySeeder idempotent insert)
DELETE FROM StrategySignals;
UPDATE Positions SET StrategyId = NULL WHERE StrategyId IS NOT NULL;
DELETE FROM Strategies;

-- 2. Paper sermaye 10x
UPDATE VirtualBalances
   SET StartingBalance = 1000,
       CurrentBalance  = 1000,
       Equity          = 1000,
       ResetCount      = ResetCount + 1
 WHERE Mode = 1;  -- Paper

-- 3. Risk profil temizleme (CB reset)
UPDATE RiskProfiles
   SET CircuitBreakerStatus = 1,   -- Closed
       ConsecutiveLosses    = 0,
       CurrentDrawdownPct   = 0;
```

Down: no-op (data migration irreversible). Mevcut `Loop23MicroScalperReseed` pattern'i (ADR-0018 §18.14) tekrarlanır.

Seeder boot'ta 4 yeni per-coin `ParametersJson`'u idempotent insert eder. BTC + XRP artık `Activate = true`.

### 19.12 Endpoint Deprecation — Backend-Dev Kontrat

`src/Api/Endpoints/StrategiesEndpoints.cs` (veya eşdeğer) düzenleme:

```csharp
// POST /api/strategies/{id}/activate
app.MapPost("/api/strategies/{id:long}/activate", () =>
    Results.Json(
        new { error = "deprecated", replacement = "Set appsettings Strategies.Seed[].Activate and restart." },
        statusCode: StatusCodes.Status410Gone))
   .WithName("DeprecatedActivateStrategy")
   .WithOpenApi(op => { op.Deprecated = true; return op; });

// /deactivate aynı pattern
```

Handler + command sınıfları (varsa `ActivateStrategyCommand`) **silinmez** — composition kaybı yaratmaz; sadece route deprecated. İleride kullanıcı kararı değişirse 1 satır `Results.Json → Send(command)` geri.

### 19.13 Per-Coin Strateji Enum — Korunur

`StrategyType.MicroScalperVwapEma30s = 2` **değişmez**. 4 seed **aynı tip** — row bazlı `ParametersJson` farklı. `MicroScalperVwapEma30sEvaluator` zaten row'un JSON'ını parse ediyor (`Evaluators/MicroScalperVwapEma30sEvaluator.cs:57-58`). Yeni evaluator sınıfı **yok**, yeni enum değeri **yok**.

### 19.14 Aggregate Sınırı — Değişiklik Yok

| Aggregate / Type | Katman | Loop 30 Etki |
|---|---|---|
| `Strategy` aggregate | Domain | Değişmez — `ParametersJson` opak string; `Status` mutation `MarkActive()`/`MarkDraft()` zaten var |
| `Position` aggregate | Domain | Değişmez |
| `Order` aggregate | Domain | Değişmez — `Commission` kolonu zaten var |
| `VirtualBalance` aggregate | Domain | Değişmez — SQL migration ile başlangıç değeri resetlenir; `Debit/ApplyFill` invariant aynı |
| `RiskProfile` aggregate | Domain | Değişmez — config `MaxOpenPositions/MaxConsecutiveLosses` defaults üzerinden yeni profile'lar okur |
| `StrategyType` enum | Domain | Değişmez |
| `MicroScalperVwapEma30sEvaluator` | Infrastructure | **Değişmez** — row bazlı parse mevcut |
| `SnowballSizing` | Infrastructure | **Değişmez** |
| `PaperFeeSimulator` | Infrastructure | **Değişmez** — sadece test coverage artar |

Yeni aggregate **yok**. Yeni domain event **yok**. Dependency rule ihlali yok.

## Consequences

### Pozitif

- **Per-coin optimizasyon** — ETH kanıtlanmış profili korundu; BNB reform (TP yakın, MaxHold kısa) WR artışı için geometri hazır; BTC/XRP konservatif/geniş parametrelerle yeniden aktif.
- **Sermaye 10x net kâr ölçeği** — $5.39 avg notional → $10; aynı WR'da 1.85× gross kâr (minNotional floor nedeniyle 10× değil 1.85×, research §1 S1).
- **6 max-open** — 4 sembol × %65 ort açıklık fırsatı; rate limit kaygısız (%0.7 kullanım).
- **Aggregate dokunulmaz** — ADR aggregate sınırı, evaluator sınıfı, `SnowballSizing` sabitleri değişmiyor; değişiklik yüzeyi **config + migration + 2 route + testler**.
- **UI/API tutarlı kaynak-of-truth** — strateji durumu `appsettings.Seeds[].Activate` tek otorite; UI/DB/kod ayrışmaz.
- **410 Gone sinyali** — eski client'lara "bu endpoint kalıcı olarak kaldırıldı" mesajı (404 yerine anlamlı).

### Negatif / Tradeoff

- **Fee red flag çözümsüz kalırsa Loop 30 negatif** — binance-expert §5 "0.315 USD/saat fee vs 0.25 USD/saat gross". §19.9 birim testleri bu çelişkiyi kapatmadan ilerleme yok; backend-dev testleri kırmızı verirse Loop 30 halt.
- **Sermaye 10x ile zararlar da 10x** — Loop 29 ölçeğinde −$0.003 BNB zararı Loop 30'da ~−$0.03. `MaxConsecutiveLosses` 10 → 8 indirme bunu karşılıyor ama CB daha sık tetiklenir.
- **BTC/XRP yeniden aktif — tekrar zarar riski** — Loop 27/28'de kanıtlanmış negatif. 2-saat izleme + SymbolCircuitBreaker yokluğu operasyonel risk. Geçici çözüm: manuel `Activate=false` + reboot.
- **`MaxOpenPositions=6`, 4 sembol** — 2 sembolde paralel 2'er pozisyon açılabilir mi? ADR-0017 duplicate guard sembol başına 1 aktif pos enforce ediyor (§17.5). Bu durumda 6 kapasite 4 sembolde sadece 4'e kadar doluyor — 2 slot atıl. Sorun değil; alarm sınırı olarak bırakılır.
- **`Loop30CapitalScale10x` migration backward dönülemez** — down no-op; VirtualBalance $1000 değeri geri $100 olmaz (manuel SQL gerekir). Pattern kabul — ADR-0018 §18.14 aynı yaklaşım.
- **Seed JSON'da `KlineInterval:"1m"` ama sınıf/enum adı `30s`** — tarihsel tutarsızlık. Loop 30 scope dışı; rename ayrı ADR.

### Nötr

- MediatR command/query yüzeyi değişmez.
- `MicroScalperVwapEma30sEvaluator` kod değişmez.
- `BinanceWsSupervisor`, `MarketIndicatorService`, `StrategySignalToOrderHandler`, `OrderFilledPositionHandler` — hepsi config-driven veya row-bazlı; dokunulmaz.
- ADR-0005/0006/0009/0010/0011/0012/0014/0015/0016/0017 uyumlu.
- Frontend Loop 30 zaten toggle butonlarını kaldırdı — bu ADR frontend'e dokunmuyor; sadece backend savunma derinliği (`410`).

## Alternatives (Reddedilen)

### Alt-A — S2 EquityFraction %5'e Çıkar (Equity $100 Sabit)

`$100 × 0.05 = $5.00` < `FloorUsd $5.10` — floor yine devrede, fiili sizing değişmez. **Red** (research §1 S2).

### Alt-B — Endpoint Fiziksel Sil

`/api/strategies/{id}/activate` + `/deactivate` route'larını komple kaldır. Eski client `404 Not Found` alır — anlamsız sinyal; audit log izi de kaybolur. `410 Gone` RFC 7231 §6.5.9 semantiği doğru. **Red** (§19.8).

### Alt-C — Evaluator Sınıfı Bölme (MicroScalperEth / Bnb / Btc / Xrp)

Her sembol için ayrı evaluator sınıfı. YAGNI — evaluator davranışı identikal, sadece parametre farklı; mevcut `ParametersJson` per-row yapı zaten bu ihtiyacı karşılıyor. Sınıf bölme **SOLID (OCP) aksine**: her yeni sembol yeni sınıf = tekrarlı kod. **Red.**

### Alt-D — Leverage Simülasyon

Paper sermaye $100 sabit, %10× kaldıraç → $1000 notional erişim. Spot piyasada kaldıraç yok (ADR-0006 spot-only); domain'e margin/liquidation kavramı girer — karmaşa. **Red.**

### Alt-E — `MaxHoldMinutes` Yerine `MaxHoldBars`

30s bar × N → MaxHold. ADR-0017 §17.7 handler `maxHoldMinutes` primary key okuyor; `maxHoldBars` fallback. Primary korunur — interop tutarlı. **Red (kısmi).**

### Alt-F — `FloorUsd` Düşürme ($5.10 → $3.00)

Binance minNotional $5.00 katı; $3 floor exchange -1013 hatasına yol açar. **Red** (ADR-0018 §18.10 kanıtı).

### Alt-G — UI'dan Toggle Koruma (Silme)

Kullanıcı "panelden ben belirlemicem" dedi — explicit istek. UI tarafı Loop 30'da kaldırıldı. Backend 410 "savunma derinliği". **Red (kullanıcı kararı).**

## Source

- [`loops/loop_30/research.md`](../../loops/loop_30/research.md) — binance-expert canlı API + per-coin parametre araştırması
- [`loops/loop_29/analysis.md`](../../loops/loop_29/analysis.md) — 40 trade / 4.5 saat / 2 sembol gerçek paper verisi
- Canlı Binance exchangeInfo: https://api.binance.com/api/v3/exchangeInfo (2026-04-19)
- [ADR-0011 Equity-Aware Sizing](./0011-equity-aware-sizing-and-risk-tracking.md) — min-notional çerçevesi
- [ADR-0015 VWAP-EMA Hybrid Strategy](./0015-vwap-ema-hybrid-strategy.md) — seed-per-symbol pattern + DB reset
- [ADR-0017 TimeStop Mapping + Duplicate Protection + Sizing](./0017-timestop-mapping-duplicate-protection-sizing.md) — duplicate guard, target-notional formula
- [ADR-0018 Micro-Scalping 30s VWAP Reclaim](./0018-micro-scalping-30s-vwap-reclaim.md) — superseded strategy parameter layer (§18.3 / §18.7 / §18.13)
- [`src/Infrastructure/Strategies/Evaluators/MicroScalperVwapEma30sEvaluator.cs`](../../src/Infrastructure/Strategies/Evaluators/MicroScalperVwapEma30sEvaluator.cs) — row bazlı `ParametersJson` parse
- [`src/Infrastructure/Strategies/SnowballSizing.cs`](../../src/Infrastructure/Strategies/SnowballSizing.cs) — FloorUsd $5.10 / EquityFraction %1 korunur
- [`src/Api/appsettings.json`](../../src/Api/appsettings.json) — config değişiklikleri (§19.10)
- [Binance Spot Filters](https://developers.binance.com/docs/binance-spot-api-docs/filters)
- [Binance Fee Schedule](https://www.binance.com/en/fee/schedule)
- [Binance Rate Limits](https://developers.binance.com/docs/binance-spot-api-docs/rest-api/limits)
- [RFC 7231 §6.5.9 — 410 Gone](https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.9)
- [Microsoft Learn — DDD + CQRS](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture)
- [joelparkerhenderson/architecture-decision-record (MADR)](https://github.com/joelparkerhenderson/architecture-decision-record)
