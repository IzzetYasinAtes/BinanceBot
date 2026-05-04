# Loop 100 Check t70 — STATUS=3 FIX ÇALIŞTI ✓

Tarih: 2026-05-04 04:51 UTC | Boot: 03:15 UTC | Süre: 1h36m

## 🎯 KRİTİK BUG ÇÖZÜLDÜ — İlk Pozisyon Açıldı

### Kök Sebep (Loop 95-99 boyunca silent bug)

`StrategyEvaluationHandler.cs` query: `WHERE Status = 3 AND SymbolsCsv LIKE @symbol`

**StrategyStatus enum** (`Domain/Strategies/StrategyEnums.cs`):
- Draft = **1**
- Paused = **2**
- **Active = 3**

Loop 95-99 boyunca PaperTrade reset bug workaround'larımda her seferinde `UPDATE Strategies SET Status = 1 WHERE Type = 3` çalıştırdım. **Status=1 = Draft** anlamına geliyor, Active değil! Evaluator query'si Status=3 (Active) arıyordu, DB'de Status=1 (Draft) → 0 sonuç → composer hiç çağrılmıyor → 5+ saat 0 emit.

### Fix (Loop 100 t60+10dk)

```sql
UPDATE Strategies SET Status = 3 WHERE Type = 3
```

**Sonuç**: 5dk sonra ilk emit + 1 pozisyon açıldı.

### Şu Anki Durum (04:51 UTC)
- 1 açık Long pos ($104.38 notional)
- UPnL: -$0.07 (commission içermiyor)
- WalletBalance: $499.95 (commission $0.05 düşmüş)
- AllocatedMargin: ~$104
- netPnl: -$0.12

### Loop 95-99 Cumulative Bug Etkisi

| Loop | Süre | Emit | Sebep |
|---|---|---|---|
| 95 | t60 | 3 emit | Status=1 ama bot boot'ta seeder Status=3 yapmıştı (override) |
| 95 t30 fix | - | 0 yeni | UPDATE Status=1 yaptı → query 0 |
| 96 | 96dk | 10 emit | Boot'ta seeder Status=3, sonra UPDATE Status=1, frekans düşük |
| 97 | 60dk | 7 emit | Aynı pattern (boot Status=3 → UPDATE 1 → query 0 zamanla) |
| 98+99+100 | 5h+ | 0 emit | Sürekli Status=1, query her zaman 0 sonuç |

Bu kademeli bir bug — boot anlık Status=3, sonra yapay UPDATE Status=1 ile bozuluyor, restart sonrası seeder Status=3 yapıyor (geçici çalışma), sonra benim cleanup script'im UPDATE Status=1 ile yeniden bozuyor.

## Loop 101 Backlog (Loop 102'ye)

1. **PaperTrade reset endpoint fix**: RiskProfile reset etmesi (force-close zarar CB tripped tetikliyor)
2. **Cleanup SQL script'lerinde Status=1 yerine Status=3 yaz** (PM workflow)
3. Bu fix'i StrategySeeder'a kalıcı yap: `Activate()` her boot'ta state'i Status=3'e zorla

## Carryover

- Bot ayakta, 1 pos açık (Long)
- WalletBalance $499.95
- Status fix kalıcı (manuel SQL, kod değişmedi)
- 19 loop -$21.5 (cumulative), Loop 100 emit aktif şimdi
