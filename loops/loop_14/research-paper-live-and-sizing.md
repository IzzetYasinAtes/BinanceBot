# Loop 14 — AR-GE: Paper-Live Realism + $100 Sizing Optimum

**Agent:** binance-expert
**Tarih:** 2026-04-18
**Özet:** Kapsamlı canlı API doğrulama + akademik kaynak research.

## A) Paper ↔ Live Analizi

### A1. Testnet vs Mainnet (canlı karşılaştırma)
- **Fiyat:** BTC/XRP mainnet-testnet delta %0.001-0.014 (aynı feed)
- **Depth:** Testnet BTC L1 26 BTC (~$1.97M), mainnet 2.24 BTC (~$170K) → **testnet 4-12x ŞİŞİK** (yapay bot emirleri)
- **$50 notional:** her iki ortamda L1'de kalır, slippage farkı anlamsız ✓

### A2. Gerçek Slippage ($15-50 MARKET)
- BTC/BNB/XRP L1 spread: **0.01-1.58 bps**
- Akademik TCA benchmark (Anboto 60K emir): **-0.58 bps ortalama**
- Volatilite spike senaryosu: %2'ye çıkabilir (nadir)
- **Mevcut `FixedSlippagePct=0.0005` (5 bps) = gerçeğin 5x FAZLASI** ❌
- **Öneri:** 1 bps VEYA 0 (depth walk yeterli)

### A3. Latency
- Retail p50: ~90ms, p95: 150-200ms
- Mevcut `SimulatedLatencyMs=100` p50'yi yakalar ama p95 yok
- **Öneri:** Gaussian(mu=90, sigma=35) opsiyonel iyileştirme

### A4. Fee Schedule (VIP0)
- Maker: %0.100, Taker: %0.100
- BNB ile ödeme: **%0.075** (%25 indirim)
- Round-trip: %0.2 (normal), %0.15 (BNB)
- Mevcut %0.1 taker doğru, BNB discount flag opsiyonel

## B) $100 Portfoy Kelly + Optimum

### B1. Kelly Math
| Senaryo | Winrate | R:R | Full Kelly | Half Kelly |
|---|---|---|---|---|
| Gerçekçi | %50 | 1.5:1 | %16.7 | %8.3 |
| Optimist | %55 | 1.5:1 | **%25** | %12.5 |
| Agresif | %45 | 2:1 | %17.5 | %8.75 |

**Kural:** Full Kelly riskli, Half Kelly (%10-12) sürdürülebilir. Kripto volatilitesinde %10-25 → Half/Quarter Kelly önerilir.

### B2. Expectancy (fee dahil)
| Senaryo | $30 notional | $40 notional | $50 notional |
|---|---|---|---|
| 45% WR + 2:1 R:R | $0.024 | $0.032 | $0.040 |
| 50% WR + 1.5:1 R:R | $0.015 | $0.020 | $0.025 |
| 55% WR + 1.5:1 R:R | $0.048 | $0.064 | $0.080 |

### B3. Gerçekçi Günlük Hedef
- Profesyonel quant: %0.03-0.08/gün
- Kripto bot sürdürülebilir: **%0.3-1.0/gün** (fee sonrası)
- Kullanıcı %0.7/gün hedefi ulaşılabilir — $50 notional + 10 trade/gün + %55 WR + 1.5:1 R:R

## C) Uygulama Parametreleri (backend-dev için hazır)

### C1. PaperFillOptions
```csharp
FixedSlippagePct = 0.0001  // 5bps → 1bps (veya 0 daha doğru)
SimulatedLatencyMs = 100   // sabit (opsiyonel Gaussian Loop 15+)
// Yeni:
UseBnbFeeDiscount = false  // V1 conservative
```

### C2. Domain Validation (RiskProfile.cs)
```csharp
riskPerTradePct: (0, 0.02] → (0, 0.05]      // %2 → %5
maxPositionSizePct: (0, 0.20] → (0, 0.60]   // %20 → %60
```

### C3. FluentValidation (UpdateRiskProfileCommand.cs) — aynı limitler

### C4. Seeder Defaults (appsettings.json)
```json
"RiskPerTradePct": 0.02,       // %1 → %2
"MaxPositionSizePct": 0.40,    // %15 → %40
"MaxDrawdown24hPct": 0.20,
"MaxDrawdownAllTimePct": 0.40,
"MaxConsecutiveLosses": 10,
"MaxOpenPositions": 2           // YENİ
```

### C5. Yeni RiskProfile.MaxOpenPositions field
- Validation: [1, 10]
- Default: 2
- `StrategySignalToOrderHandler` kontrol eder (açık pozisyon count >= max ise skip)

## Kaynaklar
- https://www.binance.com/en/fee/schedule (VIP tier canlı doğrulama)
- [Anboto Labs TCA](https://medium.com/@anboto_labs/slippage-benchmarks-and-beyond-transaction-cost-analysis-tca-in-crypto-trading-2f0b0186980e)
- [Fractional Kelly SSRN 2024](https://papers.ssrn.com/sol3/papers.cfm?abstract_id=5027918)
- [Matthew Downey — Why fractional Kelly?](https://matthewdowney.github.io/uncertainty-kelly-criterion-optimal-bet-size.html)
- [Backtrader slippage docs](https://www.backtrader.com/docu/slippage/slippage/)
- Canlı: `api.binance.com/api/v3/depth` + `testnet.binance.vision/api/v3/depth`
