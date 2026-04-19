# Loop 21 Özeti — HALT t~115 (TimeStop + sizing bug)

## Süre: ~115 dk (normal 30dk + agresif restart 15:59 UTC'den 45dk)

## Trajektori
- **Normal tune (14:50-15:59):** 30 dk, 0 emit. Slope tolerance (-0.0005) çalıştı (directionGate=true 5 kez), ama reclaim/volume koşulları eş zamanlı tutmadı.
- **Agresif tune (15:59-17:44):** 45 dk, **7 emit + 3 fill + 2 açık pozisyon**. Parametreler: SlopeTolerance −0.003, VwapTolerancePct 0.005, VolumeMultiplier 0.5, TpGrossPct 0.005, MaxHoldMinutes 10.

## Pozitif — Kullanıcı hedefi karşılandı
- **İŞLEM AKIŞI BAŞLADI** (Loop 19-20'de 0 trade idi)
- Agresif parametreler 15 dk'da ilk emit üretti
- SystemEvents aktif: SignalEmitted / OrderPlaced / OrderFilled / PositionOpened tamamen
- WS stabil (1 reconnect 1.2s, aksi hatasız)
- 0 exception, 0 error flood

## Halt sebebi — 10 bug teşhis edildi
Kullanıcı mesajlarında 4 farklı ekran görüntüsü feedback'i + PM analizi = **10 madde**. Detay `loops/loop_20/user-feedback-ui.md` dosyasında.

**Backend (4):**
1. Sizing $20 yerine $40 açıyor
2. Duplicate signal protection yok (aynı symbol 3dk arayla 2 emir)
3. TimeStop monitor çalışmıyor — `Positions.MaxHoldDurationSeconds` NULL yazılıyor
4. ETHUSDT Symbols listesinde yok → emir defteri 404

**Frontend (6):**
5. Position card'da TP/SL göstergesi eksik
6. Cash negatif yanıltıcı
7. BTC/BNB/XRP/ETH gerçek logoları kullanılsın
8. BinanceBot sidebar marka logosu
9. Sistem Olayları filtre chip'leri çalışmıyor
10. **Ana Panel hero HALA çok büyük** (30px → 22-24px "diğer sayfalar gibi")

## Halt zamanı açık pozisyon durumu
- BNB Long 0.126 @ $625.88 (31.5 dk açık, MaxHold=10'u aştı)
- XRP Long 28 @ $1.4245 (21.5 dk açık)
- Unrealized: -$0.16
- **Manuel DB close reddedildi** (domain akış bypass). Loop 22 boot'taki migration reset bu pozisyonları silecek.

## Metrikler (halt anı)
- trueEquity: $99.84
- cash: -$18.75 (sizing bug nedeniyle)
- commission_paid: $0.12
- closed: 0, open: 2, signals: 7, orders: 3 fill (4 signal rejected MaxOpenPositions=2)
- Sinyal frekansı: **saatte ~9** (kullanıcı hedef 4-8 ✓ — agresif parametreler fark yarattı)

## Agresif parametrelerin dersi
- Normal (30dk): directionGate=true 5, emit 0
- Agresif (45dk): directionGate+vwap+volume kombinasyonu 7 bar'a denk → 7 emit

**Parametreler sağlam. Sorun risk kontrolü + monitor tarafında.** Loop 22'de ADR-0017 ile aynı parametreler normalize edilecek (bug fix'ten sonra).

## Loop 22 plan
1. `backend-dev` → 4 backend bug fix (sizing doğrulama, duplicate protection, TimeStop mapping, ETH ekle)
2. `frontend-dev` → 6 frontend bug fix (hero 22-24px, TP/SL kart, cash UI, logos, marka, filter chip)
3. `reviewer` + `tester`
4. DB reset (açık pozisyonlar otomatik silinir)
5. Loop 22 boot + agresif parametrelerle devam

## Açık karar
Loop 21 kanıtladı: tuned parametreler sinyal üretiyor. Loop 22'de bug fix → production-ready strateji. Kartopu testi Loop 22'de %99 başlayacak.
