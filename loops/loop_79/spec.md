# Loop 79 Spec — BB Reversal Evaluator (binance-expert)

## Pivot Sebebi
Loop 71-78 cumulative -$5.55. KMS oversold çıkış strateji range bound dead market'te (BBW 0.002-0.008) emit yapamıyor. Multi-strategy switch gerekli.

## binance-expert Tasarım Özeti

**Multi-regime switch (BBW-only, ADX Loop 80'e ertele):**
| Regime | BBW | Strateji |
|---|---|---|
| Dead | < 0.003 | Hiçbiri (sermaye koruma) |
| **Range** | **0.003 - 0.010** | **BB Reversal** (yeni) |
| Trending | > 0.010 | KMS (mevcut, korunur) |

## BB Reversal Entry Spec (AND)
- BBW ∈ [0.003, 0.010] (Range regime)
- `close < BB_Lower + (BB_Lower × 0.0005)` (lower band yakın, %0.05 buffer)
- `RSI14 < 35` (oversold)
- `RSI14 > RSI14Prev` (RSI dip yapıp dönüyor)
- Spread < %0.5
- Cooldown 3 bar

## BB Reversal Exit
- TP: `close > BB_Middle` (orta band, 20-bar SMA)
- SL: `close < BB_Lower × (1 - 0.001)` (false breakout, %0.1)
- MaxHold: 4 bar = 20dk

## Sayısal Hedefler
- Avg TP %0.3, SL %0.1, ratio 3:1
- Round-trip fee %0.2 → net %0.1
- **WR > %67 zorunlu** kar için
- Yüksek frekans gerekli

## Risk Uyarıları (binance-expert)
1. **False breakdown** (en büyük risk) — RSI > prev rising yardım eder ama %100 değil
2. **Testnet spread anomalisi** (3-8x mainnet) — paper fill ile etki kısmen abartılı
3. **Regime geçiş çift pozisyon** — KMS+BBR aynı coin (max 5 pos koruyor)
4. **MaxHold 20dk + BE/Trail çakışma** — BB Reversal pozisyonu için BE/Trail devre dışı opsiyon

## backend-dev Implementation Scope (background, ~2h)
1. Indicators.cs (Adx ekleme YOK, BBW yeterli)
2. BbReversalSnapshot record (CurrentClose, RSI, BB_Lower/Mean, BBW, Atr)
3. IMarketIndicatorService.TryGetBbReversalSnapshot()
4. MarketIndicatorService implement
5. StrategyEnums.BollingerBandReversal5m = 2
6. BbReversalEvaluator (Parameters + EvaluateAsync)
7. appsettings.json 5 BBR seed (KMS seed KORUNUR — 10 strateji aktif)
8. DependencyInjection register
9. Tests (6-8 unit)
10. Build + test (267+ + yeni)

## PM Aksiyonu (backend-dev iş bitince)
1. Bot kill PID 14684
2. dotnet build BinanceBot.sln
3. dotnet test
4. (Migration GEREK YOK)
5. DB UPDATE 5 BBR seed insert (StrategySeeder otomatik mi yoksa manuel?)
6. Bot restart
7. Loop 79 boot.md
8. ScheduleWakeup t30

## Sıradaki Wakeup
**ScheduleWakeup 5400s → t90 (~01:00 TR)** — backend-dev iş bitince Loop 79 boot

— PM 2026-05-01 Loop 79 spec (BB Reversal evaluator)
