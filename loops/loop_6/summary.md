# Loop 6 Özeti (ERKEN KAPATILDI t150)

## Süre
- Başlangıç: 2026-04-18 09:06 UTC → Bitiş: ~13:15 UTC (~250 dk)
- **Erken kapatma — kullanıcı talimatı:** "loop bozuk, hemen düzelt"

## Sonuç
- Paper $100 → **$56.10** (-%43.9 net)
- t30: $195.25 (peak unrealized) → t90: $56.10 → t150: $56.10 (donmuş)
- Sinyal: 19 (t30+17, t90+2, sonra Paused)
- 1 closed (BNB Long realized -$0.05) + 1 open (BTC Long unrealized -$0.42)
- Errors: 0

## ADR-0011/0012 Doğrulamaları (✓)
- 24h ticker REST (BNB+%2.48)
- Sized qty (0.0002 BTC, 0.04 BNB — minNotional uyumlu)
- Stop-loss tetikleme (1 trade'de stop-2-... close order)
- Risk tracking güncellenir (peakEquity başlangıçta sane)

## Yeni 2 Bug Keşfi
**#17 — peakEquity intraday tracking yok:** equity unrealized ile $195'e çıkmış ama peakEquity $99.89'da donmuş (sadece close anında snapshot). Sonradan equity dump edince yapay drawdown %43.84 hesaplandı → CB Tripped.

**#18 — Stop-loss strateji-status filtresi şüphesi:** Loop 6 BTC pos markPrice 75876 < entry 76618, ama stop tetiklenmedi. Backend-dev kod incelemesi: filtreleme YOK (hipotez yanlış). Asıl sebep AtrStop 2.0 ile stop seviyesi henüz aşılmamış olabilir. Yine de regression test eklendi (PausedStrategy_PositionStillTriggersStopLoss).

## Loop 7 Plan (uygulanan fix)
- **EquityPeakTrackerService** (yeni BackgroundService 30s tick) — intraday peak tracking
- **RiskProfile.RecordPeakEquitySnapshot** — yeni domain method, sadece peak ratchet
- **StopLossMonitorService** XML doc lock-in (strateji-status agnostic)
- **Strateji params revize:**
  - BTC-Trend: AtrStopMultiplier 2.0 → 2.5 (daha geniş stop, daha az fake trigger)
  - BNB-MeanRev: 35/65+BB1.5 → 30/70+BB2.0 (Loop 5 sapması geri alındı, kar odaklı)
- Test 100→109 (+9 yeni)

## Karar: MUTATE → Loop 7

## Commit
- Hash: (bu commit + push, PR-based workflow disiplini)
