# Loop 81 — HALT @ t=210dk (2026-05-02 14:38 TR) — CB Tripped (4 Ardışık Küçük Loss)

## Halt Sebebi: 4. Ardışık BE/Trailing-Exit Küçük Loss → CB Tripped

t180→t210: **+1 close (XRP 2.)** -$0.184 + RiskAlert (consec_losses=4 = MaxConsecutiveLosses limiti). Bot CB tripped, emit verme yetkisi yok. **Loop 82 binance-expert spec çağrıldı (paralel).**

## Sayım (210dk)
| Metrik | t180 | **t210** | Δ |
|--------|------|----------|---|
| SignalEmitted | 5 | 8 | +3 |
| OrderFilled | 7 | 10 | +3 |
| PositionOpened | 4 | 6 | +2 (BTC + XRP 2.) |
| **PositionClosed** | 3 | **4** | **+1 (XRP 2. BE-stop)** |
| **RiskAlert** | 0 | **1** | **+1 (CB consec_losses=4)** |
| **Realized PnL** | -$0.20 | **-$0.38** | **-$0.18** |

## 4 Close Detay (Hepsi Küçük Loss)
| # | Symbol | Hold | Peak Δ | Exit Tipi | PnL |
|---|--------|------|--------|-----------|-----|
| 1 | SOL | 69min | +%0.33 | trailing-exit | -$0.003 |
| 2 | ETH | 109min | +%0.26 | trailing-exit | -$0.055 |
| 3 | XRP-1 | 99min | +%0.11 | BE-stop | -$0.141 |
| 4 | **XRP-2** | **15min** | **+%0.16** | **BE-stop** | **-$0.184** |

→ **4/4 = %100 küçük loss WR**. Peak +%0.11-0.33 kâr potansiyeli, ama trailing %0.15 + komisyon %0.20 = net %0.35 minimum gerek. Hiçbir trade bu eşiği geçemedi.

## Açık (CB Tripped Sonrası — Yeni Emit Yok)
| Symbol | Hold | UPnl | Risk |
|--------|------|------|------|
| ETHUSDT | 83min | -$0.158 | SL hit Realized -$0.55 |
| BTCUSDT | 14min | -$0.085 | yeni emit, henüz değerlendirme erken |

## Root Cause: Trailing/BE Buffer Yetersiz
- Komisyon eşiği: %0.20 (entry+exit taker)
- Trailing buffer: %0.15 (çok dar)
- BE move sonra fiyat geri çekme → küçük loss kalıbı
- **Net break-even için peak gerek: +%0.35** (4 close hiçbiri sağlamadı)

## Loop 82 Plan (binance-expert paralel)
binance-expert spec çağrıldı — beklenen önerilerden:
1. **Trailing buffer 0.0015 → 0.0025-0.0040** (komisyon kapsama)
2. **BE eşiği +%0.10 → +%0.20-0.25** (peak'in komisyon eşiği üstü olması zorunlu)
3. **R:R 1:2 → 1:1.5** (binance-expert eski spec, daha gerçekçi)
4. **Komisyon-aware trailing**: peak × (1 - 2 × feeRate)
5. **MinHold 2-3 bar** (10-15dk noise filtreleme)

## L80 vs L81 vs L82 Beklenti
| Metrik | L80 t210 | **L81 t210** | L82 hedef |
|--------|----------|--------------|-----------|
| Emit | 7 | 8 | ≥10 |
| Closed | 3 | 4 | ≥4 |
| Realized | -$0.51 | **-$0.38** | ≥-$0.20 |
| WR | 0/3 | 0/4 | ≥%30 (1/4 win) |

L81 hâlâ L80'den daha iyi (-$0.38 vs -$0.51), AMA WR=%0 → algoritma nikel-dime sürünüyor.

## Cumulative Yörünge
- L1-L80: -$13.97
- L81: -$0.38 (4 close, 2 açık)
- **TOTAL: -$14.35** ($500'den -%2.87)
- Eğer ETH SL hit olursa: -$14.55+

## Halt Aksiyon Planı
1. ✅ binance-expert çağrıldı (paralel, async)
2. ⏳ Açık 2 pozisyonun (ETH, BTC) kapanmasını bekle (1-2h muhtemel)
3. ⏳ binance-expert spec gelince → backend-dev parametre güncelleme delege
4. ⏳ Bot kill + dotnet build/test + DB UPDATE param + CB reset + Bot restart
5. ⏳ Loop 82 boot.md + ScheduleWakeup t30

## Sıradaki: ETH/BTC Outcome İzleme
Wakeup 1500s sonra (t240) — açık pozisyonların durumu + binance-expert spec gelmiş olabilir.

— PM 2026-05-02 Loop 81 HALT @ t=210 (4 ardışık küçük loss → CB tripped → Loop 82 trailing/BE fine-tune)
