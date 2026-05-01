# Loop 79 Boot — BB Reversal Multi-Regime Deploy (2026-05-01 23:35 TR)

## Pivot Sebebi
Loop 71-78 cumulative -$5.55. KMS oversold çıkış strateji range bound dead market'te (BBW 0.002-0.008) emit yapamıyor. binance-expert spec: **multi-regime switch + BB Reversal evaluator**.

## backend-dev Implementation

**Yeni dosyalar:**
- `BbReversalSnapshot.cs` (CurrentClose, RSI, BB_Lower/Mean, BBW, Atr)
- `BbReversalEvaluator.cs` (Parameters + EvaluateAsync, AND-gate logic)
- `BbReversalEvaluatorTests.cs` (11 test)

**Değişen dosyalar:**
- `StrategyEnums.BollingerBandReversal5m = 2` enum eklendi
- `IMarketIndicatorService.TryGetBbReversalSnapshot()` metodu
- `MarketIndicatorService` implement (mevcut 200 bar buffer)
- `DependencyInjection` BbReversalEvaluator register
- `appsettings.json` 5 BBR seed (KMS seed KORUNDU)

**Build/Test:** **278/278 PASS** ✓ (önceki 267 + 11 yeni)

## Multi-Regime Switch (BBW-only)
| Regime | BBW | Strateji | Davranış |
|---|---|---|---|
| Dead | < 0.003 | Yok | Sermaye koruma |
| **Range** | **0.003-0.010** | **BB Reversal** | **YENI** |
| Trending | > 0.010 | KMS | Mevcut korunur |

## BB Reversal Logic
**Entry (AND):**
- BBW ∈ [0.003, 0.010]
- close < BB_Lower + (BB_Lower × 0.0005)  (lower band yakın)
- RSI < 35 (oversold)
- RSI > RSIPrev (bouncing)
- Spread < %0.5
- Cooldown 3 bar

**Exit:**
- TP: close > BB_Middle (orta band SMA20)
- SL: entry × (1 - 0.001) (sabit %0.1)
- MaxHold: 4 bar = 20dk

**Hedef**: TP %0.3, SL %0.1 (3:1), WR > %67 zorunlu.

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 6180 |
| Port | 5000 |
| WS | Streaming ✓ |
| Warmup | 5/5 ✓ |
| **10 Strateji Active** | 5 KMS (id 891-895) + **5 BBR (id 896-900)** ✓ |
| Migration | GEREK YOK (sadece kod + appsettings) |
| Tests | 278/278 ✓ |

## Tam Stack (Loop 71-79)
| Loop | Feature | Etki |
|---|---|---|
| L71 | KMS skor sistemi | Trending strateji |
| L75 | BE move | TP momentum koruma |
| L76 | Trailing stop | Peak takibi |
| L77 | EMA200 hard-gate | Trend yukarı zorunlu (KMS) |
| L77 | BBW score | Trending bonus puan |
| L78 | BBW hard-gate (KMS) | Zayıf trend skip (KMS) |
| **L79** | **BB Reversal** | **Range strateji** |

## Risk Uyarıları (binance-expert)
1. **False breakdown** (en büyük risk) — RSI rising koruyor ama %100 değil
2. **Testnet spread anomalisi** (3-8x mainnet)
3. **Çift pozisyon riski** (KMS+BBR aynı coin, max 5 pos koruyor)
4. **WR > %67 zorunlu** kar için (round-trip fee %0.2)

## Beklenti
- Range bound market'te BB Reversal emit verir (BBW 0.003-0.010)
- Trending burst'larda KMS aktif
- Dead market (BBW < 0.003) hiçbiri emit yapmaz
- Frekans yükselir (10 strateji), entry kalitesi pazar koşuluna uygun
- Realized iyileşme bekleniyor

## Halt Eşikleri
- Realized < -$1.00 (Loop 79 specific) → Loop 80 ADX ekleme veya başka pivot
- Circuit breaker → API reset
- 5+ ardışık SL → CB tripped

## Cumulative L71-L78 → L79 Başlangıç
- Realized total: **-$5.55** (Loop 78 sonrası)
- VirtualBalance: bot DB state ile devam (carry-over)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (00:05 TR)**

— PM 2026-05-01 Loop 79 boot (BB Reversal multi-regime deploy)
