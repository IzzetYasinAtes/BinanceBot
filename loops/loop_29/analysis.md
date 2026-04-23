# Loop 29 Data Analysis (DB reset öncesi)

## Özet — 40 trade ≈ 4.5 saat

Net: +$0.05 (paper $100 equity)

## Sembol detay

| Sembol | Trade | W/L | WR | Toplam | Avg | Best | Worst | Hold ort |
|---|---|---|---|---|---|---|---|---|
| ETH | 22 | 11/11 | %50 | **+$0.051** | +$0.0023 | +$0.022 | -$0.011 | 394sn |
| BNB | 18 | 8/10 | %44 | **-$0.003** | -$0.0001 | +$0.009 | -$0.011 | 474sn |

**Bulgu:** ETH tüm net karı taşıyor. BNB toplamda ZARAR (Loop 29 ilk aşamalarda pozitif görünmüş, zamanla marjinal negatife dönmüş).

## Hold süresi dağılımı (40 trade)

| Bucket | Trade | Avg PnL | Yorum |
|---|---|---|---|
| 60-180sn | 3 | -$0.0006 | Erken çıkış küçük kayıp |
| 180-360sn | 4 | -$0.0036 | Orta pencere hep zarar |
| **360-480sn** | **4** | **+$0.0112** | **En karlı bucket (TP tetikleyenler)** |
| 480+sn (MaxHold) | **29** (%72) | +$0.0007 | Çoğu — marjinal |

**Kritik:** %72 trade MaxHold 480sn'ye dolaşıyor, küçük kar/zarar ile kapanıyor. TP (%0.30 gross) asıl kar kaynağı ama sadece **%10 trade** tetikliyor.

## Avg notional: $5.39

%1 × $100 equity + lot-size buffer = beklenen $5.10 → gerçek $5.39 (buffer üstü minik sapma).

## Loop 30 için çıkarımlar

1. **Sizing ölçek:** $5.39 × 30 trade/saat × %0.0023 ETH avg = $0.37/saat potansiyel — çok küçük. **Starting balance 10x ($100 → $1000) önerilir**, sizing $53.9/trade → aynı WR ile $3.7/saat.

2. **Per-coin:** ETH parametreleri iyi, BNB parametreleri **farklı** gerek (WR düşük, hold daha uzun 474s vs ETH 394s — BNB MaxHold fazla, TP ulaşmıyor).

3. **BTC/XRP:** Devre dışı (Loop 27/28'de kanıtlanmış zarar).

4. **TP tetikleme oranını artır:** TP %0.30 çok uzak, **%0.20** + MaxHold **5dk**'ya çekerek hızlı çevrim.

5. **UI ile ilgili:** 
   - Strategies panel toggle kaldır (readonly)
   - Orders/Positions kart → tablo satır satır
   - Format 4 küsürat her yerde ($0.0023 → $0.0023 kalabilir ama fiyatlar $75,000.0000 gibi)
