# 0023. Loop 35 Risk-First Parametre Reformu — R:R 1:2.5, BE_WR %28.6

Date: 2026-04-24
Status: Proposed (Loop 34 halt sonrası)
Relates to: ADR-0018, ADR-0020, ADR-0021, ADR-0022

> **Özet:** Loop 34'te geniş SL (SlAtrMult 1.3) + dar TP (TpAtrMult 0.9) asimetrisi 4 ardışık SL hit ile -$1.01 zarara yol açtı. Loop 35 **risk-first** ayar: SL daraltılır (SlAtrMult 0.6, MinSl %0.12), TP genişletilir (TpAtrMult 1.5, MinTp %0.40), MaxHold kısalır (8 dk), volume filtresi orta-sıkı (1.2x). R:R 1:2.5 → break-even WR **%28.6** — ulaşılabilir eşik. Sermaye $500 sabit (ADR-0022), sizing %20 sabit (ADR-0021). Altyapı değişmez.

## Context

### 23.1 Loop 34 Halt (`loops/loop_34/halt-report.md`)

| Metrik | Değer | Hedef |
|---|---|---|
| Uptime | 41 dk | 4 saat |
| Trade | 7 | ≥ 10 |
| WR | **%28.6** (2W/5L) | ≥ %45 |
| Realized net | **-$0.93** | > +$0.50 |
| TP Hit | 1 (SOL 348) | ≥ 3 |
| netPnl | **-$1.01** | - |

**4 SL hit** toplam -$1.0 kayıp. Ana kaybediciler: ADA -$0.41, SOL -$0.31, ETH -$0.12, XRP -$0.16.

### 23.2 Kök Sorun — Asimetri

Loop 34 param seti:
- `SlAtrMult 1.3` + `MinSlPct %0.25` + `MaxSlPct %0.80` → SL hit'te kayıp **$0.25-$0.80**
- `TpAtrMult 0.9` + `MinTpPct %0.30` + `MaxTpPct %0.80` → TP hit'te kazanç **$0.30-$0.80**

Görünüşte R:R 1:1 olmalı ama **1m timeframe 15dk MaxHold içinde**:
- %0.30 TP hit olasılığı **düşük** (küçük hareket)
- %0.80 SL hit olasılığı **yüksek** (volatilite normalde %0.5-1 hareket üretir 15 dk'da)
- Sonuç: SL dominant, TP seyrek → gerçek dağılım **asimetrik kayıp**

### 23.3 R:R Matematik

Break-even WR formülü: `BE_WR = SL / (SL + TP)` (fee ihmal).

| Param Set | SL | TP | R:R | BE_WR |
|---|---|---|---|---|
| Loop 33 | %0.15-0.50 | %0.40-0.50 | ~1:1 | %50 |
| Loop 34 (halt) | %0.25-0.80 | %0.30-0.80 | ~1:1 | %50 (ama SL hit %80+) |
| **Loop 35 önerisi** | **%0.12-0.30** | **%0.40-0.80** | **1:2.5** | **%28.6** |

Loop 34 gerçek WR %28.6 idi — aynı gerçek WR ile Loop 35 paramlarında **break-even**. Hafif daha iyi WR → kar.

## Decision

### 23.4 Yeni Parametreler (tüm aktif stratejiler)

| Parametre | Loop 34 | Loop 35 | Gerekçe |
|---|---|---|---|
| `SlAtrMultiplier` (AtrScalper) | 1.3 | **0.6** | Dar SL, küçük kayıp |
| `MinSlPct` (AtrScalper) | 0.0025 | **0.0012** | Hiç olmazsa %0.12 SL |
| `MaxSlPct` (AtrScalper) | 0.008 | **0.003** | Max %0.30 SL |
| `TpAtrMultiplier` (AtrScalper) | 0.9 | **1.5** | Uzun TP |
| `MinTpPct` (AtrScalper) | 0.003 | **0.004** | Min %0.40 TP |
| `MaxTpPct` (AtrScalper) | 0.008 | **0.010** | Max %1.00 TP |
| `TpGrossPct` (ETH/XRP) | %0.30 | **%0.45** | Uzun TP |
| `StopPct` (ETH/XRP) | %0.25 | **%0.15** | Dar SL |
| `MaxHoldMinutes` | 12-15 | **8** | Zaman tuzağı azalt |
| `VolumeMultiplier` | 0.8 | **1.2** | Orta-sıkı filtre |
| `SlopeTolerance` | -0.002 | **-0.001** | Orta-sıkı trend |

Diğer sabit:
- StartingBalance $500 (ADR-0022)
- SnowballSizing %20 / $20.10 floor (ADR-0021)
- Trade size $100
- MaxOpenPositions 3
- BTC + BNB Paused
- Aktif: SOL-AtrScalper, ADA-AtrScalper, ETH-MicroScalper, XRP-MicroScalper

### 23.5 Fee Matematik (Loop 35)

- Fee/trade (BNB disc): $100 × %0.075 = $0.075
- Min TP net: $0.40 − $0.075 = **$0.325** (kazanç bir SL'i kapatır)
- Min SL net: -$0.12 − $0.075 = **-$0.195** (kayıp sınırlı)
- R:R net = 0.325 / 0.195 = **1.67** (uygulanabilir BE_WR %37.5)

### 23.6 Beklenen Dağılım

10 trade → tahmini:
- 4 TP hit × $0.325 = +$1.30
- 4 SL hit × -$0.195 = -$0.78
- 2 TimeStop × -$0.05 ortalama = -$0.10
- Net ~**+$0.42**

Bu tahmin %40 TP hit oranına dayanır (Loop 34 gerçek %14). Param sıkılaşma sayesinde TP hit artış beklentisi **+%15-25** (toplam %30-40).

### 23.7 Başarı Kriteri (Loop 35 4h)

- Min 10 kapalı trade
- WR ≥ **%40** (BE_WR %37.5 üstü)
- ≥ 3 TP hit
- realized net > **+$0.30**
- Max drawdown < **-$0.50**

Halt kriteri: realized net < -$0.30 (Loop 34 -$0.93'e göre sıkılaştırılmış).

## Consequences

### 23.8 Positif
- Her kayıp max $0.20 (SL dar) — ardışık 5 kayıp dahi -$1 altında
- TP kazancı fee overhead'i kolayca aşar ($0.325 net)
- Volume + Slope filtresi kaliteli sinyal

### 23.9 Risk
- Dar SL → **"SL yalaması"** (fiyat kısa süre SL'i kıpırdatıp dönüş) olasılığı artar
- Volume 1.2x → trade sayısı düşer (4h'de 10 yerine 6-8)
- TP %0.40+ 8 dk içinde hit olabilir mi? AR-GE'de belirsiz — yeni param test gerektirir

### 23.10 Erken Çıkış
- 2h içinde realized < -$0.30 → Loop 35 halt, Loop 36 için strateji değişimi (5m timeframe veya farklı sembol)

## Alternatifler

- **R:R 1:1.5** — daha konservatif, BE_WR %40; Reddedildi: Loop 34 WR %28.6 idi, %40 ulaşılamaz
- **R:R 1:3** — daha agresif, BE_WR %25; Reddedildi: TP %0.60+ 8 dk içinde hit oranı düşük
- **Timeframe 1m → 5m** — volatilite paternleri daha güvenilir; Reddedildi (şimdilik): kod değişimi gerektirir (strategyEvaluator.KlineInterval), ADR-0024 scope

## Kaynak

- `loops/loop_34/halt-report.md`
- `loops/loop_33/strategy-arge.md` (AR-GE feasibility)
- ADR-0018 §18.x MicroScalper taban
- ADR-0020 fee-aware Position (cash-symmetric doğrulandı)
- ADR-0021 sizing %20
- ADR-0022 starting $500
