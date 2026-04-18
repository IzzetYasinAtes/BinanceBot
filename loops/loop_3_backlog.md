# Loop 3 Backlog (Kullanıcı Sipariş Listesi)

**Kullanıcıdan:** 2026-04-18 ~00:30 UTC
**Talimat:** Loop 3 başlangıcında DB **silmeden önce** kontrol et, her maddeyi ayrı planla, sonra Loop 3'e geçerek uygula.

---

## 1. UTC kaydet, UTC+3 (İstanbul) göster
- DB tarafı UTC sabit kalsın (zaten `DateTimeOffset` UTC)
- Frontend formatlama: tüm tarih/saat görüntüleri Europe/Istanbul'a çevrilsin
- Aday yer: src/Frontend/js/api.js veya ortak formatter helper

## 2. Header'da kripto son 1dk %lik hareket
- Anasayfa header'ında BTC/BNB/XRP için son 1m kline %change
- Backend: muhtemelen mevcut `/api/klines` veya yeni endpoint
- Frontend: ticker bar component

## 3. Adet hesaplama yanlış görünüyor
**Örnekler:**
- BNB 0.0290 × $640 = ~$18.5 → %1 risk = $1, sapma var
- BTC 0.0210 × $77,343 = ~$1,624 → 100$ portföyde imkansız boyut

Aday: PositionSizer / RiskCalculator hatalı, OrderSize parametresi config'ten direkt okunuyor olabilir (appsettings.json `Strategies.Seed[].ParametersJson.OrderSize`)

## 4. Çok işlem var ama sadece 2 açık + uzun süre kalmış
- Toplam 414+ fill, sadece 2 OPEN
- Exit logic eksik veya pozisyonlar TP/SL hit etmiyor
- TrendFollowing AtrStopMultiplier=2.0 belki çok geniş

## 5. Portföy özeti doğruluk kontrolü
**Kullanıcının gördüğü:**
- Başlangıç $100 / Mevcut $180.14
- Realized Today $0.00 (şüpheli)
- Unrealized -$0.94
- Kazanma %33.3 (5 doldu / 15 toplam — toplam fill 414, çelişki)
- Toplam İşlem 414, Açık 2

Aday: GetPortfolioSummaryQuery hesabı yanlış, "5/15" ne demek belirsiz

## 6. İşlemler sayfası: üst boş, alt dolu
- Açık İşlemler tablosu: "Kayıt yok" — ama portfoy özetinde 2 açık var
- İşlem Geçmişi: 417 toplam dolu

Aday: Açık order endpoint filter problemi (Status=New|PartiallyFilled vs OPEN position farkı)

## 7. Risk tablosu düzgün çalışmıyor
Detay yok ama "bişey yok" deniyor — boş response veya UI render hatası

## 8. Risk %1 / Max %10 → 100$ ile çok küçük
**Talep:** Komisyon, alım tutarları gerçek hayatta nasılsa öyle olsun
- Risk per trade %1 → $1 → çok küçük order, pratik değil
- Önerim: Profil revize — RiskPerTradePct 5%, MaxPositionPct 25%, OrderSize tabanları (BTC 0.001, BNB 0.05, XRP 50) gerçek minNotional'a uyacak

## 9. GitHub workflow disiplini

Loop sonu commit'leri PR-based workflow'a geçecek, gerçek bug observation'ları için GitHub Issues kullanılacak. Detay PM operasyonel notunda.

---

## 11. /api/orders/history ve /api/strategies/signals/latest 500 hatası
Loop 2 sonu snapshot çağrısında her iki endpoint `System.InvalidOperationException: Sequence contains no matching element` döndürdü (ValidationBehavior'da boş Single() çağrısı). UI'da liste boş gözükmesi muhtemelen bu yüzden değil ama validation pipeline'ı bozuk.

Aday: `ValidationBehavior<TRequest, TResponse>` — boş validator listesinde `.Single()` yerine `.SingleOrDefault()` veya `.FirstOrDefault()`.

## 10. Backfill retro-sinyal patlaması (Loop 2 gözlem)
Loop 2 boot sonrası 21:00:02'de tek anda 11 sinyal emit edildi (BNB-MeanRev 9, Trend 2). Bu backfill worker'ın geçmiş bar'ları da evaluator'a göndermesinden. Sorun:
- Geçmiş zamandaki sinyaller "şimdi" fiyatıyla Paper fill oldu → distorsiyon
- Risk/consec-loss sayacı geçmiş kararla şişer
- Gerçek canlı performans karışır

Çözüm adayları: (a) evaluator sadece son kapanan bar'ı değerlendirsin, backfill'den sonra "warmup done" flag'ini bekle, (b) backfill period'u "silent seed" modunda sadece indicator state'i hesapla, sinyal yayma, (c) canlı WS ilk yeni bar'dan itibaren evaluate et.

Aday fix: `KlineBackfillWorker` sonunda bir `BackfillCompletedEvent` yayınla, StrategyEvaluationHandler önce `IsSeeded` kontrol etsin.

## Kontrol Sırası (Loop 3 boot — DB SİLMEDEN ÖNCE)

1. Mevcut DB'yi koru, snapshot al (Loop 2 sonu state'i):
   - Snapshot: balances, strategies, orders count, positions, risk profile, signals
2. Her madde için ayrı tanı:
   - GET endpoint'leri çağır, beklenen vs gerçek karşılaştır
   - DB sorguları (PowerShell SqlClient)
   - Frontend HTML/JS oku
3. Plan dosyası: `loop_3/plan.md` — her madde için (a) bulgu, (b) çözüm, (c) etki, (d) agent
4. **Sonra** DB silinir, kod uygulanır, Loop 3 normal akışına geçilir
