# Loop 42 Boot — 2026-04-24 14:47 TR (Loop 41 halt sonrası)

## Loop 41'den Loop 42'ye Geçiş Nedeni
Loop 41 t=210dk HALT (Realized -$1.7985, 7 LTC ardışık SL whipsaw). Detay: `loops/loop_41/halt-t210.md`

## Loop 42 Fix (backend-dev özet)
1. **CooldownService** (yeni) — `Application/Strategies/Cooldowns/ICooldownService.cs` + `Infrastructure/Strategies/Cooldowns/CooldownService.cs`
   - Singleton `ConcurrentDictionary<(StrategyId, Symbol), DateTimeOffset>`
   - `IsCooldown(strategyId, symbol, cooldownBars, barMinutes, now)` + `RecordSignal(...)`
   - Thread-safe, in-memory, symbol upper-case normalize
2. **DonchianBreakoutEvaluator** entegrasyon — sinyal koşulları geçtikten sonra cooldown gate, geçen sinyaller `RecordSignal`
3. **appsettings.json** — 12 Donchian seed:
   - `MinAtrPct`: 0.0006 → **0.0010** (düşük vol filter sıkı)
   - `CooldownBarsAfterSignal`: 4 → **6** (90dk = MaxHold ile uyumlu)
4. **Coin blok:** `LTC-DonchianBO15m` + `BNB-DonchianBO15m` Activate=false (Loop 41 whipsaw verisinden)
5. **8 yeni unit test** — CooldownServiceTests (260/260 geçti, 252+8)
6. Build 0 warn 0 err ✓

## Loop 42 Aktif Stratejiler (10 coin)
BTC, ETH, XRP, SOL, ADA, DOGE, LINK, DOT, AVAX, TRX — hepsi DonchianBreakout15m
**Out:** LTC, BNB (Loop 41 whipsaw)

## DB Reset
Loop 41 halt sonrası temiz başlangıç:
- OrderFills 16 → 0
- Orders 16 → 0
- StrategySignals 33 → 0
- Positions 8 → 0 (1 BNB SL + 7 LTC SL)
- Strategies 24 → 0 (fresh seed: 10 Active + 14 Draft)
- SystemEvents 1953 → 0
- RiskProfiles 3 → 0 (seeder yeniden create)
- BookTickers 12 → 0 / OrderBookSnapshots 12 → 0

VirtualBalance: `POST /papertrade/reset {500}` → IterationId 933ff841-..., ResetCount 11.

## Boot Doğrulama (t=0)
| Metrik | Değer |
|---|---|
| Cash | $500.0000 |
| Equity | $500.0000 |
| netPnl | $0.0000 |
| Pos / Order / Signal / Fill | 0 / 0 / 0 / 0 |
| Aktif Strateji | 10 Donchian (LTC+BNB blok) |
| Draft | 14 (12 AtrSwing + 2 Donchian blok) |
| WS | Streaming, drift -421ms |
| RiskProfile (Paper) | RiskPerTrade 2%, MaxOpen 5, MaxDD 24h 20% |
| Console Error | 0 |

## Playwright Smoke (1920×1080)
- ui-t0-01-dashboard.png — Hero 3×$0, $500 sıfır, hero piyasa çok pozitif (BTC +%0.66, ETH +%0.56, BNB +%0.40, XRP +%0.85, SOL +%0.43, DOGE +%2.17) ✓
- ui-t0-02-strategies.png — 10 Donchian AKTIF, LTC+BNB Donchian TASLAK + 12 AtrSwing TASLAK ✓

## Loop 41 → Loop 42 Beklenti Karşılaştırma
| Metrik | Loop 41 | Loop 42 (beklenti) |
|---|---|---|
| Trade saatlik max | LTC 7 trade/8dk = ABNORMAL | Cooldown 90dk → max 1 trade/sembol/90dk |
| Whipsaw riski | YÜKSEK (LTC kanıt) | DÜŞÜK (cooldown enforced) |
| Düşük vol sinyal | MinAtr %0.06 (geniş) | MinAtr %0.10 (sıkı) |
| Sembol univers | 12 | 10 (LTC+BNB blok) |
| 24h trade beklentisi | 50-70 (AR-GE) | 35-50 (sıkı filtre + 10 coin) |
| Whipsaw kayıp koruması | YOK | Cooldown 90dk = -$1.50 halt'a 0.5dk içinde 7 SL imkansız |

## Halt Kriter Hatırlatma
- Realized < -$1.50 → halt
- 5+ ardışık SL → halt
- Zombie >270dk → halt
- WS disconnect / CB Tripped → halt

Cooldown sayesinde "5+ ardışık SL aynı sembol" pratik olarak imkansız — farklı sembollerden geliyorsa hala mümkün ama düşük olasılık.

## Sıradaki Wakeup
**ScheduleWakeup 1800 → t=30dk (15:18 TR)**

— PM 2026-04-24 Loop 42 t=0
