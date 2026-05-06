# Strateji Pivot — Spec Synthesized (PM)

Tarih: 2026-05-06 | Author: PM | Status: backend-dev pickup ready

## 1. Bağlam (43 Loop Özet)

Pattern-based scalping (Loop 81 ADR-0024) başarısız:
- Sermaye $500 → $499.95 (43 loop)
- Fee %0.10/trade > peak gross profit %0.10 → expectancy negatif
- 5dk timeframe testnet düşük volatility'de math kırık

Loop 110 t315 tepe netPnl +$2.42 oldu AMA realize edilemedi (Loop 111 bug fix).

Kullanıcı kararı (B): **Strateji elden geçir**, scalping yerine swing/grid/arbitrage.

## 2. Paralel Danışmanlık Çıktıları

### binance-expert — TAVSİYE: Swing Trading (4h MTF)

`loops/loop_strategy_pivot/spec-binance-expert-strategies.md`

Neden:
- %2 move + %0.10 fee = +%1.9 net (matematik çözülüyor)
- Mevcut mimariye %80 uyumlu (kline WS var, order types aynı)
- 5 coin × 2-5 setup/gün = haftada 50-125 trade (frekans OK)
- Long+Short açılabilir
- Win rate %40-55 + R:R 1:2 = expectancy +0.3R/trade

Parametreler:
- Timeframe: **4h** (ana sinyal), 1h confirmation MTF
- EMA(20) > EMA(50) Long; tersi Short
- Volume(bar) > SMA_volume(20) × **1.5**
- RSI(14): Long 40-65, Short 35-60
- SL: ATR(14) × **1.5** (bar low/high mesafesi)
- TP: ATR × **3** (R:R 1:2)
- Max concurrent: **3**
- Risk/trade: **%1.5 sermaye** ($7.50/$500)
- Trailing: %1+ kar → BE stop
- Time-exit: 2× 4h bar geç + %0.5 kar → kapat

Reddedilenler:
- Grid: trend market'ta zarar, mevcut altyapıdan uzak (2-3 sprint extra engine)
- Funding Arb: dual account (spot+futures) gerekir, mimari kırılım
- Breakout: %30-40 win rate riski yüksek, false breakout %50

### architect — KARAR: Senaryo C (Plug-in IStrategyEvaluator)

`docs/adr/0027-strategy-pivot.md` + `loops/loop_strategy_pivot/architecture-plan.md`

Karar:
- ADR-0024 PatternComposite **paused** (silinmez, re-aktivasyon hazır)
- IStrategyEvaluator zaten port — yeni evaluator eklenmek
- StrategyEvaluatorRegistry `IEnumerable<IStrategyEvaluator>` çoğul destek
- Çekirdek 4 commit + aile-spesifik 6-12 commit

Korunanlar:
- ADR-0025 Futures + IExchangeClient + Direction
- ADR-0026 R:R simetri + Limit pullback (Option A henüz aktif değil)
- Loop 111 lifecycle bug fix'leri (MarkToMarket VO query, signal freshness, trailing peak, PaperTrade reset)

## 3. Sentez Karar

**Strateji**: Aile A — Swing Trading 4h MTF (binance-expert tavsiyesi + architect Aile A iskeleti uyumlu)

**Mimari**: Plug-in IStrategyEvaluator (architect Senaryo C)

**Implementation Order** (architect plan §3 + binance-expert hint):

### Çekirdek (4 commit, aile-bağımsız)
1. Domain: `StrategyType.SwingTrade = 4` enum extend
2. EF Migration `Loop112StrategyPivot`:
   - UPDATE Strategies SET Status = 2 (Paused) WHERE Type = 3 (PatternComposite)
   - INSERT 5 strateji Type=4 (SwingTrade) Active=3 BTC/ETH/XRP/SOL/ADA
3. StrategyEvaluatorRegistry resolve test (PatternComposite still resolved, SwingTrade resolved when added)
4. Frontend Status badge (Active/Paused görünümü)

### Aile A — Swing Trading (8-10 commit)
5. Application: `SwingTradeEvaluator` skeleton + `IStrategyEvaluator` impl
6. Application: `IIndicatorService` extend — EMA(20), EMA(50), Volume SMA(20), RSI(14), ATR(14) for 4h timeframe
7. Infrastructure: `IndicatorService.cs` 4h kline indicator computation
8. Application: `SwingTradeEvaluator.Evaluate()` — entry signal:
   - 4h bar close + EMA20 cross EMA50 (Long) or vice versa
   - Volume bar > SMA(20) × 1.5
   - RSI 40-65 (Long) or 35-60 (Short)
9. Application: `SwingTradeEvaluator` SL/TP — ATR × 1.5 / × 3 (R:R 1:2)
10. Application: Trailing stop %1+ kar → BE move
11. Application: Time-exit 2 bar (8h) + %0.5 kar → close
12. Test: 8-10 unit test + 2-3 integration
13. DI: `services.AddScoped<IStrategyEvaluator, SwingTradeEvaluator>()`
14. Risk: max concurrent 3 + %1.5/trade (RiskProfile zaten destekler)

### Korunanlar (DOKUNULMAZ)
- IExchangeClient (ADR-0025)
- Position.Direction enum (ADR-0025)
- BinanceFuturesClient (ADR-0025)
- MarkToMarketWorker SL hit + trailing + safety net (Loop 111 fix'ler)
- PaperTrade reset KeepHistory (Loop 111 commit 4)
- TradingMode.Paper / LiveTestnet / LiveMainnet enum
- VirtualBalance Wallet+AllocatedMargin+UnrealizedPnl

## 4. Done-Definition

- Toplam 12-14 commit (çekirdek + aile A)
- dotnet build 0 hata 0 uyarı
- dotnet test 0 fail (mevcut + 8-10 yeni SwingTrade test)
- DB migration apply
- Bot restart sonrası 4h bar close anında SwingTradeEvaluator çağrılır
- PatternComposite paused (silinmez)

## 5. Risk

- 4h bar warmup: 50 bar × 4h = 8.3 gün! Bot bu kadar geçmiş kline gerektirir. Mevcut kline backfill 1000 bar (Klines tablosunda zaten 4h bar 1700+ var, 13 günlük history) → warmup OK.
- 4h bar close timing: bot her 4h bar close'unda evaluator çağırmalı (5dk bar event yerine 4h)
- Funding rate maliyeti: hold süresi 8h+ ise funding eklenebilir, RiskProfile guard

## 6. Hipotez

Loop 112+ Swing Trading 4h:
- Win rate %45-55
- Avg win +$8 (gross %2 of $500 / max 3 concurrent)
- Avg loss -$4 (gross %1 SL)
- Expectancy +$2/trade
- Haftalık 5-10 trade × +$2 = +$10-20/hafta
- Aylık beklenti **+$40-80** (çok kaba — backtest gerekli)

## 7. Sonraki

backend-dev büyük delegasyon (12-14 commit, ~1-2 hafta). Sonra bot restart + Loop 112 boot.
