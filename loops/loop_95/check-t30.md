# Loop 95 Check t30

Tarih: 2026-05-03 19:44 UTC | Boot: 19:12 UTC | Süre: 32dk

## Sonuç: Long-only çalışıyor, AMA frekans çok düşük (2 emit/30dk = 4/h)

### Open Positions (2)
| Symbol | Direction | Entry | Mark | UPnL | Peak | BE | Hold |
|---|---|---|---|---|---|---|---|
| BTCUSDT | Long | $78792.48 | $78748.30 | -$0.057 | $78752.60 | null | 10min |
| **ADAUSDT** | **Long** | **$0.2510** | **$0.25115** | **+$0.050** ✓ | $0.25115 | null | 5min |

**İLK POZİTİF UPnL!** ADA Long +$0.05 (boot+5dk). Loop 92-94'te tüm pozisyonlar UPnL negatifti (Short bias toxic + R:R asimetri).

### Closed Positions: 0 (henüz kapanma yok)

### Signals: 28 toplam (önceki Loop 94 carryover) — son 30dk **sadece 2 yeni emit**
- Direction=1 (Long): 25
- Direction=2 (Short): 3 (Loop 94 carryover, Loop 95'te yeni Short emit YOK ✓ — WeightOverrides çalışıyor)
- Son 30dk: 2 yeni emit (BTC 19:34, ADA 19:39)

**Frekans: 4/h** (hedef 30+, çok düşük)

### VirtualBalance
- WalletBalance: $499.90 ✓
- AllocatedMargin: ~$200 (2 pos)
- Equity: $499.85

### PortfolioSummary
- currentCash: $499.90
- totalCommissionPaid: $0.10 (2 pos × $0.05)
- realizedPnl: $0
- unrealizedPnlTotal: -$0.05
- **netPnl: -$0.15** (Loop 94 t30 -$0.63, %75 azaldı)

## Analiz

**İYİ**:
- ✅ Long-only emit doğrulandı (sadece Direction=1 yeni emit, Loop 95'te Short=0)
- ✅ İlk pozitif UPnL (ADA +$0.05)
- ✅ Halt eşik aşılmadı (realized $0)
- ✅ R:R tune yansıyacak (BE/trailing henüz arm değil)

**KÖTÜ**:
- ⚠ Frekans 4/h (hedef 30+) — MTF gevşetme yetmedi
- ⚠ Sadece 2 coin açık (BTC, ADA) — XRP/ETH/SOL emit yok

**Sebep hipotezi**: RequiredScore=5 muhtemelen sıkı. Long-only modda 10 Long detector × ortalama weight 2 = max 20 puan, ama bar başına detector tetiklenme oranı düşük → RequiredScore=5 nadiren aşılıyor.

**HALT EŞİĞİ**:
- realizedPnl < -$1.50 → realized=$0 → AŞILMADI
- 0 emit > 1h → 32dk içinde 2 emit, marj var (eşik 60dk)

## Karar: Loop 95 DEVAM, t60'a wakeup

t60'ta:
- Eğer hala 4-6/h frekans → Loop 96 RequiredScore 5 → 3 düşür (DB UPDATE kolay)
- Eğer pozisyonlar kapanmaya başlayıp realized pozitif → Loop 95 başarı, devam
- Eğer realized aşağıya kayar → halt + Loop 96

t60 izleme noktaları:
1. ADA pozisyonu BE +%0.30 eşiğine varır mı (peak/entry-1 > 0.003 → SL=entry × 1.003)
2. BTC pozisyonu kazanca dönüyor mu
3. Yeni emit gelir mi (frekans ölç)

## Carryover

- 2 Long açık (BTC -$0.06, ADA +$0.05)
- 0 close
- 28 signal (Loop 94 carryover dahil)
- WeightOverrides aktif, Short emit YOK ✓
