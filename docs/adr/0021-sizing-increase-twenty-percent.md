# 0021. Sizing Increase %1 → %20 — Fee-Drag Rescue with FloorUsd $20.10

Date: 2026-04-22
Status: Accepted
Supersedes sizing constants of: [ADR-0018 Micro-Scalping 30s VWAP Reclaim](./0018-micro-scalping-30s-vwap-reclaim.md) §18.10
Relates to: [ADR-0011 Equity-Aware Sizing](./0011-equity-aware-sizing-and-risk-tracking.md), [ADR-0017 TimeStop/Duplicate/Sizing](./0017-timestop-mapping-duplicate-protection-sizing.md), [ADR-0019 Per-Coin Parameters + Capital 10x](./0019-per-coin-parameters-capital-10x.md)

> Bu ADR tek eksende karar verir: **paper starting balance $100 SABİT** korunur (CLAUDE.md altın kural), ama sizing policy `max(equity × 0.01, 5.10)` yerine `max(equity × 0.20, 20.10)` olur. Yeni sabitler: `SnowballSizing.FloorUsd = 20.10m`, `SnowballSizing.EquityFraction = 0.20m`. Global risk: `RiskProfile.Defaults.MaxOpenPositions = 3` (6 → 3). $100 sermaye × %20 = $20 target → floor $20.10 devrede. 3 × $20.10 = $60.30 concurrent exposure (%60 equity), $40 cash buffer kalır; 4 coin seed'in 1'i sinyal kuyruğunda bekler. Yeni aggregate **yok**, yeni domain event **yok**, yeni evaluator **yok** — değişiklik yüzeyi 2 sabit + 1 config + seed default + test güncellemesi.

## Context

### 21.1 Loop 32 Fee-Drag Matematik Analizi

ADR-0019 §19.9 fee red-flag ("$0.315/saat fee vs $0.25/saat gross") Loop 30-32 boyunca canlı ölçekte doğrulandı. Güncel üretilen trade matematiği (Loop 32 BNB-MicroScalper seed'ine göre, en agresif TP):

| Metric | Formül | Değer |
|---|---|---|
| Avg notional | `max($100 × 0.01, 5.10)` + LOT_SIZE buffer | **$5.10–$5.39** |
| Round-trip fee (BNB discount) | `notional × 0.0015` | **$0.0081** |
| TP gross (BNB seed %0.25) | `notional × 0.0025` | **$0.0135** |
| **Net per win** | gross − fee | **+$0.0054** |
| SL gross (BNB seed %0.15) | `notional × 0.0015` | **$0.0081** |
| Net per loss | -(gross + fee) | **−$0.0162** |
| BE_WR | `(SL + fee) / (TP + SL + 2×fee)` | **%42.9** |

Saatte 5 winning trade = $0.027/saat net. Kullanıcı notu: "saatte 5 işlem kazanırsa $0.015 — komik tempo". Fee/gross oranı %60 — marjinal kar her an WR dalgalanmasıyla negatife döner.

### 21.2 Kullanıcı Talimatı (Loop 33 boot, 2026-04-22)

Birebir:

> "işlemleri çok az dolar ile yaptığımız için böyle oluyor gibi giriş fiyatını artırabilirsin"

> "$5.10 sizing çok az, artır"

**Kısıt:** starting balance $100 **SABİT** (CLAUDE.md altın kural — değişmez; ADR-0019 Loop 30 $1000 ölçeği bu kurala aykırı olduğu için §18.10 tablosu korunur ama gerçek paper balance $100 kalmıştır).

### 21.3 Hedef Matematik — Sizing %1 → %20

`equity × 0.20 = $100 × 0.20 = $20`. `FloorUsd = $20.10` (Binance minNotional $5 × 4 = $20 değil; daha yüksek user-intent floor $20 + precision buffer $0.10). Bu `max(20, 20.10) = $20.10` fiili sizing.

Yeni matematik (BNB-MicroScalper, TP %0.25, SL %0.15, fee RT %0.15):

| Metric | Formül | Değer |
|---|---|---|
| Sizing | `max($100 × 0.20, 20.10)` | **$20.10** |
| Round-trip fee | `20.10 × 0.0015` | **$0.030** |
| TP gross | `20.10 × 0.0025` | **$0.050** |
| **Net per win** | gross − fee | **+$0.020** |
| SL gross | `20.10 × 0.0015` | **$0.030** |
| Net per loss | -(gross + fee) | **−$0.060** |

**Net kazanç 3.7× hızlandı** ($0.0054 → $0.020). Saatte 5 win = $0.10/saat — hâlâ küçük ama fee drag etkisi sabit kaldı (gross %60 → gross %60 yine; ancak absolute $ büyüdüğü için noise floor dışında görünür).

**BE_WR formülü:** `(0.15 + 0.15) / (0.25 + 0.15 + 0.30) = 0.30 / 0.70 = %42.9` — aynı kaldı (yüzdeler değişmedi). Strateji WR ≥ %55 hedefi (BE %42.9 + %12 güvenlik marjı) zorunlu.

### 21.4 Concurrent Exposure Analizi

$100 sermaye × $20.10 sizing → %20.1 tek pozisyon oranı. `MaxOpenPositions = 6` yerinde kalırsa 6 × $20.10 = $120.60 — equity'yi aşar (imkansız). Çözüm: **MaxOpenPositions 6 → 3**.

3 × $20.10 = $60.30 exposure (%60.3 equity), $39.70 cash buffer. 4 coin seed (BTC/ETH/BNB/XRP) mevcut; 1 coin slot kuyruğunda bekler (`StrategySignalToOrderHandler` duplicate guard ADR-0017 §17.5 sembol başına 1 aktif pos enforce eder — 3 slot dolduğunda 4. sinyal rejected veya queued-noop).

**Drawdown worst-case:** 3 concurrent SL tetik (BNB seed en sıkı, SL %0.15). 3 × $0.060 = **$0.18** tek tick zarar. `MaxDrawdown24hPct = 0.20` → $20 budget → 111 ardışık loss event toleransı — dışarıda.

### 21.5 Kod Envanteri

| Kaynak | Durum |
|---|---|
| `SnowballSizing.FloorUsd` | `5.10m` (ADR-0018 §18.10) |
| `SnowballSizing.EquityFraction` | `0.01m` (ADR-0018 §18.10) |
| `appsettings.RiskProfile.Defaults.MaxOpenPositions` | `6` (ADR-0019 §19.7) |
| `RiskProfileConfiguration.cs` seed row | `MaxOpenPositions = 2` (3 TradingMode için seed) |
| `SnowballSizingTests.cs` | InlineData %1 + $5.10 literalleri; `Constants_MatchAdrLiterals` sentinel |
| `VirtualBalance` aggregate | Paper starting balance $100 (ADR-0019 §19.6 $1000 ölçek kullanıcı tarafında canlı kullanılmadı; $100 korundu) |

## Decision

### 21.6 SnowballSizing Sabit Güncellemesi

```csharp
internal static class SnowballSizing
{
    public const decimal FloorUsd       = 20.10m;  // ADR-0021 — user-intent floor $20 + $0.10 buffer
    public const decimal EquityFraction = 0.20m;   // ADR-0021 — %1 → %20, fee-drag rescue
    // CalcMinNotional(equity) = max(equity × 0.20, 20.10)
}
```

**Kartopu eşikleri (yeni tablo):**

| Equity | `equity × 0.20` | Floor | Target | Yorum |
|---|---|---|---|---|
| $0 / negatif | — | 20.10 | **$20.10** | Safety floor |
| $100 | 20.00 | 20.10 | **$20.10** | Floor marjinal devrede (0.5% fark) |
| $101 | 20.20 | 20.10 | **$20.20** | Pct branch başlar |
| $500 | 100.00 | 20.10 | **$100.00** | Oransal büyüme |
| $1000 | 200.00 | 20.10 | **$200.00** | Kartopu tam etkili |

**Formül güvenlik kontrolleri (invariant):**
- `CalcMinNotional(equity <= 0)` → floor
- Floor ≥ Binance minNotional ($5.00) — hard filter'ı otomatik geçer
- `StrategySignalToOrderHandler` `effectiveMinNotional = Math.Max(targetNotional, instrument.MinNotional)` zinciri dokunulmaz

### 21.7 MaxOpenPositions 6 → 3

`src/Api/appsettings.json`:

```json
"RiskProfile": {
  "Defaults": {
    "MaxOpenPositions": 3,
    "MaxConsecutiveLosses": 8,
    ...
  }
}
```

`src/Infrastructure/Persistence/Configurations/RiskProfileConfiguration.cs` seed satırı (Paper/LiveTestnet/LiveMainnet 3 mod): `MaxOpenPositions = 2` zaten config default'undan düşüktü (conservative seed), korunur. Config `Defaults.MaxOpenPositions = 3` yeni profil yaratılırken varsayılan — mevcut seed'leri etkilemez (idempotent seed HasData `2` ile oluşur, değişmez).

### 21.8 Starting Balance — $100 SABİT

`VirtualBalance` paper seed **değişmez**. CLAUDE.md altın kuralı: "$100 starting balance sabit". ADR-0019 §19.6 $1000 migration uygulanmış olsa bile Loop 32 öncesi `VirtualBalance reset` ile $100'e geri dönmüştür (veya test ortamında $100 canlı kullanılıyor — Loop 32 log'ları). Bu ADR migration eklemez; SQL run-time etkisi yok.

### 21.9 Strateji Parametre Değişmez

ADR-0019 §19.5 per-coin seed'leri (BTC/ETH/BNB/XRP TP/SL/MaxHold profilleri) **dokunulmaz**. Bu ADR sadece sizing + exposure tavanı ele alır. Strateji WR hedefi ≥ %55 zorunlu; Loop 33 ilk 2 saat monitor + WR < %42.9 düşerse halt.

### 21.10 Aggregate Sınırı — Değişiklik Yok

| Aggregate / Type | Katman | Loop 33 Etki |
|---|---|---|
| `Strategy` aggregate | Domain | Değişmez |
| `Position` aggregate | Domain | Değişmez |
| `Order` aggregate | Domain | Değişmez |
| `VirtualBalance` aggregate | Domain | Değişmez — $100 starting balance korunur |
| `RiskProfile` aggregate | Domain | Değişmez — config default `MaxOpenPositions` 3 yeni profil için; mevcut seed row'u yine 2 |
| `SnowballSizing` | Infrastructure | **Sabit değişir** — FloorUsd $20.10, EquityFraction 0.20 |
| `MicroScalperVwapEma30sEvaluator` | Infrastructure | Değişmez |
| `PaperFeeSimulator` | Infrastructure | Değişmez |

Yeni aggregate **yok**. Yeni domain event **yok**. Yeni migration **yok**. Dependency rule ihlali yok.

### 21.11 Test Güncellemesi

`tests/Tests/Infrastructure/Strategies/SnowballSizingTests.cs`:

- `InlineData(0, 20.10)` — floor safety
- `InlineData(-50, 20.10)` — negative equity safety
- `InlineData(50, 20.10)` — equity × 0.20 = 10 < floor
- `InlineData(100, 20.10)` — equity × 0.20 = 20 < floor (marjinal)
- `InlineData(100.5, 20.10)` — crossover öncesi
- `InlineData(200, 40.0)` — pct branch aktif (200 × 0.20 = 40 > 20.10)
- `InlineData(500, 100.0)` — full snowball
- `Constants_MatchAdrLiterals` sentinel: FloorUsd == 20.10m, EquityFraction == 0.20m
- `CalcMinNotional_EquityJustAboveCrossover_UsesPct`: eşik ≈ $100.5 (0.20 × 100.5 = 20.10), $110 pct branch test

## Consequences

### Pozitif

- **Net per-win 3.7× hızlandı** — $0.0054 → $0.020. Fee drag absolute dolar cinsinden görünür kalır ama saatlik kâr anlamlı olur.
- **Saatte 5 win × $0.020 = $0.10** net — 4 saatte $0.40; Loop 32 matematiği 10× hızı.
- **Binance minNotional filter rahat** — $20.10 >> $5.00; LOT_SIZE precision buffer'da bile guvenli.
- **Aggregate ve migration dokunulmaz** — değişiklik yüzeyi minimal (2 sabit + 1 config key + testler). Loop 33 boot'u hızlı.
- **$100 SABİT korunur** — CLAUDE.md altın kural ihlal edilmez.
- **Cash buffer %40** — 3 concurrent pos sonrası $39.70 cash kalır; flaş likidite ihtiyaçları (ops reset, new signal) karşılanır.

### Negatif / Tradeoff

- **Drawdown absolute dolar 10× büyüdü** — tek loss $0.016 → $0.060 (3.75×). 3 ardışık loss = $0.18. `MaxDrawdown24hPct = %20 = $20` bütçesinde 111 loss toleransı; teoride güvenli, ama WR %40 altı senaryolarda hızla erir.
- **Strateji WR ≥ %55 zorunlu** — BE_WR %42.9; %12 güvenlik marjı. Loop 32 ETH WR %50 gözlemi altında; marjın dışına düşebilir. Monitor + halt-if-WR<%45 ops kuralı.
- **4 coin / 3 slot** — 1 coin sürekli kuyrukta bekler. Hangi coin'in rejected olacağı stochastic (sinyal timing); deterministic priority yok (v2 backlog).
- **ADR-0019 §19.6 sermaye 10x "canlı olmadı" notu** — historical inconsistency; bu ADR clean-slate reset değil, sadece sizing sabit update. $1000 seed migration dosyası (`20260419210000_Loop30CapitalScale10x`) repo'da kaldı — ops reset scripti $100'e geri alıyor (repo tarihçesi).
- **`EquityFraction = 0.20` Loop 23 öncesi "%20 kullanıcı hedefi" değeri** — dairesel return. XML doc bunu net belirtir.
- **Concurrent %60 exposure** — tek piyasa crash'inde 3 × -%1 = -$0.60 (%0.6 equity) tick; hâlâ küçük ama sizing büyüdükçe risk profili değişir.

### Nötr

- MediatR command/query yüzeyi değişmez.
- `StrategySignalToOrderHandler` formülü dokunulmaz.
- `PaperFeeSimulator` + `VirtualBalance.ApplyFill` test coverage ADR-0019 §19.9 ile kapatıldı; bu ADR fee path'e dokunmaz.
- `BinanceWsSupervisor`, `MarketIndicatorService` değişmez.
- ADR-0005/0006/0009/0010/0011/0012/0014/0015/0016/0017/0018/0019 uyumlu (sadece ADR-0018 §18.10 sabit tablosu supersede edilir).

## Alternatives (Reddedilen)

### Alt-A — %10 Sizing (Daha Konservatif)

`equity × 0.10 = $10`, floor $10.10. Net per-win $0.010 — hâlâ küçük; 2× iyileşme yetersiz. Kullanıcı "artır" dedi, %10 yarı yol. **Red** — fee drag görünür fark yaratmak için %20 gerekli.

### Alt-B — %30 Sizing (Daha Agresif)

`equity × 0.30 = $30`, floor $30.10. 3 concurrent pos → $90.30 exposure (%90 equity), $9.70 cash buffer. Flash-crash + duplicate order race scenario'sunda cash negatif risk. `MaxPositionSizePct = 0.40` soft clamp var ama 3 × %30 = %90 marjinal. **Red** — %20 risk/reward optimum.

### Alt-C — MaxOpenPositions = 4 (4 Coin Paralel)

4 × $20.10 = $80.40 exposure (%80 equity), $19.60 cash. Buffer dar; ADR-0017 duplicate guard her sembol için tek pos → teoride çalışır ama cash breathing room düşük. 3 slot + 1 coin kuyruk daha sağlıklı. **Red.**

### Alt-D — Starting Balance $500'e Çıkar

CLAUDE.md altın kural "$100 SABİT" ihlal. Kullanıcı explicit sabit istiyor. **Red.**

### Alt-E — Per-Coin Farklı Sizing (BTC $15, XRP $30)

`SnowballSizing` aggregate-free static; per-coin sizing mantığı evaluator'a gömülmeli → SRP bozar. Tek formül tüm semboller için daha sade. Per-coin ihtiyaç gerçekten doğarsa ayrı ADR + `ISizingStrategy` soyutlaması. **Red (YAGNI).**

### Alt-F — FloorUsd Tamamen Kaldır

`CalcMinNotional = equity × 0.20` saf. `$100 × 0.20 = $20` zaten $5 Binance filter üstünde. Ama equity sıfıra düşerse (kriptik state) sizing 0 — order path NaN/zero bug'ı. Floor safety net. **Red.**

## Source

- Kullanıcı talimatı (2026-04-22): "$5.10 sizing çok az, artır"; "işlemleri çok az dolar ile yaptığımız için"
- Loop 32 canlı paper gözlemi: net per-win $0.0054 — gross %60 fee drag
- [ADR-0011 Equity-Aware Sizing](./0011-equity-aware-sizing-and-risk-tracking.md)
- [ADR-0017 TimeStop Mapping + Duplicate Protection + Sizing](./0017-timestop-mapping-duplicate-protection-sizing.md)
- [ADR-0018 Micro-Scalping 30s VWAP Reclaim](./0018-micro-scalping-30s-vwap-reclaim.md) — supersedes §18.10 sizing constants
- [ADR-0019 Per-Coin Parameters + Capital 10x](./0019-per-coin-parameters-capital-10x.md) — $1000 seed migration historical, $100 canlı kural
- [`src/Infrastructure/Strategies/SnowballSizing.cs`](../../src/Infrastructure/Strategies/SnowballSizing.cs) — FloorUsd + EquityFraction
- [`src/Api/appsettings.json`](../../src/Api/appsettings.json) — RiskProfile.Defaults
- [`tests/Tests/Infrastructure/Strategies/SnowballSizingTests.cs`](../../tests/Tests/Infrastructure/Strategies/SnowballSizingTests.cs)
- [Binance Spot Filters — NOTIONAL](https://developers.binance.com/docs/binance-spot-api-docs/filters)
- [Binance Fee Schedule](https://www.binance.com/en/fee/schedule)
- [RFC 2119 — Requirement Levels](https://datatracker.ietf.org/doc/html/rfc2119)
- [Microsoft Learn — DDD + CQRS](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [joelparkerhenderson/architecture-decision-record (MADR)](https://github.com/joelparkerhenderson/architecture-decision-record)
