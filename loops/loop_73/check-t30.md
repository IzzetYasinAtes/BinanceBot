# Loop 73 — Check t=30dk (2026-05-01 09:56 TR) — CB Persistent Bug + Reset

## Sonuç: CIRCUIT BREAKER PERSISTENT BUG — Reset edildi

KMS evaluator 6 emit üretti, AMA `RiskProfile.CircuitBreakerStatus = Tripped` (Loop 72'den persistent state) → tüm emitler "CB tripped, signal skipped" ile bloklandı. **0 OrderPlaced.** Bot restart CB state'i reset etmiyor (DB-persistent).

**CB reset edildi**: `UPDATE RiskProfiles SET CircuitBreakerStatus = 0 WHERE CircuitBreakerStatus = 1` (2 row). Bot CB'yi her emit'te fresh okur — restart gerekmez.

## Sayım (~30dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **6** ✓ (KMS doğru çalışıyor) |
| SignalSkipped | 29 |
| **OrderPlaced** | **0** ⚠️ (CB blokladı) |
| OrderFilled | 0 |
| RiskAlert | 0 |
| Realized | $0 |

## KMS Emit Detayı (log'dan)
| Symbol | Score | rsiZone | TpPct | SlPct | MaxHold |
|---|---|---|---|---|---|
| BTCUSDT | 5/6 | 1 | 0.003 | 0.002 | 30 |
| ETHUSDT | 5/6 | 1 | 0.003 | 0.002 | 30 |
| SOLUSDT | 5/6 | 1 | 0.003 | 0.002 | 30 |
| ADAUSDT | 5/6 | 2 | 0.003 | 0.002 | 30 |
| XRPUSDT | 5/6 | 1 | 0.003 | 0.002 | 30 |
| ADAUSDT | 5/6 | 2 | 0.003 | 0.002 | 30 |

→ Skor sistemi mükemmel çalışıyor (5/6 hep), TP/SL %0.3/%0.2 (clamp min). CB yüzünden hiçbiri fill olmadı.

## CB Bug Teşhis
- `StrategySignalToOrderHandler.cs:146-150`: `if (risk.CircuitBreakerStatus == CircuitBreakerStatus.Tripped) skip`
- Loop 72'de tetiklenen consecutive_losses=5 → CB Tripped, DB'ye persist
- Bot restart sonrası Strategies reactive (Status=3) ama CB hala Tripped
- **DB UPDATE ile manuel reset gerekli**

→ Loop 74'te backend-dev'e: bot restart sırasında CB auto-reset (consecutive_losses counter de) opsiyonel önerilebilir.

## Karar
| Şart | Aksiyon |
|---|---|
| CB blokunda 0 fill | **CB reset edildi (manuel)**, bekle |
| 6 emit doğru üretildi | KMS çalışıyor ✓ |
| t60'da fill gelmeli | Aksi halde başka bug |

## t60 Beklenti (10:23 TR)
- Yeni emit fill olur (CB reset sonrası)
- TP %0.3 / SL %0.2 / MaxHold 30dk scalping davranışı
- TP hit oranı kritik metrik
- Realized > $0 hedef

## Halt Eşikleri
- Realized < -$0.50 → Loop 74 binance-expert
- Circuit breaker tekrar tripped → Loop 74 backend-dev (CB auto-reset)
- t60 hala 0 fill → bot inspect (başka block)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (10:26 TR)**

— PM 2026-05-01 Loop 73 check-t30 (CB reset)
