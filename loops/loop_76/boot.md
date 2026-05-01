# Loop 76 Boot — Trailing Stop Implement + MinScore 5 (2026-05-01 15:37 TR)

## Pivot Sebebi
Loop 75 final -$2.61 (eşik -$2.50 geçti). BE move başarılı (8 BE + 4 TP) ama BE öncesi 5 büyük loss (-$1.78). Çözüm: **trailing stop** (BE sonrası dinamik kar koruma) + **MinScore 5** (entry kalitesi).

## binance-expert Spec (uygulandı)
- **Trailing stop**: BE applied → sonra trailing aktif. PeakMarkPrice tracking. `markPrice < Peak × (1 - 0.0015)` → exit.
- **MinScore 4→5**: sadece güçlü entry geçer (RSI Zone min 1pt + 4 nice-to-have)
- **EMA200 + BBW**: Loop 77'ye ertelendi (0-emit riski)

## backend-dev Implementation
**Domain:**
- `Position.PeakMarkPrice` field
- `UpdatePeakAndCheckTrailing()` domain method (NotEligible/PeakUpdated/ExitTriggered)
- `PositionTrailingExitTriggeredEvent` audit event
- `TrailingResult` enum

**Infrastructure:**
- `TrailingStopOptions` (Enabled=true, TrailPct=0.0015)
- `MarkToMarketWorker.TryApplyTrailingStop` hook (BE ÖNCE, trailing SONRA)
- `DispatchTrailingExitAsync` → CloseSignalPositionCommand (trail-{posId}-{unix} CID)
- `IMediator` scope inject + EF mapping

**Migration:** `20260501143000_Loop76TrailingStop` (ALTER TABLE Positions ADD PeakMarkPrice decimal(18,8) NOT NULL DEFAULT 0) ✓ apply

**Config:** `appsettings.json` TrailingStop section ✓

**Tests:**
- 8 unit (Position trailing)
- 5 integration (MarkToMarketWorker trailing)
- 4 BE test güncellendi (Loop 76 constructor signature)
- **260/260 PASS** ✓

## DB UPDATE (PM)
- `UPDATE Strategies SET ParametersJson = REPLACE(..., '"MinScoreThreshold":4,', '"MinScoreThreshold":5,')` (5 KMS row)
- Doğrulama: tüm KMS MinScore=5, RsiCeil=60, TpMul=1.5

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 6812 |
| Port | 5000 |
| WS | Streaming ✓ |
| Warmup | 5/5 symbol ✓ |
| TrailingStop module | Enabled (TrailPct 0.0015) ✓ |
| BE module | Enabled (Trigger 0.0010, Offset 0.0002) ✓ |
| KMS params | MinScore 5, RsiCeil 60, TpMul 1.5, SlMul 0.60, MaxHold 35 |
| Migration | Loop76TrailingStop ✓ |
| Tests | 260/260 ✓ |

## Beklenti
- **MinScore 5**: emit frekans azalır (Loop 75'te 30 emit/5h → tahmin 15-20/5h), AMA entry kalitesi yüksek
- **Trailing**: TP momentum yakalanan trade'lerde ek +%0.05-0.15 kar (BE sonrası)
- **Combo etkisi**: BE öncesi büyük loss azalır + BE sonrası kar maksimize

## Halt Eşikleri
- Realized < -$1.00 (Loop 76) → Loop 77 EMA200 + BBW
- 5+ ardışık SL → CB reset
- 0 emit (60dk) → MinScore 5→4 geri al

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (16:07 TR)**

— PM 2026-05-01 Loop 76 boot (trailing + MinScore 5)
