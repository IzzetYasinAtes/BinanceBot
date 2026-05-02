# Loop 83 -- binance-expert Spec: BE Offset Artis + Trailing Genisletme

Tarih: 2026-05-02 | Agent: binance-expert | Task: loop83-be-offset-radical

---

## BOLUM 1: Post-Mortem -- Loop 82 3 Close Ortak Desen

### 3 Close Detay

| Sira | Symbol | Peak | Exit Tipi | PnL Net | Tespit |
|------|--------|------|-----------|---------|--------|
| 1 | ETH | +%0.25 | trailing-exit | -/usr/bin/bash.069 | Peak esik 0.27 altinda (trail buffer dar) |
| 2 | BTC | +%0.23 | trailing-exit | -/usr/bin/bash.060 | Peak esik 0.27 altinda |
| 3 | ADA | +%0.27 | BE-stop | -/usr/bin/bash.090 | BE armed, offset 0.10% = net +0.08%, yetmedi |

Ortak desen: Peak %0.23-0.27 araliginda. BE trigger %0.20 oluyor. Stop = entry + 0.10%.
Fiyat geri cekiliyor, BE-stop hit ediyor. Net = +0.10% - 0.02% slippage = +0.08%.
KAYIP NEDENI: +0.08% kazanc asimetri 5.25x kayip karsisinda yetmiyor. Beklenti negatif.

### Kok Neden (Matematiksel)

Mevcut config:
  BE armed -> stop = entry + 0.10% (OffsetPct=0.001)
  Net win = 0.10% - 0.02% = +0.08%
  SL loss = 0.40% + 0.02% = -0.42%
  Asimetri = 5.25x

Tarihsel P(BE triggered) = 5/7 trade ~ 71% (L81+L82 verisi)
Beklenti = 0.71 x (+0.08%) + 0.29 x (-0.42%) = -0.063% (NEGATIF)

BE stop net kazanci (0.08%) komisyon/slippage'i karsilamiyor.

---

## BOLUM 2: 5 Senaryo Karsilastirma Matrisi

Varsayimlar: testnet slippage 0.02% round-trip, P(BE)=71%, peaks ETH 0.25% / BTC 0.23% / ADA 0.27%.

| Senaryo | L82 Retroaktif | Beklenti | Pozitif | Karar |
|---------|----------------|----------|---------|-------|
| A: BE Kaldır | -1.26% (3x SL) | -0.42% | 0/3 | RED - max drawdown artar |
| B1: R:R 1:1.5 (TP=0.60%) | degisim yok | -0.063% | 0/3 | RED - TP ulasilamaz |
| B2: R:R 1:1.0 (TP=0.40%) | degisim yok | -0.063% | 0/3 | RED - TP yine ulasilamaz |
| C: Trail 0.005 | +0.24% via BE-stop | -0.063% | 3/3* | KISMI - beklenti negatifte |
| **E: BE offset 0.002** | **+0.54%** | **+0.009%** | **3/3** | **ONERILEN** |

(*) Senaryo C retroaktif pozitif gosteriyor CUNKU BE-stop zaten +0.08% veriyor. Beklenti degismiyor.
    Senaryo E beklentiyi de pozitife tasiyan tek secenektir.

### Senaryo A Detay (BE Kaldır)

L82 peak 0.23-0.27% < Trail 0.25% -> trailing exit yok -> SL hit.
Net: -0.42% x3 = -1.26%. Reddet.

### Senaryo B1/B2 Detay (R:R Dusur)

TP = MinSL x 1.5 = 0.60%, MinSL x 1.0 = 0.40%.
L82 peaks 0.23-0.27%: hicbiri bu seviyelere ulasamadi.
Trailing/BE-stop ile cikiyorlar, R:R degisimi etkisiz.

### Senaryo C Detay (Trail 0.005)

TrailPct 0.005 ile: peak 0.25% < trail esik 0.52% -> trailing exit yok.
BE armed -> BE-stop devreye giriyor (offset 0.001) -> net +0.08%.
Ayin sonuc. Trail genisligi sadece peak 0.50%+ piyasada fark yaratir.

### Senaryo E Detay: ONERILEN (BE Offset 0.001 -> 0.002)

BE armed -> stop = entry + 0.20% (OffsetPct=0.002)
Peak geri cekiliyor -> stop = entry+0.20% hit
Net = 0.20% - 0.02% slippage = +0.18%

L82 retroaktif:
  ETH: peak 0.25% > offset 0.20% -> BE-stop net +0.18% (POZITIF)
  BTC: peak 0.23% > offset 0.20% -> BE-stop net +0.18% (POZITIF)
  ADA: peak 0.27% > offset 0.20% -> BE-stop net +0.18% (POZITIF)

Beklenti = 0.71 x (+0.18%) + 0.29 x (-0.42%) = +0.0086% (POZITIF, ilk defa!)

---

## BOLUM 3: R:R + WR Expectancy Tablosu

Testnet slippage 0.0002 dahil, P(BE triggered) = 71% tarihsel.

| Config | Win | Loss | Beklenti | BK WR |
|--------|-----|------|----------|-------|
| L82 (offset 0.001) | +0.08% | -0.42% | -0.063% | %84 (imkansiz) |
| L83 E (offset 0.002) | +0.18% | -0.42% | +0.009% | %70 (ulasilabilir) |
| L83 E + MinSL 0.003 | +0.18% | -0.32% | +0.037% | %64 |
| L83 E + MinSL 0.002 | +0.18% | -0.22% | +0.066% | %55 |

L83 icin MinSL degistirme onerisi: MinSL=0.004 koru.
MinSL dusurme SL noise hit riskini arttirir. Beklenti iyilesmesi marginal.
Senaryo E tek basina yeterli.

---

## BOLUM 4: Onerilen Parametre Seti (Loop 83)

| Parametre | Loop 82 | Loop 83 | Etki |
|-----------|---------|---------|------|
| BreakEven.OffsetPct | 0.0010 | **0.0020** | BE-stop net +0.18% (eski +0.08%) |
| TrailingStop.TrailPct | 0.0025 | **0.0050** | Peak 0.50%+ gorurse trail bonus |
| BreakEven.TriggerPct | 0.0020 | 0.0020 | Degismez |
| MinSlPct (ParametersJson) | 0.004 | 0.004 | Degismez |
| MaxSlPct (ParametersJson) | 0.008 | 0.008 | Degismez |
| BeMoveOffsetPct (ParametersJson) | 0.001 | **0.002** | appsettings ile senkron |
| TpRiskRewardRatio | 2.0 | 2.0 | Degismez |

Dosya: src/Api/appsettings.json

Kod degisimi: SIFIR.

### TrailPct 0.0050 Ek Mantigi

L82 piyasasinda peak 0.23-0.27%: trail 0.0050 ile trail exit yok, BE-stop devreye giriyor.
AYNI SONUC, ama trail 0.0050 ek avantaj sagliyor:
Eger piyasa volatilitesi yukselip peak 0.50%+ gorurse -> trailing exit = net 0.25-0.48% (bonus).
L83 surasinda boyle senaryo gelirse kaca gecer. Maliyeti: yok.

---

## BOLUM 5: Kirmizi Bayraklar

### Red Flag 1 -- Peak 0.20-0.22% Araliginda Kayma Riski

BE trigger 0.20%, offset 0.20% ise: peak 0.20% = stop hic hit olmaz (peak = offset noktasi).
Fiyat geri donerken SL hit. Net: -0.42%.
t30 izle: 0.20-0.22% peak + SL count. 2+ -> TriggerPct 0.0025'e yukselt.

### Red Flag 2 -- MaxHold=60dk + Trail=0.005

Daha genis trail = fiyat daha uzun wait. 60dk timestop sik tetiklenebilir.
Timestop net = anlindaki fiyat - entry - slippage (belirsiz).
t60 izle: timestop exit sayisi. 3+ -> MaxHoldMinutes 90'a cek.

### Red Flag 3 -- XRP Dusuk Volatilite

XRP peak tipik 0.11-0.16%: BE trigger gelmez, SL hit (-0.42%).
Degisim bu riski degistirmiyor.
t60: XRP 3+ SL -> ParametersJson RequiredScore 5->6 (XRP icin signal filtre kus).

### Red Flag 4 -- Mainnet Gecisi (Hatirlatma)

Testnet slippage 0.02% vs mainnet 0.20% round-trip.
Mainnet icin: BE offset 0.002 -> 0.004, trail 0.005 -> 0.007.
Bu spec testnet icindir.
Kaynak: https://www.binance.com/en/fee/schedule

---

## BOLUM 6: Backend-Dev Aksiyon Listesi

### AKSIYON-1: CB Durumu Kontrol

GET /api/risk/health
Counter >= 4 ise: POST /api/risk/circuit-breaker/reset
Header: X-Admin-Key: dev-admin-key-change-me

### AKSIYON-2: BreakEven.OffsetPct Degistir

Dosya: src/Api/appsettings.json
Eski: OffsetPct: 0.0010
Yeni: OffsetPct: 0.0020

### AKSIYON-3: TrailingStop.TrailPct Degistir

Dosya: src/Api/appsettings.json
Eski: TrailPct: 0.0025
Yeni: TrailPct: 0.0050

### AKSIYON-4: ParametersJson BeMoveOffsetPct Sync (5 strategy)

Dosya: src/Api/appsettings.json
BeMoveOffsetPct: 0.001 -> 0.002

Hedef ParametersJson (5 coin icin ayni):
{RequiredScore:5,SlAtrMultiplier:1.2,MinSlPct:0.004,MaxSlPct:0.008,
TpRiskRewardRatio:2.0,MaxHoldMinutes:60,CooldownBarsAfterSignal:2,
AdxRegimeMin:15,AdxRegimeMax:35,AdxOutsideRegimeMultiplier:0.7,
BeMoveTriggerPct:0.002,BeMoveOffsetPct:0.002}

---

## BOLUM 7: Loop 83 KPI

| KPI | L82 Gercek | L83 Hedef | Halt Esigi |
|-----|-----------|-----------|------------|
| WR | 0/3 (0%) | >=30% | <20% (>10 trade) |
| Realized 4h | -/usr/bin/bash.219 | >=-/usr/bin/bash.30 | <-.50 |
| Avg/trade | -/usr/bin/bash.073 | >=-/usr/bin/bash.03 | ort -/usr/bin/bash.10 (>8 trade) |
| BE-stop pozitif | 0/3 | >=2/3 | 0 pozitif BE (>5 trade) |
| CB Trip | 0 | 0 | >=1 (auto halt) |
| Emit/h | ~0.5/h | >=2/h | <1/h (2h+) |

Beklenti (p=71%, offset=0.002): +0.009% per trade.
10 trade x 50 position ~ +/usr/bin/bash.14 expected gain (testnet).

---

## Kaynaklar

- Binance VIP0 taker fee: 0.10% per side, round-trip 0.20%
  Kaynak: https://www.binance.com/en/fee/schedule
- Testnet slippage: PaperFill.FixedSlippagePct=0.0001 (round-trip 0.02%)
  Kaynak: src/Api/appsettings.json
- BE stop formulü: stopPrice = entryPrice x (1 + OffsetPct); exit when markPrice <= stopPrice
- Beklenti: E = p_win x net_win + p_loss x net_loss
- Net win (BE-stop) = OffsetPct - slippageRoundTrip

*binance-expert agent | 2026-05-02 | Loop 83 Spec Tamamlandi*
