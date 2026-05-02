# BinanceBot Loop L1-L80 Kapsamlı Post-Mortem (Loop 81 öncesi)

## ÖZET
- **80 loop tamamlandı**, ~11 gün (2026-04-17 → 2026-05-02)
- **Cumulative PnL**: -$13.97 (paper $500 base = -%2.8 relative)
- **Trade Statistics**: 280+ trade, %25 avg WR (breakeven %30-40 gerekli)
- **Status**: Infrastructure mature ✓, Algorithm unproven ✗

## LOOP MATRIKSI

| Loop | Net | Tip | Ana Feature | Halt Sebebi |
|------|-----|-----|-------------|------------|
| L1-L3 | $0 | MUTATE | REST Backfill, Equity Reform | Setup |
| L4-L7 | -$29.25 | HALT | SL/TP/DD Checker | CB false trip |
| L8 | +$12.24 | HALT | Risk Relax (DD 5%→20%) | CB Healthy |
| L9-L11 | -$0.22 | HALT | TP Trigger, Peak Fix | Equity bug |
| L12 | +$6.62 | HOLD | Realized Equity Fix | Consec Loss |
| L13-L18 | +$0.10 | HALT | PnL-based Equity ✓, UI Reform | User request |
| L19-L22 | N/A | HALT | VwapEma Scalper (3→4 coin) | Low frequency |
| L29 | +$0.05 | DATA | 40 trade analysis (ETH 50% WR) | Research |
| L30 | N/A | SPEC | Binance expert BE_WR calc | Research |
| **L41-L43** | **-$2.97** | **HALT** | **Donchian BO 15m** | **0% WR** |
| L44-L49 | -$1.70 | HALT | BB MeanRev + EmaScalper + Hybrid | Low WR |
| L50 | N/A | SPEC | HybridMomentum1m (15m+1m) | 8-15/h target |
| L51-L70 | -$5.23 | HALT | KMS + Param tuning | Spiral |
| L71 | +$0.85 | HOLD | KMS Skor | Solo positive |
| L72-L78 | -$5.40 | HALT | BE Move, Trailing, BBW, EMA200 | Slow-bleed |
| L79 | -$2.19 | HALT | Full Stack KMS+BBR | WR %28 < BE |
| L80 | -$0.52 | CONT | ADX + BBR Volume Gate + Counter Fix | Stable |

## FAZ ANALİZİ

### Faz 1: Infrastructure (L1-L19) — Net -$0.52
Equity tracking 6 iteration (cash → cost-basis → PnL-based, L17 ✓), -$187 false flag aggregate. Foundation locked.

### Faz 2: VwapEma Scalping (L20-L30) — Net +$0.05
ETH 50% WR + VWAP + volume = iyi (40 trade L29). BTC/BNB farklı param. Hold 6-8dk optimal.

### Faz 3: Multi-Evaluator (L41-L70) — Net -$5.23
Donchian 0% WR, BB MeanRev fail, EmaScalper 23%, HybridMomentum1m 27%, KMS tuning. Pure loss. Param spiral.

### Faz 4: Full Stack Multi-Regime (L71-L80) — Net -$8.11
Regime concept sound (KMS trending + BBR range + ADX), AMA:
- KMS slow-bleed: entry → maxhold → no TP → timestop loss
- BBR false breakdown XRP 0/2 (L80 volume gate yardım etti)
- Counter persistent (L80 auto-reset fix ✓)

## TOP 5 ÖĞRENİM

| # | Öğrenim | Loop | Fix |
|---|---------|------|-----|
| 1 | Equity tracking foundation | L6-16 | ✓ L17 PnL-based |
| 2 | Risk threshold market-dep (5%→20%) | L6-8 | ✓ L8 |
| 3 | Frekans-kalite inverse (150/h imkansız) | L41-70 | L81 hedef 20-40/h |
| 4 | Counter reset on restart | L78-80 | ✓ L80 auto-reset |
| 5 | Param tuning needs BE_WR pre-calc | L44-70 | L81: pre-calc only |

## TOP 5 TEKRARLAYAN HATA

| Hata | Kayıp | Çözüm |
|------|-------|-------|
| **Equity false flag** (6 iteration) | -$187 | PnL-based ✓ |
| **CB metric conflict** (AllTime vs 24h) | -$50+ forgone | Realized-only ✓ |
| **Param tuning spiral** | -$5.40 | Pre-calc BE_WR + lock per cycle |
| **Counter persistence** | Early halt | Seeder auto-reset ✓ L80 |
| **Strategy spec ambiguity** | Rework | ADR mandatory before code |

## FREKANS ANALİZİ (Hedef: 30+/h)

| Loop | Type | Emit/Hour | Target % |
|------|------|-----------|----------|
| L5 | Pattern scalp implicit | 10 | 33% |
| L29 | VwapEma 2-coin | 8.9 | 30% |
| L50 | HybridMomentum1m | 8-15 | 27-50% ← closest |
| L79 | KMS+BBR | 0.7 | 2% |

**Hiç 30+/h'ye ulaşılmadı.** L50 closest %50. Root: 15m timeframe ceiling, gates çok katı, 5-coin correlation paradox.

## TEST EDİLMEMİŞ KOMBİNASYON (L81+ Vision)

| # | Kombinasyon | Priorite |
|---|------------|----------|
| 1 | **Pattern-Based Scalping** (formal spec) | HIGH |
| 2 | Multi-Timeframe Confluence (1m+5m+15m) | MID |
| 3 | 5+ Coin Correlation portfolio risk | MID |
| 4 | Order Book Depth Indicator (level-1000 var, kullanılmıyor) | LOW |
| 5 | Trailing Stop Mechanism (L72 spec ambig) | MID |
| 6 | **Per-Coin Dynamic Params** (ETH 50% vs BNB 44% vs BTC 30%) | HIGH |

## LOOP 81 STRATEJİK TAVSIYE

### KESİNLİKLE YAPILMALI
- ✓ Equity tracker retest (L17 lock, 1h verify)
- ✓ Counter auto-reset confirm (L80 deployed)
- ⚠ ADX threshold tune (KMS 20 too tight)
- ⚠ **Pattern-Based Scalping formal spec** (ADR-0024)
- ⚠ Per-Coin Param Lock (BE_WR research → seed config)

### YAPILMAMALI
- ❌ **Donchian Breakout 15m** (0% WR, enum'dan kaldır)
- ❌ **BB Reversal single-coin** (XRP false breakdown)
- ❌ **Param tuning intra-loop** (-$5.40 spiral kanıt)
- ❌ Saat dilimi strateji
- ❌ Over-tight risk (5% DD natural 2% vol triggers)

## KÜMÜLATİF PnL

| Faz | Trade | Realized | WR | Avg/Trade |
|-----|-------|----------|-----|-----------|
| Infra L1-19 | 100 | -$0.52 | N/A | -$0.005 |
| VwapEma L20-30 | 40 | +$0.05 | 50% | +$0.001 |
| MultiEval L41-70 | 60+ | -$5.23 | 20% | -$0.087 |
| FullStack L71-80 | 80+ | -$8.11 | 28% | -$0.101 |
| **TOTAL L1-80** | **280+** | **-$13.97** | **25%** | **-$0.050** |

## SONUÇ

**Negative expectancy** doğrulandı (WR 25% < BE 30-40%).

### Yön
- **Pattern-based scalping** = primary L81 (L5 implicit +%12.27 evidence)
- **Multi-regime concept** keep, param tuning kuralları değişti
- **Per-coin tuning** mandatory

**L81 GO**: Pattern-Based Scalping ADR-0024 + DB tam reset + Per-coin BE_WR pre-calc.

— Compiled by Explore agent (Haiku 4.5 Thorough), 2026-05-02
