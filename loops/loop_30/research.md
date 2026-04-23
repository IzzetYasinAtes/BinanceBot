# Loop 30 - Binance Expert Research

Tarih: 2026-04-19  
Task: loop-30-research

---

## Bolum 0 - Canli Exchange Verileri (Dogrulanmis)

Kaynak: GET https://api.binance.com/api/v3/exchangeInfo - 2026-04-19

### LOT_SIZE stepSize (canli)

| Symbol  | stepSize     | minQty       | tickSize |
|---------|-------------|--------------|----------|
| BTCUSDT | 0.00001000  | 0.00001000   | 0.01     |
| ETHUSDT | 0.00010000  | 0.00010000   | 0.01     |
| BNBUSDT | 0.00100000  | 0.00100000   | 0.01     |
| XRPUSDT | 0.10000000  | 0.10000000   | 0.0001   |

### NOTIONAL filter (canli)

Tum 4 sembol: minNotional=5.00 USD, applyMinToMarket=true, avgPriceMins=5.
avgPriceMins=5: market order notional son 5 dk agirlikli ort fiyat uzerinden kontrol edilir.

### Rate Limits (canli)

| Tip             | Interval  | Limit   |
|-----------------|-----------|---------|
| REQUEST_WEIGHT  | 1 MINUTE  | 6,000   |
| ORDERS          | 10 SECOND | 100     |
| ORDERS          | 1 DAY     | 200,000 |
| RAW_REQUESTS    | 5 MINUTE  | 300,000 |

21 trade/saat x 2 order = 42 order/saat. Rate limit sorun yok (kullanim %0.03).

### Fee (binance.com/en/fee/schedule dogrulanmis)

VIP0: %0.100 maker / %0.100 taker
VIP0 BNB discount (%25): %0.075 maker / %0.075 taker
---

## Bolum 1 - Sizing Olcek Analizi

### Mevcut Durum

- Equity: 100 USD paper
- EquityFraction: %1.0 target = 1.00 USD
- Binance minNotional floor: 5.00 USD (canli dogrulandi)
- Fiili sizing: max(1.00, 5.10) = 5.39 USD ortalama
- Loop 29 net: +0.051 USD / 4.5 saat = +0.011 USD/saat

### S1 - Starting Balance 1000 USD

| Parametre       | Mevcut  | S1        |
|-----------------|---------|-----------|
| Equity          | 100 USD | 1000 USD  |
| EquityFraction  | %1.0    | %1.0      |
| Target sizing   | 1.00    | 10.00     |
| Fiili sizing    | 5.39    | 10.00     |
| Max 4 pos acik  | 21.56   | 40.00     |
| Equity exposure | %21.6   | %4.0      |

Fiili olcek carpani: 10.00 / 5.39 = 1.85x (minNotional floor nedeniyle 10x degil 1.85x).

### S2 - EquityFraction %5 (equity 100 sabit)

100 x 0.05 = 5.00 USD. minNotional floor ile fark yok. ISE YARAMAZ.

### S3 - 500 USD x %2 = 10 USD

S1 ile ayni fiili sizing. S1 daha temiz (tek degisiklik: paper balance).

### Oneri: S1 (Starting Balance 1000 USD)

1. EquityFraction kodu degismez, sadece paper balance arttirilir.
2. 10 USD sizing BTC micro-slip oranini korur.
3. %4 max exposure (4 pos x 10 / 1000) guvenli.
4. Mainnet: 100-500 USD gercek sermaye, paper kanitlanirsa.
---

## Bolum 2 - BTC Micro-Slippage (10 USD Sizing ile)

BTC fiyat varsayim: 75000 USD. stepSize: 0.00001 BTC = 0.75 USD/adim.

| Sizing  | Target qty  | Round-down qty | Actual notional | Slip  |
|---------|-------------|----------------|-----------------|-------|
| 5.39    | 0.0000719   | 0.00007000     | 5.25 USD        | %2.6  |
| 10.00   | 0.0001333   | 0.00013000     | 9.75 USD        | %2.5  |

Sonuc: 10 USD sizing slip sorununu cozmez, ama kotulestirmez (oran ayni).
Asil BTC sorunu %30 WR. Bu parametre meselesi, sizing degil.

---

## Bolum 3 - Per-Coin Parametre Onerileri

BE_WR formulü: BE_WR = (SL + fee_RT) / (TP + SL + 2*fee_RT)
fee_RT (round-trip): %0.075 x 2 = %0.150

### ETH - Kanitlanmis (%50 WR, +0.051 USD, 394sn hold)

| Parametre     | Deger |
|---------------|-------|
| TP            | %0.30 |
| SL            | %0.15 |
| MaxHold       | 8dk   |
| VwapTolerance | 0.005 |
| BE_WR         | %40   |

Hesap: (0.15+0.15)/(0.30+0.15+0.30) = 0.30/0.75 = %40
WR %50 > BE_WR %40. Pozitif beklenti kanitlanmis. DEGISTIRME.

### BNB - Reform (%44 WR, -0.003 USD, 474sn hold)

Sorun: TP %0.30 cok uzak. 474sn hold ort MaxHold sinirinda dolaniyor.

| Parametre     | Mevcut | Loop 30 |
|---------------|--------|---------|
| TP            | %0.30  | %0.20   |
| SL            | %0.15  | %0.12   |
| MaxHold       | 8dk    | 5dk     |
| VwapTolerance | 0.005  | 0.005   |

BE_WR: (0.12+0.15)/(0.20+0.12+0.30) = 0.27/0.62 = %43.5
WR %44 = BE_WR %43.5 (marjinal). TP %0.20 ile daha fazla TP tetiklenirse WR artacak.

### BTC - Yeniden Aktif (konservatif)

| Parametre     | Loop 27 | Loop 30 |
|---------------|---------|---------|
| TP            | %0.30   | %0.25   |
| SL            | %0.15   | %0.15   |
| MaxHold       | 8dk     | 6dk     |
| VwapTolerance | 0.005   | 0.004   |

BE_WR: (0.15+0.15)/(0.25+0.15+0.30) = 0.30/0.70 = %42.9
Loop 27 WR %30 < BE_WR %42.9. Negatif beklenti riski devam ediyor.
ZORUNLU: SymbolCircuitBreaker: 5 ust uste kayip -> 2 saat pause.

### XRP - Yeniden Aktif (genis parametre)

XRP stepSize 0.10, fiyat ~2.20 USD. 10 USD sizing = 4.5 XRP. Lot-size uyumlu.

| Parametre     | Loop 28 | Loop 30 |
|---------------|---------|---------|
| TP            | %0.30   | %0.40   |
| SL            | %0.15   | %0.20   |
| MaxHold       | 8dk     | 7dk     |
| VwapTolerance | 0.005   | 0.008   |

BE_WR: (0.20+0.15)/(0.40+0.20+0.30) = 0.35/0.90 = %38.9
Loop 28 WR %27 < BE_WR %38.9. Riskli ama genis tol+TP kombinasyonu test edilmemisti.
ZORUNLU: SymbolCircuitBreaker: 5 ust uste kayip -> 2 saat pause.
---

## Bolum 4 - Islem Sayisi Artirimi

Mevcut: ~9 trade/saat (2 coin). Hedef: 20-30 trade/saat (4 coin).

### Lever 1 - VolumeMultiplier

VolumeMultiplier: candle_volume > multiplier x SMA_volume. Dusurünce daha fazla sinyal.

| Deger        | Emit artisi | Risk                         |
|--------------|-------------|------------------------------|
| 0.5 (mevcut) | baseline    | standart                     |
| 0.4          | +%20        | az dusük-volume candle girer |
| 0.3          | +%40        | sahte kirilim riski artar    |

Oneri: 0.4

### Lever 2 - MaxOpenPositions

| Deger       | Max acik USD | Exposure (1000 USD) | Risk  |
|-------------|-------------|---------------------|-------|
| 4 (mevcut)  | 40          | %4.0                | dusuk |
| 6 (oneri)   | 60          | %6.0                | orta  |
| 8           | 80          | %8.0                | orta  |

Oneri: 6. Binance 100 order/10sn limiti hic zorlanmaz.

### Beklenen Trade Sayisi

4 coin + VolumeMultiplier 0.4 + MaxOpenPositions 6:
ETH ~6/saat, BNB ~5/saat, BTC ~5/saat, XRP ~5/saat = ~21 trade/saat
---

## Bolum 5 - Fee ve Net Kar Projeksiyonu

### Fee Hesabi

21 trade/saat x 10 USD avg notional x 0.0015 (round-trip %0.150) = 0.315 USD/saat

### Loop 29 Bazli Projeksiyon

Loop 29 gercek: +0.011 USD/saat (100 USD equity, 2 coin)
1000 USD equity (lineer olcek): +0.11 USD/saat
4 coin ekle, 2.3x trade artisi (iyimser): ~0.25 USD/saat gross

### Kritik Uyari

0.315 USD/saat fee vs ~0.25 USD/saat gross -> NET NEGATIF RISKI VAR.

Break-even hesabi: 0.315 / 21 = 0.015 USD/trade avg gerekir.
10 USD sizing icin bu %0.15 avg PnL demek.
ETH WR %50 analizi: 0.5*0.030 - 0.5*0.015 - 0.015 fee RT = 0.0075 - 0.015 = -0.0075/trade

Celisme: Loop 29 net pozitif geldi. Acik sorular (backend-dev dogrulamali):
1. Paper trade fee nasil hesaplaniyor? VirtualBalance degisiyor mu?
2. BNB fee discount paper simulation aktif mi?
3. Net PnL formulu: gross_PnL - (entry+exit notional) x 0.075% mi?

Bu sorular cevaplanmadan kesin projeksiyon yapilamaz.
ETH icin pozitif beklenti kanitlanmis (loop 29 gercek veri). BTC/XRP ilk 2 saat izle.

---

## Bolum 6 - Risk Yonetimi Guncellemeleri

| Parametre            | Mevcut  | Loop 30             |
|----------------------|---------|---------------------|
| MaxOpenPositions     | 4       | 6                   |
| ConsecutiveLoss CB   | 10      | 8                   |
| MaxDrawdown24h       | %5      | %5 (50 USD paper)   |
| SymbolCircuitBreaker | yok     | 5 kayip -> 2h pause |
| DrawdownCooldown     | 1 saat  | 2 saat              |

---

## Ozet - Loop 30 Kesin Parametre Tablosu

Starting Balance: 1000 USD (paper). EquityFraction: %1.0 -> 10 USD/trade.

| Sembol  | TP    | SL    | MaxHold | VwapTol | Durum            |
|---------|-------|-------|---------|---------|------------------|
| ETH     | %0.30 | %0.15 | 8dk     | 0.005   | Aktif, degismez  |
| BNB     | %0.20 | %0.12 | 5dk     | 0.005   | Aktif, reform    |
| BTC     | %0.25 | %0.15 | 6dk     | 0.004   | Yeniden aktif    |
| XRP     | %0.40 | %0.20 | 7dk     | 0.008   | Yeniden aktif    |

| Global Parametre    | Loop 30 Degeri          |
|---------------------|-------------------------|
| VolumeMultiplier    | 0.4                     |
| MaxOpenPositions    | 6                       |
| ConsecutiveLoss CB  | 8                       |
| MaxDrawdown24h      | %5 (50 USD paper)       |
| SymbolCB (BTC/XRP)  | 5 kayip -> 2 saat pause |

---

Kaynak:
- https://developers.binance.com/docs/binance-spot-api-docs/filters
- https://www.binance.com/en/fee/schedule
- https://developers.binance.com/docs/binance-spot-api-docs/rest-api/limits
- Canli: GET https://api.binance.com/api/v3/exchangeInfo (2026-04-19)