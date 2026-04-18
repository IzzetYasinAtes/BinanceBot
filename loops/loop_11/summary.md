# Loop 11 Özeti (HALT t30)

## Süre: 30dk
- Paper $100 → $99.78 (-%0.22, neredeyse breakeven)
- peakEquity unrealized $164.56 (intraday spike), DD %39.37 → CB Tripped
- 0 Open / 2 Closed (BNB Short -$0.10, BTC Long ~)
- Stop-loss + TP tetiklemeleri çalıştı ✓

## İlerleme (Loop 10'dan)
- Pozisyonlar GERÇEKTEN KAPANIYOR (Loop 10 hep open kalıyordu)
- MeanRev stop aktif
- TP yakın (1.5x ATR) realize ediyor

## Root Sorun (Loop 12'ye reform)
peakEquity = mark-to-market (balance + unrealized) — intraday unrealized spike'lar peak yakalıyor → equity normal volatility ile düşüyor → CB false-trip → strateji paused.

Çözüm: peakEquity = realized-only (sadece cash balance).

## Karar: MUTATE → Loop 12
EquityPeakTrackerService GetRealizedEquityAsync (yeni) çağırsın. VirtualBalance.CurrentBalance zaten realized-only.
