# Loop 59 — Halt @ t=480dk (8h) (2026-04-30 01:33 TR) — ORTA YOL GEVŞETME

## Halt Sebebi (POZİTİF)
8h boyunca 0 emit. Sermaye $500 %100 korundu. binance-expert kararı: orta yol gevşetme (B), EMA200 trend filter KORU.

## Loop 60 Parametre Değişikliği

| Parametre | Loop 59 | **Loop 60** | Etki |
|---|---|---|---|
| `BbStdMultiplier` | 2.2 | **2.0** | %4.5 → %9.1 frekans (~2x) |
| `RsiOversoldThreshold` | 30 | **35** | Daha erken dip yakalama |
| `VolumeZScoreThreshold` | 0.5 | **0.3** | Konfirmasyon korunur, eşik daha düşük |

**KORUNUR (Loop 58 anti-disaster):**
- EMA200 trend filter: `close > Ema200_15m` ✓
- BTC-only: ✓
- MaxOpenPositions=1: ✓
- KlineInterval=15m: ✓
- TpAtr 2.5×, SlAtr 0.7× (R:R 3.57:1): ✓
- Cooldown 8 bar: ✓

## Risk Profili Sıkılaştırma (binance-expert)

| Parametre | Eski | **Loop 60** |
|---|---|---|
| `MaxConsecutiveLosses` | 8 | **3** (tatil unsupervised güvencesi) |
| `MaxDrawdown24hPct` | 0.20 | **0.05** ($25/24h limit) |
| `MaxDrawdownAllTimePct` | 0.40 | **0.10** ($50 toplam limit) |

3 ardışık kayıp → otomatik halt → kullanıcı tatilden döndüğünde inceler.

## Beklenti
- Frekans: günde 2-5 sinyal (0'dan anlamlı artış)
- WR: %30+ hedef (BE WR ~%22, R:R 3.57:1)
- EMA200 anti-disaster koruması aktif (Loop 58 tekrarlamaz)

## Sıradaki: Loop 60 Boot (DB reset YOK)
1. appsettings patch (1 BB MeanRev v2 strategy + RiskProfile)
2. dotnet kill + restart (DB korundu, $500 zaten temiz)
3. Loop 60 boot rapor
4. ScheduleWakeup t60

— PM 2026-04-30 Loop 59 halt @ t=480 (8h sermaye koruma → orta yol)
