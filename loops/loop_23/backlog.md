# Loop 23 Backlog — 2026-04-19 ~17:30 UTC

## Kullanıcı feedback — Ana Panel hero 3. tur
**Tarih:** 2026-04-19 17:30 UTC (Loop 22 cycle t~8)
**Screenshot:** Ana Panel ekranı, hero kartı hala Hızlı Metrikler'den daha büyük görünüyor
**Kullanıcı:** "hala ana panel çok büyük diğer sayfalar ile boyutu aynı olması gerek bunu al önümüzdeki sprint geçişine"

### Geçmiş iterasyonlar
- Loop 19: Hero 42px (frontend-dev ilk UI)
- Loop 21: Hero 42→30px (first küçültme)
- Loop 22: Hero 30→23px (ikinci küçültme + gradient shine animasyon)
- **Şimdi:** Hala "diğer sayfa ile aynı olması gerek"

### Problem
Hero kartı artık 23px rakam kullanıyor ama:
- **Gradient background** hala hero'yu öne çıkarıyor (linear-gradient 135deg #1e293b→#0f172a)
- **Kartın kendi iç padding'i** "Hızlı Metrikler" kartlarından fazla
- **Hero ile Hızlı Metrikler arasındaki boşluk** hero'yu ayrı gösteriyor
- Kullanıcı için "aynı" = aynı görsel ağırlık, aynı kart stili, aynı padding

### Çözüm (Loop 23 frontend-dev brief)
**Option A — Hero kartını tamamen kaldır, KPI grid'e taşı:**
- "Toplam Net K/Z", "Mevcut Bakiye", "Gerçek Özkaynak" → Hızlı Metrikler grid'in ilk 3 kartı
- Toplam 7 KPI kartı aynı scale, aynı bg
- "Hızlı Metrikler" başlığını "Portföy" veya tek başlık "Ana Panel Metrikleri" yap
- `.hero-kpi` CSS bloğunu sil, `.card.kpi` şablonu kullan

**Option B — Hero'yu "Hızlı Metrikler" ile aynı kart stili yap:**
- `.hero-kpi` → `.card` + gradient bg kaldır
- Padding sp-5 → sp-4 (Hızlı Metrikler ile aynı)
- Rakam 23px → 20-22px (Hızlı Metrikler rakamları neyse ona göre)
- Hero ayrı görünmesin, normal kart olsun

**Tercih A** daha temiz — kullanıcı 3 kez istek yapmış demek ki "ayrı kart" kavramı hiç işine yaramıyor.

---

## Loop 22 review minor/nit'leri (Loop 23 backlog)
Reviewer 0 blocker verdi ama 4 minor + 3 nit kayıtlı:

### Minor
1. `OrderFilledPositionHandler.cs:93-123` — ContextJson parse mantığı `ParseMaxHoldDuration` private helper'a çıkar (SRP)
2. `symbolLogo.js:29` — `base.value` alfanumerik sanitization (defensive XSS)
3. `docs/adr/0017-timestop-...md:321-325` — ADR §17.11.2 örneği `mode.ToString()` → `BarOpenTime` düzelt
4. `appsettings.json:36,67-86` — ETHUSDT sembol var ama strateji yok, kasıt belirt (comment veya seed ekle)

### Nit
- `symbolLogo.js:7` — KNOWN set comment ile sync sorumluluğu belirt
- `OrderFilledPositionHandlerTests.cs:169-173` — Reflection yerine `StrategySignal.Emit(StrategyId, ...)` parametre ekleme
- `appsettings.json:32` — `dev-admin-key-change-me` production override doc ekle

---

## Loop 23 öncelik sırası
1. **Ana Panel hero düzleştir (Option A)** — en üst öncelik, kullanıcı 3. tur
2. ETHUSDT strateji seed veya kasıt comment (minor #4)
3. ADR-0017 typo fix (minor #3)
4. OrderFilledPositionHandler SRP refactor (minor #1)
5. symbolLogo path sanitization (minor #2)
6. Admin key docs (nit)

## Not
Loop 22 cycle hala devam ediyor (API açık, 3 strateji aktif, sinyal bekleniyor). Backlog Loop 22 sonu + yeni boot'ta yapılacak.
