# Loop 93 Spec — Futures Accounting Bug Fix

Tarih: 2026-05-03 | Author: PM | Status: Backend-dev pickup ready

## Bağlam

Loop 92 boot başarılı oldu (Spot→Futures pivot 14 commit), bot 32dk koştu ve 5 coin'den 6 emit (1 Short BTC dahil) ile 3 pozisyon açtı. AMA accounting layer'da 2 bug tespit edildi:

1. **Position.EntryCommission $117 ETH bug** — OrderFill'de commission $0.05 doğru kayıtlı ama Position aggregate'te $117.29 yazılmış. Tek bu pozisyona özgü, BTC ($0.05) ve ADA ($0.01) doğru.
2. **PortfolioSummary cash formülü Spot kalıntı** — VirtualBalance.Wallet=$399.99 (Futures: sadece commission düşülür) ama PortfolioSummary.currentCash=$79.16 (Spot: cash - notional - commission). UI yanlış değer gösteriyor.

## Görev (backend-dev)

### Fix 1: Position.EntryCommission Hesabı

ETH pozisyonu için Position.EntryCommission $117.29 yazılmış. OrderFill.Commission tek satır $0.0508 doğru. Bu mismatch'in kaynağı muhtemelen:
- Position.AddFill (Domain) commission eklerken OrderFill.Commission yerine başka bir field okuyor
- VEYA Position.Open factory'de commission yanlış parametreyle set ediliyor
- VEYA MarkToMarketWorker fee accumulation'da bir overflow

**Yöntem**:
1. Position.AddFill metodunu oku (Domain/Positions/Position.cs)
2. PaperFillSimulator'dan dönen `PaperFillOutcome.QuoteCommissionTotal` ile karşılaştır
3. PlaceOrderCommandHandler içinde Position.AddFill çağrısı doğru parametre ile mi
4. Tek pozisyona neden özgü (3 pozisyondan ikisi doğru, biri yanlış)

**Beklenti**: ETH pozisyonu için Position.EntryCommission da $0.05 olmalı.

### Fix 2: PortfolioSummary Futures Formülü

`GetPortfolioSummaryQuery` handler şu formülü kullanıyor (Spot kalıntı):
- currentCash = StartingBalance + Σ realized - Σ open.cost - Σ open.commission

Futures'ta:
- currentCash = WalletBalance (= StartingBalance - Σ commission ± Σ realized ± Σ funding)
- openPositionsValue = Σ Position.MarkPrice × Position.Quantity (notional, görüntü için)
- trueEquity = WalletBalance + Σ Position.UnrealizedPnl

**Yöntem**:
1. `GetPortfolioSummaryQuery.cs` (Application/Portfolio/Queries) oku
2. Cash hesabını VirtualBalance.WalletBalance'tan oku, notional çıkarma
3. trueEquity = WalletBalance + Σ UnrealizedPnl
4. Test güncelle (`GetPortfolioSummaryQueryTests.cs`)

### Fix 3 (opsiyonel): AllocateMarginForPosition Wiring

Reviewer Loop 92'de "AllocateMarginForPosition / ReturnMarginAndApplyPnl wiring yok, AllocatedMargin hep 0" demişti. Loop 93'te eklemek faydalı (UI'da margin görünür) ama akut değil.

`PlaceOrderCommandHandler` içinde `paperBalance.ApplyFill(...)` çağrısının yanına `paperBalance.AllocateMarginForPosition(notional, leverage)` ekle. Position kapanınca `paperBalance.ReturnMarginAndApplyPnl(notional, realizedPnl)` çağrılır (CloseSignalPositionCommand veya MarkToMarketWorker close path).

Eğer akut değilse Loop 94'e bırak.

## Done-Definition

- 2-3 commit (Fix 1 + Fix 2 + opsiyonel Fix 3)
- dotnet build 0 hata 0 uyarı
- dotnet test 0 fail (mevcut 332 + yeni test'ler)
- Manuel verify: Bot restart + 1 pozisyon aç → Position.EntryCommission ≈ $0.05 + PortfolioSummary.currentCash = WalletBalance
- Commit + push (development)

## Disiplin

CLAUDE.md altın kurallar geçerli:
- Result<T> exception-for-flow yasak
- async + CancellationToken
- AsNoTracking() read path
- ILogger structured
- Deprecated yorum yok

## Bağlam Dosyaları

- `loops/loop_92/halt-t30.md` — bug detail + DB snapshot
- `loops/loop_92/spec-synthesized.md` — Loop 92 sentez
- `docs/adr/0025-futures-short-pivot.md` — Futures pivot kararı
- Reviewer rapor (Loop 92): `AllocatedMargin wiring eksik` notu (4. minor)

## Sonraki

Backend-dev fix tamamlanınca: PM bot restart + DB reset + Loop 93 boot.md + ScheduleWakeup t30 standart döngü.
