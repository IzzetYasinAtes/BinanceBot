# Loop 72 — Check t=90dk (2026-05-01 08:43 TR)

## Sonuç: ⚠️ ZOMBI EPIDEMIC + Capacity Check Bug, Realized -$0.015 (carry +$0.835)

15 emit/90dk frekans iyi, AMA 5 zombi (Status=2 takılı, MaxHold geçti) + 10 toplam açık pozisyon (MaxOpenPositions=5 ihlal).

## Sayım (90dk)
| Metrik | Değer | Δ (t60 → t90) |
|---|---|---|
| **SignalEmitted** | **15** | **+6** (10/h frekans) |
| OrderFilled | **15** | +7 |
| **PositionOpened** | **10** | **+5 (ikinci dalga)** |
| PositionClosed | 5 | +2 |
| RiskAlert | **0** | ✓ |
| **Realized PnL** | **-$0.015** | -$0.20 (BTC SL etkisi) |

## Trade Sonuçları (PositionClosed Event'lerden — Position row Realized doğru yazılmış)
| Symbol | Hold | PnL | Tip |
|---|---|---|---|
| BTCUSDT | ~5min | -$0.062 | SL |
| ETHUSDT | ~5min | +$0.030 | TP small |
| **SOLUSDT** | ~5min | **+$0.211** ✓ | TP |
| ADAUSDT | ~5min | -$0.090 | SL |
| XRPUSDT | ~5min | -$0.105 | SL |
| **TOPLAM** | | **-$0.015** | |

→ Position row'larında RealizedPnl **doğru yazılmış**. Sadece Status=2 takılı (3'e geçemiyor).

## ZOMBI Pattern
| Symbol | Status | Hold | MaxHold | Bug |
|---|---|---|---|---|
| BTCUSDT 10483 | 2 | **63min** | 45 | ZOMBI ✗ |
| ETHUSDT 10484 | 2 | **63min** | 45 | ZOMBI ✗ |
| XRPUSDT 10485 | 2 | **63min** | 45 | ZOMBI ✗ |
| SOLUSDT 10486 | 2 | **63min** | 45 | ZOMBI ✗ |
| ADAUSDT 10487 | 2 | **54min** | 45 | ZOMBI ✗ |

## CAPACITY CHECK BUG
- RiskProfile MaxOpenPositions=5
- Aktif: 10 pozisyon (5 zombi Status=2 + 5 yeni Status=1)
- Bot bu durumda yeni 5 emit aldı + fill yaptı → **Status=2 açık sayılmıyor**

## Yeni Açık Pozisyonlar (Status=1, ikinci dalga)
| Symbol | Hold | Entry | Mark | UPnl |
|---|---|---|---|---|
| **SOLUSDT** | 18min | $84.06 | $84.11 | **+$0.055** |
| BTCUSDT | 18min | $77108 | $77115 | +$0.010 |
| ETHUSDT | 18min | $2283.55 | $2283.40 | -$0.007 |
| ADAUSDT | 8min | $0.2492 | $0.2491 | -$0.070 |
| XRPUSDT | 3min | $1.3761 | $1.3761 | -$0.006 |

→ Net UPnl: **-$0.018** (yakın breakeven).

## Cumulative
- Loop 71: +$0.850
- Loop 72 t90: -$0.015
- **Total Realized: +$0.835**

## Karar
| Şart | Aksiyon |
|---|---|
| Realized -$0.5 ile $0 + ≥6 emit | **Loop 72 devam, t120** |
| **5+ ZOMBI + Capacity ihlal** | **Loop 73 backend-dev FIX KRİTİK** |
| RiskAlert = 0 | ✓ |
| 3 SL ardışık | (eşik altında) |

## Loop 73 Plan (paralel iş)
- backend-dev agent: PaperFillSimulator state machine fix (Status 2→3 transition + ClosedAt + ExitPrice set garanti) + RiskProfile capacity check Status IN (1,2)
- Build + test
- DB UPDATE Position'lar Status=2 → 3 manuel patch (event'lerden RealizedPnl korunur)
- Bot restart + Loop 73 boot rapor

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=120dk (09:08 TR)** — backend-dev iş bitince Loop 73 boot ile değişebilir

— PM 2026-05-01 Loop 72 check-t90
