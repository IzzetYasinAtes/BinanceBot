# Loop 32 — Sessizlik Teşhisi

**Tarih:** 2026-04-22 19:04 UTC
**Tespit:** Son kapanan trade 2026-04-21 17:05 UTC — 25 saattir hiç trade kapanmamış. 3 açık pozisyon 26 saattir açık.

## 1. Gözlem

### Sinyal akışı normal
- Son 24 saatte 28 sinyal üretilmiş (son: 2026-04-22 19:03 UTC — 1 dk önce)
- 4 coin × dakikada 1 sinyal dönüyor
- Kline'lar taze (son bar 19:03 UTC)
- WsStateChanged 24h=4 — WS bağlantıları sağlıklı
- StrategyActivated 24h=4 — API ~10 dk önce restart edilmiş

### Ama trade açılmıyor
- `SignalSkipped` 24h=**33**
- `SignalEmitted` 24h=**28**
- Sinyallerin yarıdan fazlası **skip** — yeni pozisyon açılmıyor
- 4 strateji Status=3 (Active, brief'teki "BTC Paused runtime Status=2" artık geçerli değil — restart sonrası hepsi Active)

### 3 Zombie Pozisyon
```
Id=326 ETHUSDT OpenedAt=2026-04-21 17:00:01  AgeMinutes=1563 (26.05 saat)
Id=327 XRPUSDT OpenedAt=2026-04-21 17:00:01  AgeMinutes=1563
Id=328 BTCUSDT OpenedAt=2026-04-21 17:02:00  AgeMinutes=1561
```

MaxHold parametreleri: ETH 8dk, BNB 5dk, BTC 6dk, XRP 7dk. **Hepsi MaxHold'un ~200 katı süredir açık.**

## 2. Root Cause Hipotezleri

### H1 — TimeStop monitor restart sonrası eski pozisyonları ignore (olası)
- API ~18:53 UTC'de restart edilmiş
- `StopLossMonitorService` + `TakeProfitMonitorService` muhtemelen **event-based** (yeni kline geldiğinde sadece yeni pozisyonlar için check) veya **startup scan eksik** — restart sonrası "Now - OpenedAt > MaxHold" koşulunu eski pozisyonlar için yeniden evaluate etmiyor.
- Veya monitor tarama süresi yeterince kısa ama pozisyon bir predicate filtresi tarafından atlanıyor (OpenedAt eski diye "stale" kabul ediliyor olabilir).

### H2 — SignalSkipped sebep: MaxOpenPositions=3 (olası, paralel)
- RiskProfile.MaxOpenPositions = 3 olmalı (per-coin veya total). Zaten 3 açık = yeni sinyal skip edilir.
- Her dakika 4 sinyal üretiliyor: 3 mevcut open olan coin için skip (aynı symbol zaten açık), 1 serbest coin için de skip... olabilir başka filter.

### H3 — API restart öncesi strateji fail etti, positions orphaned
- 21 Nisan 17:05 sonrası hiç kapama yok, tam o anda process crash olmuş olmalı.
- 21 Nisan 17:05-22 Nisan 18:53 arası ~25 saat downtime
- Loop 32 aslında **crash halinde**, ~25 saat önce etkin operation sona ermiş.

## 3. Tavsiye

**Loop 32 fiilen bitmiş kabul et.** Strateji EV analizi için kapsamlı veri yok (25 trade kırık state, 3 zombie). Fix zinciri:

1. **3 zombie pozisyonu kapat** (force-close veya DB Status=2 update + exit fiyatı son kline close)
2. **Loop 33 boot öncesi DB reset** (briefing'teki reset SQL — Orders/OrderFills/Positions/StrategySignals/Strategies/SystemEvents + VirtualBalance starting=100 + RiskProfile Peak reset)
3. **Monitor restart-resume bug'ı Loop 33 reform kapsamında ADR+fix** — monitor service startup'ında tüm open positions için MaxHold/SL/TP check tetiklemeli (eager sweep)
4. **binance-expert AR-GE sonucu** Loop 33 stratejisini belirleyecek

## 4. Loop 32 Final Verdict

- **Result:** FAIL
- **Runtime:** ~24 saat (stall sonrası 25 saat downtime)
- **Kapalı trade:** 25 — Net: **-$0.0109** (gross -$0.086 loss + $0.075 win, %48 win rate)
- **Açık zombie:** 3 (ETH/XRP/BTC, paper MTM unrealized +$0.53, kapanınca fee sonrası ~+$0.50)
- **Portföy etkisi:** trueEquity $100.42 → gerçek kar $0.42 (24 saatte)
- **Bug'lar bulundu:**
  - Asimetrik fee (simulator BUY fee ghost, SELL fee cash düşülüyor) → ADR-0020 + Fix A
  - Monitor restart-resume eski pozisyonlar için TimeStop tetiklemiyor → Loop 33 reform
- **Strateji verimi:** saatte ~$0.02 gerçek, iddialı değil — AR-GE devam

## 5. Sonraki Adım

- [x] PnL bug teşhisi yazıldı → ADR-0020 + Fix A delege (backend-dev + architect çalışıyor)
- [x] Sessizlik teşhisi yazıldı → bu dosya
- [ ] binance-expert AR-GE raporu (loops/loop_33/strategy-arge.md) — çalışıyor
- [ ] Tüm async task'lar döndüğünde: Loop 32 halt + Loop 33 boot planı
