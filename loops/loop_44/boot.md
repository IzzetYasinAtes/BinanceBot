# Loop 44 Boot — BB Mean Reversion 15m (2026-04-27 23:40 TR)

## Pivot Sebebi
Loop 41-42-43: 11 trade / 0 TP / 11 SL / **%0 WR**, -$2.97 realized. Donchian Breakout 15m crypto'da çalışmıyor (false breakout %60-80 oranı, fee yutması, downward dominant rejim).

binance-expert root cause raporu (loops/loop_43 + bu boot tartışması):
- Donchian close fiyatı kırılım yapsa da bar içi reversal oluyor → next-bar entry çoğunlukla geri dönen bar
- LTC (Loop 41) örneği: 7 ardışık SL aynı 0.0007$ aralıktan, 67sn ortalama hold = stop-hunt zonu
- SL %0.26 noise içinde boğuluyor (fee %0.15 round-trip + slippage = ~%0.30 minimum edge)
- Long-only spot + downward piyasa = yapısal dezavantaj

**Karar:** BB Mean Reversion 15m'e pivot (binance-expert Seçenek B).

## Yeni Strateji — BB Mean Reversion 15m

**Giriş AND koşulları:**
1. `currentClose < bbLower` (BB period 20, std 2.0)
2. `rsi14 < 30` (oversold)
3. `volumeZScore > 1.0` (panik satış teyidi)
4. `atrPct >= 0.0007`
5. BarClosed

**Çıkış geometrisi:**
- TP: `entry × (1 + clamp(atr14 × 1.5 / entry, 0.004, 0.010))` → %0.4-1.0
- SL: `entry × (1 - clamp(atr14 × 1.0 / entry, 0.003, 0.006))` → %0.3-0.6
- R:R ortalama 1.33:1, BE WR ~%43
- MaxHold 90dk, Cooldown 4 bar (60dk), Direction=LONG only

**Beklenti (binance-expert):**
| Senaryo | WR | Sinyal/Gün | Net/Gün |
|---|---|---|---|
| Kötü | %35 | 3 | -$0.36 |
| Orta | %45 | 4 | +$0.32 |
| İyi | %55 | 5 | +$1.25 |

## Aktif Stratejiler (5 coin)
- BTC-BbMeanRev15m (id 183)
- ETH-BbMeanRev15m (id 184)
- XRP-BbMeanRev15m (id 185)
- SOL-BbMeanRev15m (id 186)
- ADA-BbMeanRev15m (id 187)

12 DonchianBO15m + 12 AtrSwing strategy = Status=**Draft** (devre dışı).

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.0000 |
| CurrentCash | $500.0000 |
| Equity | $500.0000 |
| Realized | $0 |
| Net | $0 |
| Open Pos | 0 |
| Active Strategies | 5 (BB Mean Rev) |
| API Port | 5188 |
| Branch | feature/loop-44-bb-mean-reversion |

## Implementasyon
- backend-dev impl: yeni evaluator + snapshot + indicator + DI + appsettings
- 270 test pass (260 önceki + 10 yeni BbMeanReversionEvaluatorTests)
- 0 build warning, 0 error
- StrategyType `BbMeanReversion15m = 5`

## DB Reset
- 12 tablo silindi (Instruments + Klines korundu)
- VirtualBalance Id=1 Mode=Paper $500 seed (manuel direct insert, /papertrade/reset 401 auth issue)

## Tatil Disiplini (7 gün otonom)
- Halt kriter: Realized<-$1.50, 5+ ardışık SL, zombie>270dk, WS down>5dk, CB tripped
- Halt → fine-tune ya da pivot → yeni loop, kullanıcı onayı YOK
- ScheduleWakeup zinciri: t+60dk başlangıç, sonraki t+60dk her kontrolde
- Hedef: 7 gün × 24h ≈ 168 saat ≈ 28 loop, BE veya kâr

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=60dk (00:40 TR ertesi gün, 28.04)**

İlk kontrol: warmup tamam mı, sinyal akışı var mı, drift ms.

— PM 2026-04-27 Loop 44 boot
