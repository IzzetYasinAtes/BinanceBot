# Loop 111 Boot — Position Lifecycle Bug Fix Bekleyen

Tarih: 2026-05-06 01:28 UTC | Bot port 5188

## Loop 110 → 111 Geçiş

Loop 110 tepe noktasında netPnl +$2.42 (trueEquity $502.42) gözlendi AMA realize edilemedi. PaperTrade reset force-close DELETE yapıyor → cumulative -$0.17 + ADA +$1.17 + BTC -$0.11 = +$0.89 net realized OLMADI.

5 pozisyon lifecycle bug tespit edildi:
1. SL hit semantic Long: mark <= SL exit (BE armed sonrası AÇIK kalıyor)
2. MaxHold timeout 60min aşımı pos kapanmıyor
3. Hard MaxHold safety net 120dk tetiklemiyor (Loop 109 fix aktif değil)
4. Trailing peak update SL'i yukarı taşımıyor
5. PaperTrade reset realize değil delete

## Boot State (Şu An)

- Bot ayakta, port 5188
- Wallet $500, 0 pos (PaperTrade reset)
- ResetCount 28, force-closed 2 (delete, realize değil)
- CB Healthy, Strategies Active=3 ✓

## Backend-dev Delegasyon (Loop 111 Asıl İş)

Position lifecycle bug fix — 4-5 commit:
1. MarkToMarketWorker SL hit semantic Long+Short (BE armed sonrası bile)
2. MaxHold timeout aktif (pos.MaxHoldDuration <= now-OpenedAt → exit)
3. PositionSafetyOptions Hard MaxHold cycle tetik kontrol
4. Position.UpdatePeakAndCheckTrailing SL update yansıması
5. PaperTradeResetCommand realize-and-keep variant veya yeni endpoint

## Bekleyen

backend-dev delegasyon başlayana kadar bot çalışmaya devam ediyor. Yeni emit + pos açılacak (RPT 0.005, R:R 1:1, MTF 0.002, BE 0.001).

## Cumulative

30 loop -$26.5+ (cumulative all-time), Loop 110'un +$0.89 net realize EDİLEMEDİ. Sermaye $500'de baştan.

## Sonraki

ScheduleWakeup t30, paralel backend-dev delegasyon başlatılacak.
