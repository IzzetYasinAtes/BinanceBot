# Loop 94 Spec — Peak Tracking + Wallet Semantik + Margin Wiring

Tarih: 2026-05-03 | Author: PM | Status: Backend-dev pickup ready

## Bağlam

Loop 92 boot Spot→Futures pivot'unu davranışsal olarak başardı (5 coin emit, Long+Short composer). Loop 93 commission ($0.05/pos) ve query cash formülü fix'i çalıştı. AMA Loop 93 t60'ta 3 yeni kritik bug:

1. **ExtremeMarkPrice=0** her 3 açık pozisyonda 60dk sonra hala 0 — peak/trough tracking BOZUK
2. **WalletBalance Spot semantik** — FuturesPaperFillSimulator notional düşürüyor (Loop 93'te bilinçli ertelendi)
3. **3 fixedMaxOpen darboğaz** — pozisyonlar 60dk açık kalıyor, kapanma yok, sirkülasyon donmuş

Detay: `loops/loop_93/halt-t60.md`

## Görevler (backend-dev — 3-4 commit)

### Fix #1 — ExtremeMarkPrice Peak/Trough Tracking (KRİTİK ÖNCELİK)

**Sorun**: 3 Long pozisyon 60dk açık AMA Position.ExtremeMarkPrice=0. Trailing-stop + BE-move tetiklenmiyor.

**Yöntem**:
1. `D:/repos/BinanceBot/src/Domain/Positions/Position.cs` `UpdatePeakAndCheckTrailing` (veya ExtremeMarkPrice ile ilgili method) oku — Loop 92 commit 1'de Direction-aware refactor + PeakMarkPrice→ExtremeMarkPrice rename yapıldı, regression var
2. `D:/repos/BinanceBot/src/Infrastructure/Positions/MarkToMarketWorker.cs` ExtremeMarkPrice güncelleme akışını oku — workers metodu çağırıyor mu?
3. Long: ExtremeMarkPrice = max(prev, mark). İlk tick: prev=0, mark=$84 → max(0, 84)=84. Beklenti karşılanmalı (ama 60dk sonra hala 0!)
4. Short: ExtremeMarkPrice = min(prev, mark). İlk tick: prev=0, mark=$84 → min(0, 84)=0 — **BU BUG**. Short için sentinel decimal.MaxValue veya nullable<decimal> kullan.
5. Eğer MarkToMarketWorker güncelleme branch'ini Loop 92 refactor sırasında atlamışsa: ekle.
6. Test (Position aggregate unit test): 
   - Long pos, mark=$100 → ExtremeMarkPrice=$100; mark=$110 → $110; mark=$95 → $110 (azalmaz)
   - Short pos, mark=$100 → ExtremeMarkPrice=$100; mark=$95 → $95; mark=$105 → $95 (artmaz)

### Fix #2 — FuturesPaperFillSimulator Semantik Refactor (Wallet Futures)

**Sorun**: WalletBalance=$197.56 (3 pozisyon × $100 düşülmüş — Spot cash flow). Futures'ta notional MARGIN'a alınır, Wallet'tan sadece commission düşer.

**Yöntem**:
1. `D:/repos/BinanceBot/src/Infrastructure/Trading/Paper/FuturesPaperFillSimulator.cs` oku — `PaperFillOutcome.RealizedCashDelta` hesabı
2. Open Long: `RealizedCashDelta = -fee` (notional cash'ten DÜŞÜLMEZ — margin'da locked)
3. Open Short: aynı
4. Close Long: `RealizedCashDelta = realizedPnl - exitFee`
5. Close Short: aynı (PnL formülü ters: entry - exit ile hesap)
6. PaperFillSimulator → PlaceOrderCommandHandler → VirtualBalance.ApplyFill akışı: ApplyFill'in input'u realizedCash. Yeni semantikte:
   - Open: ApplyFill(-fee) → Wallet -= fee, AllocateMarginForPosition(notional/leverage) → Margin += notional/leverage
   - Close: ApplyFill(realizedPnl - fee) → Wallet += pnl - fee, ReturnMarginAndApplyPnl(margin, pnl)
7. Test (Application + Domain): Open 1 Long $100 notional → Wallet=$499.95, Margin=$100, Equity=Wallet+Margin+UPnL=$500-fee+UPnL

### Fix #3 — AllocateMarginForPosition Wiring (Fix #2'nin parçası)

**Sorun**: Reviewer Loop 92'de "AllocateMarginForPosition wiring eksik" dedi — `VirtualBalance.AllocateMarginForPosition` metodu var ama caller yok. AllocatedMargin hep 0.

**Yöntem**:
1. `D:/repos/BinanceBot/src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommandHandler.cs` paper fill flow'unda `paperBalance.ApplyFill(...)` yanına ekle:
   - Open: `paperBalance.AllocateMarginForPosition(notional / leverage)` (default leverage=1, notional=fillPrice × qty)
2. Close path (CloseSignalPositionCommandHandler veya MarkToMarketWorker close):
   - `paperBalance.ReturnMarginAndApplyPnl(originalMargin, realizedPnl)`
3. Test: AllocatedMargin pozisyon açıldığında > 0, kapandığında geri döner

### Fix #4 (opsiyonel — Loop 95'e bırakılabilir): MaxOpenPositions Artırma

Mevcut RiskProfile.MaxOpenPositions=3. Hedef: **5** (her coin 1 pos). Frekans hedefi 30/h için pos sirkülasyonu kritik.

**Yöntem**: RiskProfile seed (Infrastructure/Risk/RiskProfileSeeder.cs) MaxOpenPositions=5 default + migration.

Eğer Fix #1+#2+#3 toplamı 3 saat aşarsa Fix #4 Loop 95'e bırak.

## Done-Definition

- 3-4 atomik commit, her biri development branch'a push
- dotnet build 0 hata 0 uyarı
- dotnet test 0 fail (mevcut 335 + yeni testler)
- Manuel verify zorunlu DEĞİL — PM bot restart ile test eder
- Her commit sonrası agent-bus MCP append_decision

## Disiplin

CLAUDE.md altın kurallar:
- Result<T> exception-for-flow yasak
- async + CancellationToken
- AsNoTracking() read path
- ILogger structured
- Deprecated yorum yok

## Bağlam Dosyaları

- `loops/loop_93/halt-t60.md` — DB snapshot + bug detay
- `loops/loop_92/halt-t30.md` — pivot ilk halt
- `docs/adr/0025-futures-short-pivot.md` — Futures pivot ADR

## Sonraki

Backend-dev fix → PM bot restart + DB reset + Loop 94 boot.md + ScheduleWakeup t30.
