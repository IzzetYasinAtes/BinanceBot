# Loop 61 Boot — EmaScalper1m 5 Coin Frekans + Kartopu (2026-04-30 09:14 TR)

## Pivot Sebebi (Kullanıcı Direktifi)
Loop 59+60 toplam 16h **0 emit**, sermaye %100 korundu ama hiç işlem yok. Kullanıcı kızgın:
> "saate 150 dedik sen 15 saate hiç işlem yapmicak hale getirmişsin, kar topu şeklinde kar etmeye odakla, 5 coin"

## Yeni Altın Kural #12
**5 coin minimum + sürekli işlem (saatte 30+ hedef) + kartopu kar.** Sermaye koruma odaklı muhafazakar param YASAK. 0 emit > 1 saat → ANINDA pivot.

## Strateji: EmaScalper1m 5 Coin Gevşek (binance-expert D revize)

### Parametreler

| Parametre | Loop 56 (eski) | **Loop 61** | Gerekçe |
|---|---|---|---|
| Coin | 5 | **5** ✓ (BTC, ETH, XRP, SOL, ADA) | Korelasyon riski kabul, kullanıcı direktifi |
| `RsiLowerBand` | 35 | **35** | Geniş giriş penceresi |
| `RsiUpperBand` | 70 | **70** | |
| `VolumeMultiplier` | 0.8 | **0.5** | Düşük hacim de kabul (1m bar) |
| `TpAtrMultiplier` | 1.3 | **2.0** | Geniş TP (fee cover %0.50+) |
| `SlAtrMultiplier` | 0.8 | **0.6** | Dar SL |
| `MinTpPct` | 0.003 | **0.005** | %0.50 floor (fee %0.16 × 3) |
| `MaxTpPct` | 0.008 | **0.012** | |
| `MinSlPct` | 0.002 | **0.002** | |
| `MaxSlPct` | 0.005 | **0.004** | |
| `MaxHoldMinutes` | 10 | **10** | |
| `CooldownBarsAfterSignal` | 2 | **3** | 5 coin paralel için |
| `MinAtrPct` | 0.0003 | **0.0002** | XRP/ADA düşük fiyat için |

R:R = 2.0/0.6 = **3.33:1** (parametre), gerçek %0.50 TP / %0.20 SL = **1:1.06 net** (fee dahil), BE WR **%51.4**.

### RiskProfile

| Parametre | Eski | **Loop 61** |
|---|---|---|
| `MaxOpenPositions` | 1 | **5** ✓ |
| `MaxConsecutiveLosses` | 3 | **5** |
| `MaxDrawdown24hPct` | 0.05 | **0.03** ($15 limit, daha sıkı) |

## Beklenti (binance-expert)
- Frekans: **25-35 emit/h** (5 coin × 1m × %15-20 hit oranı)
- WR: %51-55 (BE WR %51.4)
- Net/h: **+$0.625** (orta senaryo)
- 8h: +$5
- 24h: +$15

Kullanıcı 150/h talebi matematiksel imkansız (BE WR %72 → ulaşılamaz). 30/h pratikte makul, tatilde +$15-30/gün hedef = **kartopu**.

## Boot State (DB Reset YOK)
| Metrik | Değer |
|---|---|
| Mode | Paper |
| Cash / Equity | $500 / $500 |
| Active | 5 (BTC/ETH/XRP/SOL/ADA EmaScalper1m gevşek) |
| MaxOpenPositions | 5 |
| API Port | 5188 |

## Halt Eşikleri
- Realized < -$15 (24h MaxDD %3) → otomatik halt
- 5+ ardışık SL → otomatik halt (RiskProfile)
- t60 = 0 emit → config/altyapı sorun (5 coin × 60dk = beklenir)
- WR < %40 (10+ trade) → Loop 62 binance-expert

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (09:44 TR)**

— PM 2026-04-30 Loop 61 boot
