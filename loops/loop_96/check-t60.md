# Loop 96 Check t60

Tarih: 2026-05-04 21:23 UTC | Boot: 20:19 UTC | Süre: 64dk

## Sonuç: 5 Açık (MaxOpen Dolu), 2 Pozitif UPnL (BTC +$0.24, ETH +$0.06)

### Open Positions (5 Long, MaxOpen=5 dolu)
| Symbol | Entry | Mark | UPnL | Peak | Peak/Entry-1 | BE | Hold |
|---|---|---|---|---|---|---|---|
| **BTCUSDT** | $78792 | $78977 | **+$0.239** ✓ | $79013.80 | **+0.28%** | null | 63min |
| ADAUSDT | $0.2509 | $0.2504 | -$0.230 | $0.25125 | +0.13% | null | 59min |
| XRPUSDT | $1.3949 | $1.3939 | -$0.075 | $1.39810 | +0.23% | null | 59min |
| SOLUSDT | $84.57 | $84.37 | -$0.247 | $84.525 | -0.06% | null | 44min |
| **ETHUSDT** | $2330.16 | $2331.46 | **+$0.056** ✓ | $2333.61 | +0.15% | null | 14min |

**BTC kar büyüyor**: t30 +$0.10 → t60 +$0.24 (+$0.14 in 30dk). Peak %0.28 (BE eşik %0.30 → ÇOK YAKIN).

### Closed (1, aynı)
- ETHUSDT (önceki): -$0.62 SL hit (Loop 96 boot+30dk)

### Frekans
- Loop 96 emit: 10 toplam / 64dk = **10/h** (sabit, MTF fix sonrası tutarlı)
- t30: 5 emit → t60: 10 emit (+5 son 30dk)
- 5 coin'den emit ✓ (ETH yeni eklendi t30 sonrası)

### VirtualBalance
- WalletBalance: $499.13 ($500 - $0.35 commission - $0.62 realized + $0.10 mantıken yansır)
- AllocatedMargin: ~$500 (5 pos × ~$100 = $503)
- Equity: $498.85

### PortfolioSummary
- realizedPnl: -$0.62 (sabit)
- unrealizedPnl: -$0.27 (t30 -$0.55 → t60 -$0.27, **%50 iyileşme**)
- totalCommissionPaid: $0.35
- **netPnl: -$1.15** (t30 -$1.37, $0.22 iyileşme)

## Analiz

**İYİ**:
- ✅ MaxOpen=5 dolu (5 coin sirkülasyon ✓)
- ✅ 2/5 pozisyon pozitif UPnL (Loop 95'te 0/2 idi)
- ✅ BTC kar büyüyor (BE eşiğine çok yakın)
- ✅ Frekans tutarlı (10/h)
- ✅ Long-only emit + WeightOverrides çalışıyor

**KÖTÜ**:
- ⚠ Frekans 10/h hala hedeften düşük (30+)
- ⚠ ADA + SOL persist negative UPnL
- ⚠ 0 yeni close (60dk hold pos'lar SL/TP'ye varmıyor — pazar yatay)

**HALT EŞİĞİ**:
- realizedPnl -$0.62 → eşik -$1.50, marj $0.88 ✓
- 0 emit > 1h → 5 emit son 30dk ✓
- netPnl -$1.15 (eşik tanımı realized'a göre)

## Karar: Loop 96 DEVAM, t90 wakeup

t90 senaryolar:
1. **İdeal**: BTC BE eşiğini geçer → BE arm → trailing %0.3 → +$0.20+ profit lock. ETH benzeri.
2. **Orta**: BTC profit korur ama BE'ye varmaz, diğer 3 pos hold. realized sabit -$0.62.
3. **Kötü**: ADA+SOL SL hit → realized -$1.50+ → halt + Loop 97.

Pozitif sinyal: 60dk içinde BTC sürekli yukarı (+$0.10 → +$0.24). Eğer trend devam ederse BE+trailing R:R fix'inin asıl testi olur.

## Carryover

- 5 Long açık (BTC+ETH +$0.30 kar, ADA+XRP+SOL -$0.55 zarar)
- 1 closed (-$0.62)
- Realized -$0.62, UPnL -$0.27 → toplam -$0.89
- 5 coin sirkülasyon
