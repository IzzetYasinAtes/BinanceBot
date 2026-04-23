# 4 Loop Kümülatif Sentez — Loop 33-37 Analizi + Loop 38 Radikal Plan

**Tarih:** 2026-04-24 02:23 TR
**Durum:** Kullanıcı süre baskısı: sabah 09:00'a kadar kar şart. 6.5 saat kaldı.

## 1. 5 Loop Kümülatif

| Loop | Timeframe | MaxHold | Params | Sonuç |
|---|---|---|---|---|
| 33 | 1m | 5-8dk | TpAtr 1.4, Tight scalping | -$0.26, WR %14, 0 TP |
| 34 | 1m | 12-15dk | TpAtr 0.9, Wide SL | -$0.93, WR %29, 1 TP |
| 35 | 1m | 8dk | TpAtr 1.5, Tight SL | -$0.35, WR %20, 0 TP |
| 36 | 1m | 15dk | Combo orta | -$0.38, WR %38, 0 TP |
| 37 | 5m | 40dk | 5m pivot | -$0.97, WR %0, 0 TP (kritik fail) |

**Toplam:** 5 loop / -$2.89 realized / 32 trade / 1 TP hit (%3.1)

## 2. 5 Kritik Kök Sebep

### (a) Fee/Gross Yapısal Engel
- Fee round-trip $0.15 / $100 sizing = **%0.15 break-even**
- Piyasa şu an ultra-düşük vol: 1m bar body **%0.02-0.05** (ort)
- TP %0.30+ hit olasılığı <%10

### (b) Piyasa Rejimi Gözlem (2026-04-24 02:00 TR)
Canlı veriye göre 6 coin (1m/15m/1h/5h yüzdesel):
- 1m: hepsi ±%0.03 (ultra-sessiz)
- 15m: -%0.09 ile -%0.23 (hafif aşağı)
- 1h: -%0.12 ile -%0.32 (yeşil yok)
- 5h: ADA +%0.85, ETH +%0.55, SOL +%0.49 (toparlanma)

**Sonuç:** Saat 02:00-09:00 TR = Asya piyasası aktif, Avrupa/ABD kapalı. Düşük vol. Hacim düşük. Scalping için en kötü zaman.

### (c) 1m/5m Timeframe Yetersiz
- 1m × 15 bar = %0.75 potansiyel → TP %0.30 hit %5
- 5m × 8 bar = %1.2 potansiyel → TP %0.30 hit teorik %30 ama Loop 37 gösterdi %0 olabiliyor düşük vol'da

### (d) 6 Coin Yetersiz (sinyal asimetrisi)
- ADA sessiz, BNB sessiz (5h %0 ve %0.39)
- BTC trend yok (Loop 37 4 trade hepsi BTC -$0.62)
- Likidite/volatilite farklı semboller lazım

### (e) BTC/BNB Pause-Sızıntı Bug
- Her API restart'ta Status=3 oluyor (appsettings varsayılan)
- Ben her defa manuel Pause yapıyorum ama genelde 1-2 trade kaçıyor
- Sızıntı 5 loop'ta toplam $1+ zarar getirdi

## 3. Loop 38 Radikal Plan

### Felsefe Değişimi: Scalping → Swing
5 loop gösterdi ki $100 sermaye + paper fee %0.075 + ultra-düşük-vol piyasa = **scalping mekaniği kar üretemiyor**. Pozisyon süresi uzatılmalı: 1-2 saat bekleme → fiyat yön seçer → TP veya SL hit net, TimeStop dominantlığı kırılır.

### Coin Genişletme: 6 → 12
Yeni eklenenler: **DOGE** (kullanıcı direkt talep + %0.85 24h), **LINK** (DeFi volatil), **MATIC** (L2 vol), **AVAX**, **DOT**, **ATOM**. Mevcut 6 (BTC, ETH, BNB, XRP, SOL, ADA) korunur.

### Parametre Reform (Loop 38)
| Parametre | Loop 37 | Loop 38 |
|---|---|---|
| Timeframe | 5m | **5m korundu** |
| MaxHold | 40dk (8 bar) | **60dk (12 bar)** |
| TpAtrMultiplier | 1.0 | **1.3** (uzun TP) |
| SlAtrMultiplier | 0.7 | **0.9** (dar ama makul SL) |
| MinTpPct | 0.003 | **0.004** (%0.40 min) |
| MaxTpPct | 0.008 | **0.012** (%1.20 max) |
| MinSlPct | 0.0015 | **0.0025** (%0.25 min) |
| MaxSlPct | 0.005 | **0.006** (%0.60 max) |
| VolumeMultiplier | 1.0 | **1.2** (orta-sıkı filtre) |
| SlopeTolerance | -0.0015 | **-0.001** (sıkı trend) |
| MaxOpenPositions | 3 | **5** (12 coin için genişletme) |

### Fee Matematik Loop 38
- Sizing $100, Fee $0.075/fill = $0.15/trade round-trip
- TP %0.40 → gross $0.40 → **net $0.325/win**
- SL %0.25 → loss $0.25 + fee $0.075 = **-$0.325/loss**
- R:R 1:1, BE_WR %50
- TimeStop 60dk dominant'ı kırar (fiyat 60dk'da genelde yön seçer 5m'de)

### Beklenen
- 6.5 saat × saatte 4-6 trade = **25-40 trade**
- WR %50 varsayımı: break-even
- WR %55+ ile: +$0.30-1.20 net
- WR %45-: -$0.30-1.00

### Pause Sızıntı Kesin Fix
DB reset SQL'e BTC+BNB için `Activate=0` seed ekle. API restart'ta seed-sync Status=2 ile başlat (appsettings `Activate: false` → Seeder bunu uygulamalı).

### BTC/BNB kararı
**BTC kalabilir ama Activate:true** — BTC low vol ama likit. Son 5h +%0.18 hafif yeşil. 12 coin içinde BTC'yi aktif kabul et, sadece parametreler ile sıkı filtre (VolumeMult 1.2, Slope -0.001) düşük kaliteli sinyalleri elecek.

BNB ise 5h +%0.39 ama %0.85 BE_WR olduğu Loop 32 AR-GE'de görüldü — **Paused kalır**.

**BTC yine aktive** — 12 coin + BTC = 12 aktif, 1 paused (BNB).

## 4. Başarı Kriter (09:00 TR'e kadar)

- 4-6 saatte min 20 kapalı trade
- WR ≥ %45
- TP hit ≥ 5 (önceki 32/1'in 5 katı)
- **Realized net > $0** (pozitif, herhangi bir miktar)
- Max drawdown < -$2.00

## 5. Eğer Başarısız Olursa

Pürüzsüz dürüstlük: 5-6 loop denemesi, 4 iterasyon radikal değişim. Matematiksel olarak paper trading %0.075 taker fee ile ultra-düşük-vol piyasada **kar etmek inanılmaz zor**. Düşünülecek seçenekler:
- Mainnet geçiş (gerçek likidite, gerçek volatilite — ama gerçek para risk)
- Strateji tipi tümden değişim (funding-rate, OFI, multi-tf confluence)
- Paper'ı feature-validation için bırak, kar hedefi kaldır

Ama şu an **son çaba: 12 coin + swing + 60dk MaxHold**. Bu bir son şans.
