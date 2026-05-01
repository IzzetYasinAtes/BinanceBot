# Loop 77 — Check t=30dk (2026-05-01 17:55 TR) — EMA200 Gate Geçti, Yine SL

## Sonuç: 1 Emit ADA -$0.45 SL — EMA200 Gate Geçti Ama Entry Hala Loss

EMA200 trend gate (close $0.2498 > ema200 $0.2483 ✓) ve hard-gate'ler geçti, ADA emit oldu (score 4/7). AMA fiyat hızla düştü, SL hit, **-$0.45 loss**. Ardışık SL counter persistent → CB tripped (sadece 1 yeni close ile, önceki Loop 76 SL hala counter'da).

## KMS Emit Detay (log)
```
[17:35:00 INF] KMS emit symbol=ADAUSDT score=4/7 coinClass=alt
  rsiZone=1 slope=0 surge=1 spread=1 atr=1 bbw=0
  entry=0.24980 stop=0.24930 tp=0.25032
  tpMul=1.3 slMul=0.65 (LowScore — score=4 minimum)
  ema200=0.24830 (close > ema200 ✓ EMA gate GEÇTİ)
  bbwValue=0.00645 (< 0.008 → 0 puan, trend zayıf!)
  decision=Emit
```

→ **BBW 0.0064 < 0.008** = trend strength düşük (uyarı sinyali). Bot yine de emit verdi (BBW skor nice-to-have, hard-gate değil).

## Sayım (~30dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **1** (RsiCeiling 70 ile EMA200 sonrası) |
| SignalSkipped | 14 |
| OrderPlaced | 2 (1 entry + 1 exit) |
| OrderFilled | 2 |
| PositionOpened | 1 |
| **PositionClosed** | **1** (ADA -$0.45 SL) |
| **RiskAlert** | **1** (CB tripped) |
| **Realized PnL** | **-$0.45** |

## CB Counter Persistent Bug
1 yeni SL'de CB tripped — `consecutive_losses=5` counter Loop 76'dan kalan SL'leri sayıyor. CB reset sırasında counter da reset olmuyor olabilir.

→ Loop 78 backlog backend-dev: CB reset → counter reset garantisi.

## Düzeltme
- **CB API reset**: 200 OK ✓
- **5 KMS strategies reactivated** (Status=3) ✓

## Karar
| Şart | Aksiyon |
|---|---|
| Realized -$0.45 < -$0.30 | bekle t60 (entry kalitesi etki için zaman) |
| EMA200 gate geçti ama loss | BBW hard-gate düşünülebilir (trend zayıfta emit yok) |
| 1 emit / 30dk | Frekans iyi, MinScore 4 sabit |

## t60 Beklenti (18:25 TR)
- Yeni emit + EMA200 gate filter etkisi (trend yukarı coin'lerde emit)
- BBW pozitif coin'lerde +1 puan, score 5/7 emit
- Realized iyileşme ya da Loop 78 binance-expert (BBW hard-gate)

## Halt Eşikleri
- Realized < -$1.50 → Loop 78 BBW hard-gate
- Circuit breaker → API reset
- 5+ ardışık SL → CB reset + counter sıfırlama

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (18:25 TR)**

— PM 2026-05-01 Loop 77 check-t30 (EMA200 gate geçti, BBW hard-gate düşünmek)
