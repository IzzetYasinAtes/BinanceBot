# Loop 8 Backlog (Kullanıcı Sipariş Listesi)

**Kullanıcıdan:** 2026-04-18 ~13:25 UTC (Loop 7 t10 dolayında)
**Talimat:** Loop 8 boot'ta plan + uygulama.

---

## #19 — Portföy Özeti tutarsız + İngilizce etiketler

**Gözlem (ekran):**
- Başlangıç Bakiyesi: $100.00
- Mevcut Bakiye: $70.75
- Realized Today: $-0.07
- Unrealized: $-0.02
- Fill Başarısı %: 100.0% (4 doldu / 4 toplam)

**Tutarsızlık matematik:**
- Beklenen: $100 + (-0.07) + (-0.02) = $99.91
- Gösterilen: $70.75 → fark **-$29.25** açıklanmamış

**Hipotezler:**
1. **Mevcut Bakiye = cash balance**, açık pozisyonlar mark-to-market hesaba dahil değil. Yani:
   - Cash balance = Starting + Realized - (open positions × entry price) = $100 - 0.07 - 29.18 ≈ $70.75
   - Bu durumda doğru "portföy değeri" = cash + open positions@markPrice
2. **Unrealized backend hesabı eksik:** sadece bir kısım pozisyon hesaba alınıyor, diğerleri 0 veriyor.
3. **VirtualBalance.equity fonksiyonu yanlış:** ADR-0008 §8.4 ApplyFill/ApplyUnrealized akışı eksik.

**Aday fix lokasyonu:**
- `src/Application/Balances/Queries/GetBalances/` — cevap DTO
- `src/Domain/Trading/VirtualBalance.cs` (varsa)
- `src/Frontend/index.html:46` "Mevcut Bakiye" → ne göstermeli?

**Etiket Türkçeleştirme:**
- "Realized Today" → **"Bugünkü Realize"** veya **"Bugünkü Kar/Zarar"**
- "Unrealized" → **"Gerçekleşmemiş"** veya **"Açık Pozisyon K/Z"**
- "Fill Başarısı %" → kalabilir (yarı Türkçe), ama "Doluluk Oranı %" daha temiz
- "Toplam İşlem" → kalabilir
- "açık" → kalabilir (zaten TR)

---

## #20 — Aktif Sinyal × Trade sayı uyumsuzluğu

**Gözlem (ekran):**
- Aktif Sinyaller bölümünde 5 sinyal kart (BNB MeanRev x4, BTC Trend x1, Exit x1)
- Toplam İşlem: 4 (1 açık)
- Ekran zamanı: 15:24:01

**Hipotezler:**
1. **Aynı strateji aynı yön ardışık sinyal:** BNB-MeanRev "Long" sinyalleri 15:21, 15:22, 15:24 — ardışık aynı yön. PlaceOrder idempotent (clientOrderId aynı bar için aynı), ya da pozisyon zaten açık → yeni order yaratılmıyor. Doğru davranış.
2. **Exit signal NotFound:** BNB Exit 15:16 → açık pozisyon yoksa NotFound, sessizce skip. Trade'e dönüşmüyor. Doğru davranış.
3. **Sized qty 0 → SKIP:** Min notional altı veya equity 0 → trade yaratılmıyor. SystemEvent'lerden kontrol edilmeli (trade.sizing_skipped).

**Asıl soru:** "Çok sinyal ama az trade" beklenen davranış mı, **bug mı?** Frontend'de "Aktif Sinyaller" bölümünün gerçek anlamı net değil — kullanıcı her sinyalin trade'e dönüşmesini bekleyebilir. UX clarification + log gözden geçirme.

**Aday fix:**
- Frontend `index.html` "Aktif Sinyaller" bölümüne her sinyal için **sonuç badge'i** ekle: "FILLED" / "SKIPPED (sizing)" / "EXIT (no_position)" / "DUPLICATE"
- Backend SystemEvents tablosundan signal-to-order karar nedenlerini çek
- Yeni endpoint? `/api/strategies/signals/recent-with-outcome` (sinyal + order durumu join)

---

## Kontrol Sırası (Loop 8 boot)

1. Mevcut DB üzerinden tanı (DB silmeden):
   - `/api/balances` ham veri vs UI hesaplaması karşılaştırma
   - `/api/orders/history` son 20 + `/api/strategies/signals/latest` son 20 cross-reference
2. Plan dosyası: `loop_8/plan.md`
3. Uygulama (frontend-dev + backend-dev gerekirse)
4. DB drop + Loop 8 normal cycle
