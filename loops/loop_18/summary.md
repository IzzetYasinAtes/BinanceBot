# Loop 18 Özeti (HALT t170 — kullanıcı UI reform talebi)

## Süre: ~170dk (erken kapatıldı)
- Trajektori: t30 +$0.193 → t90 -$0.544 → t150 -$0.278 → t170 net +$0.16
- 26 closed + 1 open (XRP Short +$0.27 unrealized)
- peakEquity $100.45 ✓ (PnL-based mükemmel)
- CB Healthy ✓, DD %0.56 ✓
- 65 toplam order

## Kullanıcı Erken Halt Talebi
Mevcut UI değerleri yanıltıcı: "$316.59 Mevcut Bakiye" cash gösteriyor (gerçek equity $100.10). 123 XRP sized aşırı (sized service MTM cash kullanıyordu).

## Loop 19 Reform (uygulandı)
**Backend:**
- `/api/portfolio/summary` yeni endpoint — TrueEquity, NetPnl, Commission, WinRate cumul
- `IEquitySnapshotProvider.GetSizingEquityAsync` (yeni method, realized-only)
- `StrategySignalToOrderHandler` sizing GetSizingEquityAsync kullanıyor (MaxPos $40, eskiden $126)

**Frontend:**
- `index.html` HERO KPI Toplam Net K/Z 40px renkli, "Mevcut Bakiye $316" KALDIRILDI
- `positions.html` trade pair card view (✓/✗ kazanan/kayıp, açıldı/kapandı/komisyon/net)
- `orders.html` Notional + Komisyon sütunu, "Açık Pozisyonlar" çıkarıldı (positions.html'de)
- `risk.html` tüm etiketler TR ("İşlem Başına Risk %", "Acil Stop Durumu", "Anlık Kayıp Yüzdesi")
- Typography: base 14→16px, h2 18→22px, KPI 22→28-32px, system-ui sans-serif + tabular nums
- `format.js` yeni `moneySigned`, `pctSigned`, `duration`, `timeHm` helper'lar

Test 167→174 (+7 yeni Portfolio + Sizing). Build 0/0.
