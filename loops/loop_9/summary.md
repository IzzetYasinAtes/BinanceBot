# Loop 9 Özeti (HALT t30)

## Süre: 30dk
- Paper $100 → **$82.81** (-%17.19)
- peakEquity $114.45 (intraday tracker doğru)
- DD %27.64 → CB Tripped doğru ("drawdown_24h=%27,64>=%20,00")
- Yumuşatılmış %20 24h tavanı bile aşıldı

## Root Sorun
**Take-Profit YOK.** Stratejiler pozisyon açıp tutuyor. Equity intraday peak'e ulaşıyor sonra düşüyor → realize edilmemiş kar buharlaşıyor.

## Karar: MUTATE → Loop 10
Take-Profit ekleme (TakeProfitMonitorService + Position.TakeProfit + evaluator TP hesaplama).
TrendFollowing TP = entry ± ATR×3 (R:R 1.2 stop=2.5x, tp=3x)
MeanReversion TP = BB mean band
Grid TP yok (ters yön zaten exit görevi)
