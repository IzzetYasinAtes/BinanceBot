# Loop 62 Boot — EmaScalper TP/SL Daralt (2026-04-30 10:19 TR)

## Pivot Sebebi
Loop 61 t60: 30 emit/h ✓ ama WR %20 → RiskAlert otomatik halt. R:R 3.33 büyük TP'ye 2/10 ulaşılabildi.

**Çözüm:** TP daralt (BE WR daha gerçekçi).

## Loop 62 Parametre Değişikliği

| Parametre | Loop 61 | **Loop 62** | Etki |
|---|---|---|---|
| `TpAtrMultiplier` | 2.0 | **1.2** | TP %0.30 floor |
| `SlAtrMultiplier` | 0.6 | **0.5** | SL %0.20 |
| `MinTpPct` | 0.005 | **0.003** | %0.30 |
| `MaxTpPct` | 0.012 | **0.008** | |

**Korunur (kullanıcı kuralı: 5 coin sürekli işlem):**
- 5 coin: BTC, ETH, XRP, SOL, ADA
- RSI 35-70, Vol 0.5, MinAtr 0.0002 (frekans için)
- MaxHold 10dk, Cooldown 3 bar
- MaxOpenPositions=5

R:R = 1.2/0.5 = **2.4:1** (yine pozitif beklenti)
BE WR ≈ 0.20/(0.30+0.20) = **%40**

## Boot State (DB Reset YOK)
| Metrik | Değer |
|---|---|
| Cash | $99.20 (Loop 61'den 4 açık pos kilit) |
| Equity | **$499.41** |
| Realized | -$0.566 (Loop 61) |
| Open Pos | 4 (Loop 61'den, 10dk içinde TimeStop) |
| Active | 5 EmaScalper1m (re-aktive) |
| Loop 61 → 62 transition | yumuşak (DB korundu) |

## Beklenti
- Frekans korunur: 25-35 emit/h
- WR hedef: %40+ (mevcut %20'den artış)
- Net trend: BE veya hafif kar

## Halt Eşikleri
- Realized < -$15 (24h MaxDD %3) → otomatik halt
- 5+ ardışık SL → otomatik halt (RiskProfile)
- WR < %35 (15+ trade) → Loop 63 binance-expert

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (10:49 TR)**

— PM 2026-04-30 Loop 62 boot
