# Loop 93 Check t30

Tarih: 2026-05-03 10:08 UTC | Boot: 09:36 UTC | Süre: 32dk

## DB Snapshot

### Positions (3 açık, 0 kapalı)
| Symbol | Direction | Qty | Entry | UPnL | EntryCommission |
|---|---|---|---|---|---|
| XRPUSDT | 1 (Long) | 72 | $1.3905 | -$0.219 | $0.0501 ✓ |
| BTCUSDT | 1 (Long) | 0.0013 | $78565.66 | -$0.187 | $0.0511 ✓ |
| SOLUSDT | 1 (Long) | 1.19 | $84.06 | -$0.175 | $0.0500 ✓ |

**Fix #1 doğrulama**: Tüm pozisyonlarda commission $0.05 (Loop 92'de ETH $117 → şimdi normal). ✓

### StrategySignals
- Toplam: 11 emit / 32dk = ~21/h
- Direction=1 (Long): 10
- Direction=2 (Short): 1 (MaxOpen=3 dolu olduğu için pozisyon olarak açılmadı, skipped)

### VirtualBalance
- StartingBalance: $500
- WalletBalance: $197.56 ← **HALA YANLIŞ** (Spot semantik)
- AllocatedMargin: $0 (Loop 94 wiring iş)
- UnrealizedPnl: $0 (cache yok)
- Equity: $197.56

### PortfolioSummary
- currentCash: $197.56 (= WalletBalance, fix #2 ✓)
- openPositionsValue: $301.71
- trueEquity: $196.99
- unrealizedPnlTotal: -$0.58 (3 pos)
- totalCommissionPaid: $0.15 ✓
- netPnl: -$303 (yanlış görünüm — netProfitAfterFees -$0.73 gerçek)

### Risk
- ConsecutiveLosses: 0 / CB: Healthy / DD: 0%

## Analiz

**İYİ**:
- ✅ Position.EntryCommission fix çalışıyor ($0.05/pos)
- ✅ Total commission paid $0.15 doğru
- ✅ Frekans 21/h (hedef 30+, kabul edilebilir)
- ✅ 5 coin'den emit (XRP, BTC, SOL aktif pozisyon, ETH/ADA emit yok ama composer skip dağılımı normal)
- ✅ Long+Short composer çalışıyor (1 Short signal emit)

**KÖTÜ**:
- ⚠ VirtualBalance.WalletBalance $197.56 (Spot semantik — Futures'ta $499.85 olmalı). Loop 94 ana iş (FuturesPaperFillSimulator semantic refactor — backend-dev Loop 93'te bilinçli erteledi).
- ⚠ Pazar düşüşte — 3/3 pozisyon UPnL negatif (toplam -$0.58)
- ⚠ MaxOpen=3 yüzünden Short emit'i kayıp (XRP Short signal pos açamadı)

**HALT EŞİĞİ**:
- realizedPnl < -$1.50 → şu an $0 → AŞILMADI
- Gerçek ekonomik zarar (commission $0.15 + UPnL $0.58) = -$0.73 → eşiğin altında
- 0 emit > 1h → 11 emit/32dk → KARŞILANDI

## Karar: Loop 93 DEVAM, ScheduleWakeup t60

Bot davranışsal sağlıklı. Cash UI bug Loop 94 işi (FuturesPaperFillSimulator semantic refactor). Şu an pozisyonlar açık, SL/TP tetiklenmesi bekleniyor. t60'ta:
- Pozisyonlar kapanmaya başlarsa: realizedPnl izle
- Hâlâ 3/3 açıksa: Pazar yönü incelenir, MaxOpen=3 mü dar mı tartışılır
- Realized < -$1.50 ise: halt + Loop 94 spec (FuturesPaperFillSimulator semantic refactor + max open tartışması)

## Carryover

3 açık pozisyon (XRP+BTC+SOL Long), toplam UPnL -$0.58 (-0.19% notional).
