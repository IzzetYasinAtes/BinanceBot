# Loop 82 -- binance-expert Spec: Trailing/BE Kalibrasyon

Tarih: 2026-05-02 | Agent: binance-expert | Task: loop82-trailing-be-calibration

---

## BOLUM 1: Post-Mortem -- Loop 81 4 Close Analiz

### 4 Close Detay (Gercek Veri)

| Sira | Symbol | Hold | Peak UPnL | Exit Tipi | PnL Net | Teshis |
|------|--------|------|-----------|-----------|---------|--------|
| 1 | SOL | 69min | +0.33% | trailing-exit | -0.003 USD | Trail buffer 0.15%, hizli mean-reversion, entry yakin exit |
| 2 | ETH | 109min | +0.26% | trailing-exit | -0.055 USD | Ayni kalip. 0.15% buffer dar, geri cekilme = entry yakin cikis |
| 3 | XRP-1 | 99min | +0.11% | BE-stop | -0.141 USD | BE move entry+0.02% stop, geri donus + slippage = net loss |
| 4 | XRP-2 | ? | +0.16% | BE-stop | -0.184 USD | Ayni BE kalip. Peak kucuk, BE aninda armed, geri donus |

Toplam Realized: -0.38 USD. CB tripped (MaxConsecutiveLosses=4).

### Net P&L Matematigi (Testnet Gercegi)

Testnet slippage:
  PaperFill.FixedSlippagePct = 0.0001 round-trip = 0.0002 (%0.02)
  UYARI: Mainnet Binance VIP0 standart taker %0.10 x2 = %0.20 (10x fark).
  Kaynak mainnet fee: https://www.binance.com/en/fee/schedule

Trailing exit breakeven (testnet slippage %0.02):
  peakPct >= trailPct + slippageRound = 0.0015 + 0.0002 = 0.0017 (%0.17)

SOL +0.33%, ETH +0.26% her ikisi de %0.17 ustunde -- teoride net pozitif olmali.
GERCEK KAYIP NEDENI: Hizli mean-reversion. Trailing arming sonrasi %0.15 geri cekilme
= trailing stop tetikleniyor, ama mark o noktada entry bolgesi.

BE-stop (XRP):
  BE stop = entry x 1.0002. Net hedef: +0.02% - 0.02% slippage = 0.
  Gercek: -0.14 USD, -0.18 USD -> stop hit entry altina kaydi veya slippage basti.

KOK NEDEN (ikili):
  A) Trailing buffer dar (%0.15): hizli mean-reversion toleransi yok.
  B) BE offset dar (%0.02): stop entry yakini, slippage yer.
---

## BOLUM 2: Senaryo Analizi

### Senaryo 1: Trailing Buffer Genisletme (0.0015 -> 0.0025/0.0030/0.0040)

Breakeven esigi (testnet slippage 0.0002): peakPct >= trailPct + 0.0002

| TrailPct | Breakeven Peak | L81 SOL(+0.33%) | L81 ETH(+0.26%) | L81 XRP(+0.11/0.16%) |
|----------|---------------|-----------------|-----------------|----------------------|
| 0.0015 | 0.17% | GECMELI (gecmedi) | GECMELI | Hayir |
| 0.0025 | 0.27% | GECECEK | Sinirda (0.26~0.27) | Hayir |
| 0.0030 | 0.32% | GECECEK | Hayir | Hayir |
| 0.0040 | 0.42% | Hayir | Hayir | Hayir |

Optimal: TrailPct=0.0025.
  SOL +0.33% > 0.27% -> net pozitif (iyilesme).
  ETH +0.26% sinirda, marginal pozitif/sifir.
  XRP dogrudan SL/TP (BE almaz, trail almaz).

Neden 0.0030 degil: 0.0030 ile ETH kaybeder (0.26% < 0.32%). Dengeli secim: 0.0025.

### Senaryo 2: BE Esigi Yukseltme (0.0010 -> 0.0020)

Sorun: XRP +0.11%, 0.16% -> BE trigger (>0.10%) -> stop entry+0.02% -> geri donus -> loss.

| BE TriggerPct | XRP +0.11% | XRP +0.16% | SOL +0.33% | ETH +0.26% |
|---------------|-----------|-----------|-----------|-----------|
| 0.0010 | TRIGGER (kotu) | TRIGGER | TRIGGER | TRIGGER |
| 0.0015 | trigger yok | TRIGGER | TRIGGER | TRIGGER |
| 0.0020 | trigger yok | trigger yok | TRIGGER | TRIGGER |
| 0.0025 | trigger yok | trigger yok | trigger yok | trigger yok |

Optimal: BE TriggerPct=0.0020.
  XRP kucuk peak: BE ALMAZ -> dogrudan SL/TP (kucuk kayip yerine buyuk SL, ama CB korur).
  SOL/ETH: BE alir, trailing armed, yeni parametrelerle net pozitif.

BE OffsetPct revize 0.0002 -> 0.0010:
  Eski: stop = entry+0.02% -> slippage = net kayip.
  Yeni: stop = entry+0.10% -> fiyat buraya dusmeden exit yok = daha guclu buffer.

### Senaryo 3: R:R Rebalans

Net R:R (testnet slippage 0.0002 dahil, MinSlPct=0.006):
  Net win = SL_pct x RR - 0.0002
  Net loss = SL_pct + 0.0002 = 0.0062

| R:R | Net win | Net loss | Net R:R | Breakeven WR |
|-----|---------|----------|---------|--------------|
| 1:1.5 | 0.0088 | 0.0062 | 1.42 | %41 |
| 1:2.0 | 0.0118 | 0.0062 | 1.90 | %34 |
| 1:2.5 | 0.0148 | 0.0062 | 2.39 | %29 |

R:R 1:2.0 zaten iyi (breakeven WR %34). Asil sorun: TP cok uzak, ulasim sifir.
Cozum: MinSlPct dusur -> TP yaklasir. MinSlPct=0.004 ile TP=%0.8 (onceki %1.2 yerine).

MinSlPct=0.004, R:R=2.0:
  Net win = 0.004 x 2.0 - 0.0002 = 0.0078
  Net loss = 0.004 + 0.0002 = 0.0042
  Net R:R = 1.86, Breakeven WR = %35

Oneri: R:R degistirme yok. MinSlPct=0.004, TpRiskRewardRatio=2.0 (koru).

### Senaryo 4: Komisyon-Aware Trailing (dinamik buffer)

effectiveTrail = max(configTrail, slippageRound) = max(0.0015, 0.0002) = 0.0015
Slippage zaten kapaniyor. Asil sorun: peak volatilitesi (hizli geri cekilme).
Oneri: TrailPct=0.0025 ile volatility-aware buffer.

### Senaryo 5: MinHold Suresi (2-3 bar)

Loop 81 hold surelerini kontrol: SOL 69min, ETH 109min, XRP 99min. Hepsi uzun.
MinHold 10-15dk = etki yok. Sorun peak buyuklugu, hold suresi degil. Gereksiz.
---

## BOLUM 3: Optimal Parametre Seti (Loop 82)

| Parametre | Loop 81 | Loop 82 Oneri | Etki |
|-----------|---------|---------------|------|
| BreakEven.TriggerPct | 0.0010 | 0.0020 | XRP kucuk peak BE almaz |
| BreakEven.OffsetPct | 0.0002 | 0.0010 | Stop entry+0.10%, geri donus toleransi |
| TrailingStop.TrailPct | 0.0015 | 0.0025 | Hizli mean-reversion tolere, SOL net+ |
| MinSlPct (ParametersJson) | 0.006 | 0.004 | TP yaklasir (0.8%), ulasma sansi artar |
| MaxSlPct (ParametersJson) | 0.012 | 0.008 | Cap kisalir |
| BeMoveTriggerPct (ParametersJson) | 0.001 | 0.002 | Audit sync |
| BeMoveOffsetPct (ParametersJson) | 0.0002 | 0.001 | Audit sync |
| TpRiskRewardRatio | 2.0 | 2.0 | Degismez |

Dosya: src/Api/appsettings.json
---

## BOLUM 4: Beklenen Sonuclar

### L81 Verisi Uzerinde L82 Simulasyonu

SOL +0.33%, TriggerPct=0.002, TrailPct=0.0025:
  BE trigger: 0.33% > 0.20% -> TRIGGER. newStop = entry x 1.001 (entry+0.10%)
  Trailing armed. trailingStop = peak x (1 - 0.0025) = peak - 0.25%
  Trailing exit fiyati: entry + 0.33% - 0.25% = entry + 0.08%
  Net: +0.08% - 0.02% slippage = +0.06%. POZITIF (L81: -0.003 USD -> L82: +kucuk)

ETH +0.26%, TriggerPct=0.002, TrailPct=0.0025:
  BE trigger: 0.26% > 0.20% -> TRIGGER. newStop = entry x 1.001.
  Trailing exit: entry + 0.26% - 0.25% = entry + 0.01%.
  Net: +0.01% - 0.02% = -0.01% (kucuk). L81: -0.055 USD -> L82: ~-0.02 USD

XRP +0.11%, TriggerPct=0.002:
  BE trigger: 0.11% < 0.20% -> BE ALMAZ, trailing yok.
  Dogrudan SL (0.4%) veya MaxHold timestop.
  Trade-off: L81 BE-stop -0.14 USD vs L82 SL -buyuk. CB=4 korur.

### KPI Hedefleri (Loop 82)

| KPI | L81 Gercek | L82 Hedef | Halt Tetigi |
|-----|-----------|-----------|-------------|
| WR | 0% (0/4) | >=45% | <25% (>8 trade) |
| Realized 4h | -0.38 USD | >=-0.30 USD | <-1.50 USD |
| CB Trip | 1 | 0 | >=1 |
| Trailing exit net + | 0/4 | >=1 | 0 pozitif trailing (4h) |
| Emit/h | ~1.3/h | >=4/h | <2/h (60dk+) |
---

## BOLUM 5: Test Stratejisi

Kod degisikligi: SIFIR. Sadece appsettings.json konfigürasyon.

### Manuel Dogrulama (Boot Sonrasi)

1. CB reset (zorunlu, L81 CB tripped):
   POST /api/risk/circuit-breaker/reset
   Header: X-Admin-Key: dev-admin-key-change-me

2. appsettings.json kontrol (restart oncesi):
   BreakEven.TriggerPct = 0.002
   BreakEven.OffsetPct = 0.001
   TrailingStop.TrailPct = 0.0025
   ParametersJson her 5 coin: MinSlPct=0.004, BeMoveTriggerPct=0.002, BeMoveOffsetPct=0.001

3. Log dogrulama (t30 check):
   Beklenti: BE-MOVE applied ... triggerPct=0.002
   Beklenti: TRAILING peak-up ... trailPct=0.0025

4. Trailing exit net pozitif kontrol:
   Ilk trailing exit: exitPrice > entryPrice olup olmadigi izle

Regresyon riski: Dusuk. MarkToMarketWorker kodu degismedi. Sadece threshold buyudü.
---

## BOLUM 6: Kirmizi Bayraklar

### Red Flag 1 -- XRP Buyuk SL Riski

Risk: XRP BE almaz, trailing almaz -> dogrudan SL (MinSlPct=0.4%).
4 ardisik XRP SL -> CB. MaxConsecutiveLosses=4 zaten aktif.
t60 izle: 3+ XRP SL -> RequiredScore XRP icin yukselt (WeightOverride).

### Red Flag 2 -- BE Stop Sonrasi Hizli Geri Donus

BE offset=0.10% daha iyi ama hala risk: SOL/ETH hizli 0.20%+ geri donerse stop hit.
Simülasyon: ETH +0.26%, geri cekilis 0.15% = exit entry+0.01% -> neredeyse sifir.
Nihai cozum: peak buyutmek = daha iyi pattern sinyal kalitesi.

### Red Flag 3 -- MinSlPct Daralma ve XRP Volatilitesi

XRP 5m ATR genellikle 0.3-0.5%. MinSlPct=0.004 ile SL dar.
Gercekte: SL = max(ATR x 1.2, 0.4%). ATR=0.35% -> SL=0.42% (MinSlPct dominant).
ATR=0.45% -> SL=0.54% (ATR dominant). Genelde uygun, sik tetiklenme olmaz.

### Red Flag 4 -- Mainnet Geciste Tekrar Kalibrasyon

Bu spec testnet slippage=0.02% uzerine. Mainnet: taker 0.10% x2 = 0.20% round-trip.
Mainnet gecisinde:
  BE trigger: 0.20% -> 0.40% (komisyon kapsamak icin).
  TrailPct: 0.25% -> 0.45%.
Mainnet gecisi oncesi binance-expert tekrar cagirilmali.

---

## BOLUM 7: Backend-Dev Aksiyon Listesi

### AKSIYON-1 (ZORUNLU -- L82 Baslayabilmek Icin)

CB tripped. Reset:
  POST /api/risk/circuit-breaker/reset
  Header: X-Admin-Key: dev-admin-key-change-me

### AKSIYON-2: appsettings.json BreakEven Section Guncelle

  Dosya: src/Api/appsettings.json
  BreakEven: { Enabled: true, TriggerPct: 0.0020, OffsetPct: 0.0010 }

### AKSIYON-3: appsettings.json TrailingStop Section Guncelle

  Dosya: src/Api/appsettings.json
  TrailingStop: { Enabled: true, TrailPct: 0.0025 }

### AKSIYON-4: Tum 5 Coin ParametersJson Guncelle

  Dosya: src/Api/appsettings.json (Strategies.Seed her 5 entry)
  Degerler: MinSlPct=0.004, MaxSlPct=0.008, BeMoveTriggerPct=0.002, BeMoveOffsetPct=0.001
  TpRiskRewardRatio=2.0 ve diger tum parametreler degismez.

  Hedef ParametersJson (5 coin icin ayni):
  {RequiredScore:5,SlAtrMultiplier:1.2,MinSlPct:0.004,MaxSlPct:0.008,
   TpRiskRewardRatio:2.0,MaxHoldMinutes:60,CooldownBarsAfterSignal:2,
   AdxRegimeMin:15,AdxRegimeMax:35,AdxOutsideRegimeMultiplier:0.7,
   BeMoveTriggerPct:0.002,BeMoveOffsetPct:0.001}

Not: BeMoveTriggerPct ve BeMoveOffsetPct ParametersJson audit amacli.
Gercek BE MarkToMarketWorker -> BreakEvenOptions (AKSIYON-2) kullanir.
Her ikisi senkron olmali.

---

## Kaynaklar

- Binance VIP0 mainnet fee: maker/taker her biri %0.10.
  Kaynak: https://www.binance.com/en/fee/schedule
- Binance testnet komisyon: sifir (sanal). PaperFill slippage simüle eder.
  Kod incelendi: src/Api/appsettings.json PaperFill.FixedSlippagePct=0.0001.
- Trailing stop breakeven formulü: peakPct >= trailPct + feeRound
- Net R:R breakeven WR: WR_min = 1 / (1 + netRR)
- Binance testnet: https://testnet.binance.vision

---

*binance-expert agent | 2026-05-02 | Loop 82 Trailing/BE Kalibrasyon Spec Tamamlandi*