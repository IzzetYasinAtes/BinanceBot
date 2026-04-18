# Loop 8 Özeti (HALT t30 — KAR'LI)

## Süre: 30dk
- Paper $100 → **$112.24 (+%12.24)** — KAR
- peakEquity intraday: $131.30 (tracker çalışıyor)
- DD %14.51 → **CB Tripped doğru** ✓ "drawdown_24h=%14,51>=%5,00"
- Bug #19 fix doğrulandı — CB doğru tetikledi

## Halt sebebi
CB Tripped → 3 strateji Paused → trade akışı durdu (Loop 6/7 ile aynı senaryo, ama bu kez KAR'LI durumda donmak gerekiyor değil).

## Karar: MUTATE → Loop 9
**Risk parametre revize:** Max DD %5 → %20 (24h), %25 → %40 (alltime). Conservative başlangıçta normal volatility (%14) bile trip'liyordu. Daha esnek tut, gerçek kayıplara reaksiyon ver.

`appsettings.json` `RiskProfile.Defaults`:
- MaxDrawdown24hPct: 0.05 → **0.20**
- MaxDrawdownAllTimePct: 0.25 → **0.40**
- MaxConsecutiveLosses: 3 → **5**

DB drop, Loop 9 boot. Kod değişimi YOK (sadece config).
