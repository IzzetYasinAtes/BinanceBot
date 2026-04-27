# Loop 43 — Check t=30dk (2026-04-24 18:00 TR)

## Durum: STAGNATION DEVAM (filtre gevşetmesi henüz tetiklemedi)

| Metrik | t0 | t30 | Δ |
|---|---|---|---|
| Cash | $500.0000 | $500.0000 | 0 |
| Equity | $500.0000 | $500.0000 | 0 |
| Realized | $0.0000 | $0.0000 | 0 |
| Pos Open / Closed | 0 / 0 | 0 / 0 | 0 |
| Signals / Fills | 0 / 0 | 0 / 0 | 0 |
| EvtSkip (35dk) | — | 250 | normal |
| EvtErr | 0 | 0 | 0 |

**30dk boyunca 0 sinyal.** Filtre MinAtrPct 0.0010 → 0.0007'ye düşürüldü ama hala sinyal yok.

## Sebep Analizi (yeni hipotez)
**Piyasa aşağı yönlü** (BTC -%0.01, ETH -%0.05, BNB -%0.15, XRP -%0.19, SOL -%0.31, DOGE -%0.57 son 60dk):
- Donchian üst kırılım = fiyat son 20-bar high'ı geçmeli
- Fiyat **aşağı yönlü** olduğu için son 20-bar high uzakta kalıyor → üst kırılım sistematik olarak imkansız
- Volume Z hala 1.5 + ATR yeterli olsa bile koşul 1 sağlanmıyor

**Strateji long-only — Donchian alt kırılım (short) tasarımda yok.** AR-GE şampiyon stratejisinin doğal kısıtı: bull/range market'te çalışır, downward market'te ölü.

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | 0 | ✓ buffer dolu |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 0 açık | ✓ |
| WS / CB | Streaming, HEALTHY | ✓ |

**HALT YOK.**

## Loop 44 Candidate Plan (TETİKLENİRSE — t150/t240'a kadar hala 0 trade ise)

### Seçenek A: Short tarafı ekle (KOD DEĞİŞİKLİĞİ — backend-dev gerekli)
- DonchianBreakoutEvaluator'a Donchian ALT kırılım (short) sinyal mantığı ekle
- Position.Side=Short paper simülasyonda destekleniyor mu kontrol gerek
- Spot piyasada short = sentetik (perpetual değil — ya da SHORT_SELL eşdeğeri)

### Seçenek B: Filtreyi daha agresif gevşet (config-only)
- MinAtrPct 0.0007 → **0.0005**
- VolumeZScoreThreshold 1.5 → **1.3**
- Cooldown 6 → 4 bar (60dk)

### Seçenek C: Strateji ailesi değiştir (RADİKAL — kullanıcı onayı gerek)
- Mean-reversion eklemesi (bear market için)
- Bollinger Band squeeze break
- Bu pivot — auto mode dışı, kullanıcıya sorulmalı

**Şimdiki plan:** Seçenek B (config-only, hızlı, geri alınabilir). Loop 44 boot tetiklendiğinde bu uygulanır.

## Playwright Smoke (1 sayfa)
- ui-t30-01-dashboard.png — Hero 3×$0, $500 sabit, piyasa hero hala kırmızıda
- Console error 0

## Sıradaki Wakeup
**ScheduleWakeup 3600 → t=90dk (19:00 TR)**

Eşik:
- t90'da 1+ trade gelirse → loop devam, sonraki t150
- t90'da hala 0 trade → t150'ye kadar bekle
- t150'de hala 0 trade → **Loop 44 başlat (Seçenek B: MinAtrPct 0.0005, VolumeZ 1.3, Cooldown 4 bar)**

— PM 2026-04-24 Loop 43 t=30
