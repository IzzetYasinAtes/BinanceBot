# Loop 46 Boot — EmaScalper1m HFS Pivot (2026-04-28 10:03 TR)

## Pivot Sebebi
Kullanıcı feedback: "saatlik 150 demiştik 3-4 yapıyoruz, sık işlem + kar lazım, kartopu etkisi". Loop 45 BB MeanRev 15m: 6h'da 2 trade (~0.33/saat). Hedef 75-150x ölçek artışı.

binance-expert AR-GE:
- 150/saat fee yutar (sermayenin %108'i = sürdürülemez)
- Pragmatik **20-30/saat** EMA9/EMA21 1m crossover scalper
- 12 coin × 1m = 720 değerlendirme/saat → koşulların %3-5'i sinyale → 20-35 sinyal/saat
- R:R 1.875:1, BE WR ~%34.8 (düşük eşik, düşük R:R yüksek WR ihtiyacı)

## Yeni Strateji — EmaScalper1m

**Giriş AND koşulları:**
1. `EMA9 > EMA21` (kısa vadeli trend)
2. `currentClose > EMA9` (fiyat momentum üstünde)
3. `RSI14 ∈ [40, 65]` (overbought/oversold reddi)
4. `currentVolume > volumeSma20 × 0.8` (gevşek volume teyidi)
5. `BarClosed == true`
6. `atrPct >= 0.0003` (sessiz piyasa engeli)

**Çıkış geometrisi:**
- TP: `entry × (1 + clamp(atr14 × 1.5 / entry, 0.003, 0.008))` → %0.30-0.80
- SL: `entry × (1 - clamp(atr14 × 0.8 / entry, 0.002, 0.005))` → %0.20-0.50
- R:R = 1.875:1, BE WR ~%34.8
- MaxHoldMinutes=8 (8 bar × 1m, hızlı scalp)
- CooldownBarsAfterSignal=2 (2dk per coin)
- Direction = LONG only

## 12 Aktif Coin
BTC, ETH, BNB, XRP, SOL, ADA, DOGE, LINK, DOT, AVAX, LTC, TRX

Diğer 17 strateji (DonchianBO15m, BbMeanRev15m, MicroScalperVwapEma30s, AtrScalperVwapEma1m) Activate=false.

## Beklenti (binance-expert)
| Senaryo | WR | Trade/saat | Net/saat (fee dahil) | Net/24h |
|---|---|---|---|---|
| Kötü | %35 | 20 | -$0.45 | -$10.80 |
| Orta | %45 | 25 | +$0.82 | +$19.68 |
| İyi | %55 | 30 | +$2.10 | +$50.40 |

Hedef: 7 gün × $3/gün = +$21 net. Orta senaryo bunu 24 saatte karşılar.

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.0000 |
| CurrentCash | $500.0000 |
| Equity | $500.0000 |
| Realized | $0 |
| Open Pos | 0 |
| Active Strategies | 12 (EmaScalper1m) |
| API Port | 5188 |
| Branch | development (altın kural #10) |
| Commit (impl) | f88d008 |

## Implementasyon
- backend-dev impl: yeni evaluator + snapshot + indicator service + DI + appsettings 12 strategy
- 283 test pass (270 önceki + 13 yeni EmaScalper1m)
- 0 build warning, 0 error
- StrategyType `EmaScalper1m = 6`

## Halt Kriter
- Realized < -$1.50 → Loop 47
- 5+ ardışık SL → Loop 47
- Zombie > 270dk → Loop 47 (MaxHold 8dk olduğu için zombie pratikte imkansız)
- Sinyal akmıyor (>2h, çünkü 1m strateji 4h beklenmez) → filtre gevşetme
- WS / CB sorun → halt

## Kritik İzleme — t30 Erken Kontrol
1m bar stratejisi olduğundan ilk sinyal 5-15dk içinde gelmeli. **t30'da 0 sinyal kalırsa filtre çok sıkı** demektir → erken Loop 47 (RSI bandı 40-65 → 30-70, Volume × 0.8 → × 0.5).

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (10:33 TR — 30dk hızlı kontrol)**

EmaScalper1m'in karakteri: yüksek frekans → erken doğrulama mantıklı.

— PM 2026-04-28 Loop 46 boot
