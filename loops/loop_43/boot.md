# Loop 43 Boot — 2026-04-24 17:27 TR (Loop 42 stagnation sonrası auto-pivot)

## Loop 42'den Loop 43'e Geçiş Nedeni
Loop 42 t30 → t150 arası **2 saat 11 dakika boyunca**:
- Sadece 2 trade (XRP+SOL aynı anda 12:15 UTC, ikisi de SL)
- Realized -$0.7262 sabit
- 60dk pik dilim (15:20-16:23 TR) **0 yeni trade**
- 8 fresh coin (BTC, ETH, ADA, DOGE, LINK, DOT, AVAX, TRX) hiç sinyal vermedi

**Stagnation tespiti:** MinAtrPct=0.0010 + Volume Z 1.5 birlikte aşırı sıkı. Kâr olunmuyor + kayıp da gelmiyor = sermaye atıl.

## Loop 43 Fix (config-only, backend-dev gereksiz)
**appsettings.json patch:**

| Parametre | Loop 41 | Loop 42 | Loop 43 |
|---|---|---|---|
| MinAtrPct | 0.0006 | 0.0010 | **0.0007** (orta) |
| CooldownBarsAfterSignal | yok | 6 (90dk) | **6** (Loop 42 başarısı korundu) |
| VolumeZScoreThreshold | 1.5 | 1.5 | 1.5 (sabit) |
| TpAtrMultiplier / SlAtrMultiplier | 2.0 / 0.65 | 2.0 / 0.65 | 2.0 / 0.65 |
| MaxHoldMinutes | 90 | 90 | 90 |
| MaxOpenPositions (RiskProfile) | 5 | 5 | **3** (cross-symbol whipsaw koruma) |
| LTC + BNB | Active | Blok | **Blok** (Loop 41 verisi) |
| Symbol univers | 12 | 10 | **10** |

**Doğrulanmış parametreler (BTC strateji ParametersJson):**
```json
{"KlineInterval":"15m","DonchianPeriod":20,"VolumeWindow":20,
 "VolumeZScoreThreshold":1.5,"AtrPeriod":14,
 "TpAtrMultiplier":2.0,"SlAtrMultiplier":0.65,
 "MinTpPct":0.005,"MaxTpPct":0.012,
 "MinSlPct":0.002,"MaxSlPct":0.005,
 "MaxHoldMinutes":90,"MinAtrPct":0.0007,
 "CooldownBarsAfterSignal":6}
```

## DB Reset
| Tablo | Sildi |
|---|---|
| OrderFills | 4 |
| Orders | 4 |
| StrategySignals | 2 |
| Positions | 2 (XRP+SOL SL) |
| Strategies | 24 (fresh seed) |
| SystemEvents | 1261 |
| RiskProfiles | 3 (seeder yeniden create — yeni MaxOpen=3) |
| BookTickers | 12 |
| OrderBookSnapshots | 12 |

## Boot Doğrulama (t=0)
| Metrik | Değer |
|---|---|
| Cash | $500.0000 |
| Equity | $500.0000 |
| netPnl | $0.0000 |
| IterationId | 10d99ca4-... (ResetCount 12) |
| Aktif Strateji | 10 Donchian (LTC+BNB blok) |
| Draft | 14 (12 AtrSwing + 2 Donchian blok) |
| RiskProfile MaxOpenPositions | 3 ✓ |
| MinAtrPct (BTC seed doğrulama) | 0.0007 ✓ |
| WS | Streaming, drift -475ms |
| Console Error | 0 |

## Loop 42 → 43 Beklenti
| Metrik | Loop 42 | Loop 43 (beklenti) |
|---|---|---|
| Sinyal frekansı | Çok düşük (2 saat 0 trade) | Orta (MinAtrPct 30% düşürüldü → daha çok kabul) |
| Whipsaw riski | DÜŞÜK (cooldown korudu) | DÜŞÜK (cooldown sabit) + DAHA DÜŞÜK (MaxOpen 3 cross-symbol koruma) |
| Cross-symbol eşzamanlı SL | 2 (XRP+SOL aynı anda) | Maks 3 paralel (eski 5'ten azaltıldı) |
| 24h trade beklentisi | Stagnation | 15-30 trade orta tahmin |

## Halt Kriter
- Realized < -$1.50 → halt
- 5+ ardışık SL → halt
- Zombie >270dk → halt
- WS / CB → halt

## Sıradaki Wakeup
**ScheduleWakeup 1800 → t=30dk (17:57 TR)**

— PM 2026-04-24 Loop 43 t=0
