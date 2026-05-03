# Loop 93 Halt — t60 KRİTİK PEAK TRACKING BUG

Tarih: 2026-05-03 10:40 UTC | Boot: 09:36 UTC | Süre: 64dk

## Özet

Loop 93 commission fix'i çalıştı ($0.05/pos doğrulandı). Cash formülü fix'i de query yansıttı (PortfolioSummary.currentCash = WalletBalance). AMA **3 yeni bug tespit edildi**:

1. **ExtremeMarkPrice=0** (peak/trough tracking BOZUK) — 3 pozisyon 60dk açık ama Position.ExtremeMarkPrice hiç güncel olmamış. Trailing-stop ve BE-move ÇALIŞMIYOR.
2. **WalletBalance Spot semantik kalıntı** ($197.56, beklenen $499.85) — FuturesPaperFillSimulator hala notional düşürüyor (Loop 93'te bilinçli ertelendi)
3. **MaxOpenPositions=3 darboğaz** — yeni emit pozisyon açamıyor (15 emit/h, sadece 3 pos açık 60dk boyunca, kapanma yok)

Sonuç: Bot dondu — pozisyonlar açıldı (-$0.65), 60dk hareketsiz, SL/TP/Trailing hiç tetiklenmedi.

## DB Snapshot

### Open Positions (3, hepsi 60+ dk hold)
| Symbol | Direction | Entry | Mark | UPnL | SL | TP | **ExtremeMarkPrice** | BE | Hold |
|---|---|---|---|---|---|---|---|---|---|
| SOLUSDT | Long | $84.062 | $83.925 | -$0.163 | $83.684 | $84.692 | **0.00** ⚠ | null | 61min |
| BTCUSDT | Long | $78565.66 | $78424.55 | -$0.183 | $78212.29 | $79154.61 | **0.00** ⚠ | null | 60min |
| XRPUSDT | Long | $1.3905 | $1.3886 | -$0.133 | $1.3841 | $1.4008 | **0.00** ⚠ | null | 60min |

**ExtremeMarkPrice=0 anomalisi**: Long pozisyon için peak = max(prev, mark) — ilk tick'te mark price'a yazılmalıydı (~$84, ~$78400, ~$1.39). 60dk sonra hala 0 — MarkToMarketWorker ya bu metodu çağırmıyor ya da Direction-aware refactor sırasında bir branch bozuldu.

### Signals (15 toplam, 14 Long + 1 Short)
- Frekans: 15/h (hedef 30+, yetersiz)
- Direction=2 (Short) sadece 1 (composer Short emit etmek için MTF gate'in slope < 0 koşuluna takılıyor — 5 coin tamamı uptrend olabilir)

### VirtualBalance
- StartingBalance: $500
- WalletBalance: $197.56 (Spot semantik bug — Loop 93'te ertelendi)
- AllocatedMargin: $0
- Equity: $197.56

### Risk
- ConsecutiveLosses: 0 / CB: Healthy / DD: 0%

## Halt Sebebi

1. **Peak tracking bug = trailing/BE çalışmıyor**: pozisyonlar dondu, SL/TP'ye kadar bekleyemez (saatler sürer), bot atıl.
2. **MaxOpen=3 + 0 close = sıkışma**: yeni emit pozisyon açamıyor, pozisyon sirkülasyonu yok, kartopu kar mümkün değil.
3. **Wallet Spot semantik = cash UI yanıltıcı**: ekonomik gerçek -$0.65 ama UI -$303 gösteriyor (kullanıcı paniği).

Halt eşik (realized < -$1.50) teknik aşılmadı (realized=$0) ama 3 bug fix edilmeden devam etmek anlamsız — peak tracking olmadan strateji etkin değil.

## Loop 93 Net Sonuç

- ✅ Position.EntryCommission fix doğrulandı ($0.05/pos)
- ✅ PortfolioSummary cash query fix yansıdı (currentCash = WalletBalance)
- ⚠ ExtremeMarkPrice peak/trough tracking BOZUK (kritik bug — Loop 92 commit 1+8 sırasında PeakMarkPrice→ExtremeMarkPrice rename + Direction-aware refactor regression yaratmış)
- ⚠ WalletBalance Spot semantik (Loop 93 ertelendi)
- ⚠ MaxOpen=3 darboğaz

## Loop 94 Spec'e Geçiş

Backend-dev'e 3 bug fix delegasyonu (öncelik sırası):

### Fix #1: ExtremeMarkPrice Peak/Trough Tracking (KRİTİK)
- `Position.UpdatePeakAndCheckTrailing()` Direction-aware (Loop 92 commit 1) implementasyonunda regression
- Long: `ExtremeMarkPrice = max(prev, mark)` (ilk tick'te 0 → mark)
- Short: `ExtremeMarkPrice = min(prev, mark)` (ilk tick'te 0 → mark, AMA 0 < herhangi mark → bug! Short için sentinel decimal.MaxValue veya nullable kullan)
- VEYA: MarkToMarketWorker bu metodu hiç çağırmıyor (Direction-aware refactor sırasında bypass edilmiş)
- Test: 1 Long pos 5dk açık → ExtremeMarkPrice ≈ mark (0 değil); 1 Short pos 5dk → ExtremeMarkPrice ≤ mark

### Fix #2: FuturesPaperFillSimulator Semantic Refactor (Wallet Futures)
- Open Long: `realizedCash = -fee` only (notional MARGIN'a alınır, cash'ten düşmez)
- Open Short: aynı (Futures'ta SELL leg'i de margin allocate eder)
- Close: `realizedCash = realizedPnl - fee`, ReturnMarginAndApplyPnl
- PlaceOrderCommandHandler + CloseSignalPositionCommand + MarkToMarketWorker close path koordineli
- Test: Open 1 Long $100 notional → WalletBalance = $500 - $0.05 = $499.95 (NOT $399.95)

### Fix #3: AllocateMarginForPosition Wiring (Fix #2 ile birlikte)
- PlaceOrderCommandHandler içinde `paperBalance.AllocateMarginForPosition(notional, leverage)` ekle
- Close path'te `paperBalance.ReturnMarginAndApplyPnl(margin, realizedPnl)` ekle
- AllocatedMargin pozisyon başına notional/leverage yansır

### Fix #4 (opsiyonel, Loop 95'e): MaxOpenPositions artırma
- Mevcut: 3 (RiskProfile)
- Hedef: 5 (her coin 1 pos olabilir)
- VEYA: Aynı coinde Long+Short hedge mode (Futures destekliyor ama composer şu an "both qualified skip" dediği için zaten engelli)
- Loop 94'te isteğe bağlı, Loop 95'te yapılabilir

## Sonraki Adım

`loops/loop_94/spec.md` yazılacak (bu halt'ın detaylı spec versiyonu) → backend-dev delege → 3-5 commit → Loop 94 boot.
