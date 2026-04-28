# Loop 52 — Halt @ t=30dk (2026-04-29 01:55 TR) — HybridMomentum1m MİMARİ HATA

## Halt Sebebi
3 farklı parametre setiyle 0 SignalEmitted:

| Loop | BBstd | RSI | Vol | MinAtr | Cool | SignalEmitted | SignalSkipped |
|---|---|---|---|---|---|---|---|
| 50 (sıkı) | 2.0 | 40 | 1.2 | 0.0003 | 3 | 0 | 315 (1h) |
| 51 (gevşek) | 1.5 | 50 | 0.8 | 0.0002 | 3 | 0 | 160 (30dk) |
| 52 (agresif) | 1.3 | 55 | 0.6 | 0.0001 | 2 | **0** | **160 (30dk)** |

Parametre gevşemesinin etki ölçeği aynı kalıyor (skip rate sabit) → AND koşullarının yapısı hatalı.

## Olası Mimari Hata

7 AND koşulundan biri her zaman false dönüyor:
1. 15m close < bbLower(20, 1.3) — bant çok dar, bu sürekli sağlanır
2. 15m RSI < 55 AND RSI artıyor — RSI 55 yüksek eşik, çoğunlukla sağlanır
3. 1m EMA9 > EMA21 — momentum koşulu, sağlanır
4. 1m vol > volMA × 0.6 — gevşek, sağlanır
5. 1m atrPct ≥ 0.0001 — neredeyse sıfır, sağlanır
6. 15m BarClosed — periyodik sağlanır
7. Cooldown — yeni başlangıç, sağlanır

**Şüphe:** İndikatör hesabında bir bug ya da snapshot null dönüyor. Backend-dev'in yazdığı `TryGetHybridMomentum1mSnapshot` çift-buffer (1m + 15m) okuma → 15m buffer hala warmup'ta olabilir (300dk = 5h gerekli).

15m buffer Klines tablosundan backfill alıyor, normalde anında dolu olmalı, ama belki BB hesabı için minimum 22 bar wait yapıyor.

## Loop 41-52 Aggregate
| Loop | Strateji | Trade | Realized |
|---|---|---|---|
| 41-43 | Donchian 15m | 11 | -$2.97 |
| 44-45 | BB MeanRev 15m sıkı/gevşek | 2 | +$0.011 |
| 46-48 | EmaScalper1m (3 config) | 12 | -$1.69 |
| 49 | BB MeanRev 15m gevşetilmiş | 7 | -$0.576 |
| 50-52 | HybridMomentum1m (3 config) | 0 | $0 |
| **Total** | — | **32** | **-$5.23** |

## Karar
binance-expert tetiklendi. Cevaba göre Loop 53 boot.

— PM 2026-04-29 Loop 52 halt @ t=30
