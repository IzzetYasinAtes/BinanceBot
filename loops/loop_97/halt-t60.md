# Loop 97 Halt — t60 Realized -$1.998 (Eşik AŞILDI)

Tarih: 2026-05-04 23:35 UTC | Clean boot: 22:32 UTC | Süre: 63dk

## Halt: Realized -$1.998 < Halt Eşiği -$1.50 (✗)

### t30 → t60 Trajectory (Hızlı Çöküş)
| Metrik | t30 | t60 | Δ |
|---|---|---|---|
| Realized | **+$0.048** ✓ | **-$1.998** ✗ | -$2.05 |
| Win Rate | %100 (1/1) | %25 (1/4) | -75% |
| Açık | 3 | 1 (yeni) | - |
| Closed | 1 (winner) | 4 (1W/3L) | +3 |

### KÖK SEBEP: 3 Ardışık SL Hit 1 Dakika İçinde

Closed timeline:
| # | Saat | Symbol | Direction | Entry | Exit | RPnL |
|---|---|---|---|---|---|---|
| 1 | 22:48:40 | SOLUSDT | Long | $84.60 | $84.73 | **+$0.048** |
| 2 | **23:22:58** | XRPUSDT | Long | $1.3990 | $1.3914 | **-$0.643** |
| 3 | **23:23:18** | BTCUSDT | Long | $79172 | $78754 | **-$0.647** |
| 4 | **23:23:43** | ADAUSDT | Long | $0.2515 | $0.2499 | **-$0.757** |

**23:22-23:23 (45 saniye)** içinde XRP+BTC+ADA üçü de SL hit. Pazar flash crash benzeri Long-only catastrophic loss.

Net realized: +$0.048 - $0.643 - $0.647 - $0.757 = **-$1.999**

### Pattern Tekrarı (Loop 94 → Loop 97 simetri)
- Loop 94: Long+Short, 2 büyük Short SL hit (uptrend) = -$1.247
- Loop 97: Long-only, 3 büyük Long SL hit (downtrend) = -$2.047

**Sonuç**: Pazar BİR YÖNDE keskin hareket ederken her iki mod (Long-only, Long+Short) korumasız. Pozisyon başı RİSK çok büyük → Loop 98 fix gerek.

## Loop 97 Pozitif Gözlemler (Yine de var)

- ✅ İLK pozitif realized trade (SOL +$0.048) — 17 loop sonrası
- ✅ BE-stop matematik mekanik çalışıyor (TriggerPct=0.002 doğru)
- ✅ Frekans 14/h → 9/h (sirkülasyon devam, hedef 30+ değil)
- ✅ Wallet/Margin/Peak doğru
- ✅ MTF threshold doğru yön
- ✅ PaperTrade reset bug workaround çalıştı

## Loop 98 Hipotezi: Pos Başı Risk Yarıya

**Tune** (PM doğrudan, kod yok):
1. **RiskPerTradePct 0.02 → 0.01** (DB UPDATE RiskProfiles)
   - Pos qty yarıya → SL hit loss yarıya (-$0.65 → -$0.32 ortalama)
   - 3 ardışık SL hit toplam -$2 → -$1 (eşik -$1.50 AŞILMAZ)
   - Win amount da yarıya (+$0.05 → +$0.025) AMA absolute risk azalır
2. **WeightOverrides Revoke** (Short emit geri)
   - DB UPDATE: 5 Strategies ParametersJson WeightOverrides=NULL
   - Composer hard-coded weight kullanır (Long+Short dengeli)
   - Pazar yönü her iki tarafa hedge
3. **MaxOpenPositions 5 → 3** (DB UPDATE RiskProfiles)
   - Risk concentration düşer
   - 3 ardışık SL → -$1 (3 pos × $0.32) yerine 5 pos × $0.32 = -$1.60 → -$1 daha güvenli

Loop 98 spec yazılacak. PM doğrudan tune (kod gerek yok).

## Cumulative 18 Loop

| Loop | Realized |
|---|---|
| 80-91 | -$17.04 |
| 92 | -$117 (bug) → gerçek -$0.65 |
| 93 | $0 (frekans donması) |
| 94 | -$1.16 |
| 95 | $0 (frekans bug) |
| 96 | -$1.30 |
| 97 | **-$1.998** ← halt eşiği AŞILDI ilk kez |
| **Total** | **-$21.5** / 18 loop / **0 winning loop** |

## Carryover

Bot kapatıldı. Loop 98 boot bağımsız (PaperTrade reset zaten bug, manuel state cleanup gerekli).
