# Loop 75 Boot — Break-Even SL Move Implement (2026-05-01 12:48 TR)

## Pivot Sebebi
Loop 72/73 "slow-bleed timestop" kritik bug: pozisyon entry'den +%0.10 hareket, TP %0.3'e ulaşamıyor, sonra geri dönüyor, SL %0.2 değmiyor, MaxHold timestop loss. backend-dev break-even SL move implement etti.

## backend-dev Implementation Özeti

**Domain:**
- `Position.cs` — `BreakEvenAppliedAt` field + `MoveStopToBreakEven()` domain method (idempotent, MoveStopResult enum)
- `PositionEnums.cs` — `MoveStopResult { Applied, AlreadyApplied, NotImproving }`
- `PositionEvents.cs` — `PositionStopMovedEvent` audit event

**Infrastructure:**
- `BreakEvenOptions.cs` — global options (`Enabled`, `TriggerPct=0.0010`, `OffsetPct=0.0002`)
- `MarkToMarketWorker.cs` — `TryApplyBreakEvenMove` hook (Long-only MVP, mark ≥ entry × (1+0.0010) → MoveStopToBreakEven(entry × (1+0.0002)))
- `PositionConfiguration.cs` — `BreakEvenAppliedAt` EF mapping
- `DependencyInjection.cs` — BreakEvenOptions register
- `KmsMomentumEvaluator.cs` — BeMoveTriggerPct/OffsetPct Parameters fields + ContextJson audit

**Migration:**
- `20260501094054_Loop75BreakEvenSL.cs` — `ALTER TABLE Positions ADD BreakEvenAppliedAt datetimeoffset NULL`
- ✓ DB apply başarılı

**Config:**
- `appsettings.json` BreakEven section + KMS strategy params

**Tests:**
- 6 unit test (Position domain): Applied/AlreadyApplied/NotImproving paths, null initial stop, throws
- 4 unit test (MarkToMarketWorker): trigger üzeri/altı, idempotency, Enabled toggle
- **247/247 toplam test geçti** ✓

## Spec Mantığı

```
markPrice >= entryPrice × (1 + 0.0010)   →   stopPrice = entryPrice × (1 + 0.0002)
                  +%0.10 trigger                       +%0.02 garanti (fee karşılar)
```

**Idempotent**: 1 pozisyona 1 kez. `BreakEvenAppliedAt` set olunca skip.

**Loop 73 senaryosu**: 4/4 açık pozisyon t90'da +$0.08-0.14 UPnl idi → BE move tetiklenirdi → timestop loss yerine ya TP hit ya da +%0.02 küçük kar çıkardı.

## Boot State
| Metrik | Değer |
|---|---|
| Cash / Equity | API'den oku — VirtualBalance state aynı (carry-over) |
| Active | 5 KMS (Status=3) ✓ |
| Bot PID | 20444 |
| WS State | Streaming ✓ |
| Warmup | 5/5 symbol ✓ |
| BreakEven module | Active (Enabled=true, Trigger %0.10, Offset %0.02) ✓ |
| Migration | Loop75BreakEvenSL ✓ apply |
| Tests | 247/247 ✓ |

## KMS Param (Loop 74.6 → Loop 75)
RsiCeiling 60, MinScore 4, TpAtrMul 1.5, SlAtrMul 0.60, MaxHold 35dk, MinTp 0.002, MaxTp 0.012. **YENI**: BeMoveTriggerPct 0.0010, BeMoveOffsetPct 0.0002.

## Beklenti
- Yeni emit fill olur (RsiCeiling 60 + MinScore 4 emit-friendly)
- Pozisyon +%0.10 UPnl olunca BE move tetiklenir → SL entry'nin +%0.02 üstüne çekilir
- Sonraki senaryolar:
  - **TP hit**: en iyi (büyük kar)
  - **BE SL hit**: küçük kar +%0.02 (fee dahil ~+$0.02)
  - **Original SL hit**: küçük loss -%0.20
  - **Timestop**: ya BE SL trigger olmuş (küçük kar) ya da pozisyon BE'ye ulaşamamış (timestop loss küçük)

→ Slow-bleed timestop pattern KIRILMALI (BE move kar koruma).

## Halt Eşikleri
- Realized < -$0.30 → Loop 76 algoritma overhaul (binance-expert)
- Circuit breaker → API reset (PascalCase!)
- 0 emit (60dk) → param fine-tune

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (13:18 TR)**

— PM 2026-05-01 Loop 75 boot (break-even SL deploy)
