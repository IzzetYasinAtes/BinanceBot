# Loop 22 Boot Brief — 2026-04-19 ~17:00 UTC

Loop 21 t~115'te halt. Agresif parametreler (SlopeTolerance -0.003, VwapTolerancePct 0.005, VolumeMultiplier 0.5, TpGrossPct 0.005) **işe yaradı** — 45dk'da 7 emit + 3 fill + 2 pos. Ancak 10 bug ortaya çıktı.

## Hedef
10 bug fix → kartopu test-ready sistem. Parametreler korun (Loop 21 kanıtlıyor), risk kontrolü + monitor + UI düzelt.

## 10 Bug Özet

### Backend (4)
1. **Sizing $20 yerine $40** açıyor. Formül: `max(equity × 0.20, 20)` = $20 olmalı. Audit: `SnowballSizing.CalcMinNotional` vs `StrategySignalToOrderHandler` zinciri — override/multiplication neredeyse 2×.
2. **DuplicateSignalProtection yok.** BNB 3dk arayla 2 emir → tek pozisyon $78. Aynı `(StrategyId, Symbol)` için `Open` pozisyon varsa `EmitStrategySignalCommand` handler'da skip. Domain invariant.
3. **TimeStop bozuk.** `Positions.MaxHoldDurationSeconds = NULL`. Evaluator `ContextJson`'a `maxHoldMinutes=10` yazıyor ama `StrategySignalToOrderHandler` → `Position.OpenFromSignal` bunu Position aggregate'a map etmiyor.
4. **ETHUSDT Symbols'ta yok.** `appsettings.json` → `Binance.Symbols: ["BTCUSDT","BNBUSDT","XRPUSDT"]`. ETHUSDT ekle. Strateji seed opsiyonel (sadece izleme için yeterli, evaluator ETH'a açık değil şimdilik).

### Frontend (6)
5. **Position card'da TP/SL yok.** DB'de mevcut (`stopPrice`, `takeProfit`), API döner. `positions.js` template'e 2 yeni `.kv` bloğu: "Hedef Fiyat" (yeşil) + "Stop-Loss" (kırmızı) + mark'a göre mesafe subtext.
6. **Cash negatif gösterim yanıltıcı.** `-$18.75` yerine "Kullanılabilir: $0.00" + "Limit aşıldı" badge. `dashboard.js` hero KPI condition.
7. **Kripto logoları.** BTC/BNB/XRP/ETH gerçek SVG (local `assets/logos/`, CDN bağımlılık yok). `js/components/symbolLogo.js` reusable. Kullanım: carousel, position card, order card, strategy card, klines chip, ticker marquee.
8. **BinanceBot marka logosu.** Sidebar brand'e favicon.svg'den stilize kopya + "BinanceBot" metin + hover rotate 3deg micro-interaction.
9. **Sistem Olayları filter chip çalışmıyor.** `logs.js` setup'ta `filter = ref('all')`, `filtered = computed(...)`, template'te `v-for="e in filtered"` + chip'lerde `@click="filter = 'info'"`.
10. **Ana Panel hero HALA büyük (30px).** Kullanıcı "diğer sayfalar gibi" istedi → **22-24px**. `.hero-kpi .value` + padding-y azalt (sp-5 → sp-3). Alternatif: hero kartını tamamen normal KPI grid ile değiştir.

## Parametreler korun (ADR-0016 + agresif override)
- SlopeTolerance: **-0.003** (Loop 21'den korun)
- VwapTolerancePct: **0.005** (korun)
- VolumeMultiplier: **0.5** (korun)
- StopPct: BTC/BNB 0.003, XRP 0.004 (korun)
- TpGrossPct: **0.005** (korun — %0.3 net)
- MaxHoldMinutes: **10** (korun — ama Position'a MAP EDİLECEK)

## Agent akışı (kullanıcı talimatı: soru sorma, karar ver)
1. **architect** ADR-0017 → 3 backend bug için domain kararı (TimeStop mapping, DuplicateSignalProtection, sizing doğrulama)
2. **frontend-dev** paralel → 6 UI bug fix
3. **backend-dev** (architect sonrası) → 4 backend bug fix
4. **tester** Playwright + **reviewer** SOLID/security paralel
5. DB reset + migration (gerekirse) + API restart + Loop 22 boot + t30 wakeup

## DB reset not
Loop 21 halt anında 2 Open pozisyon DB'de kaldı (manuel close reddedildi). Loop 22 migration `DELETE FROM Positions; DELETE FROM Orders; DELETE FROM OrderFills; DELETE FROM StrategySignals; DELETE FROM SystemEvents; DELETE FROM Strategies; UPDATE VirtualBalances SET CurrentBalance=100, Equity=100` ile tam temiz reset.
