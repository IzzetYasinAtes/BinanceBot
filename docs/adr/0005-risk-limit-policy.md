# 0005. Risk Limit Politikasi

Date: 2026-04-16
Status: Accepted

## Context

Bir crypto trading bot u en cok "zekice fikirle" degil, **risk disiplini** ile hayatta kalir. [docs/research/binance-research.md §6](../research/binance-research.md) ve §4.2 akademik kaynaklar asagidakileri net koyuyor:

- **Kelly criterion full**: %50+ drawdown uretir; crypto volatilitesinde *Quarter Kelly* onerilir.
- **Risk-per-trade konsensus: %1-2** sermaye.
- **Client-side stop** WebSocket koparsa calismaz — May 19, 2021 ve Aralik 2025 flash wick olaylarinda likidasyon yasandi. Cozum: server-side `STOP_LOSS_LIMIT`.
- **Max drawdown**: trend following tipik %20-40; kill-switch %25 uzeri trading pause.
- **Ardisik kayip**: backtest overfitting in real-world sinyali; 3+ kayip sonrasi stratejinin yeniden degerlendirilmesi.

BinanceBot otomatik trade eden bir sistem olacak; hatali karar insan operatorden once bot tarafindan tespit edilip durdurulmalidir.

## Decision

`RiskProfile` aggregate i global (tek row) uygulama genelinde geçerli risk kurallarini tutar. Default degerler asagidadir; admin UI/API uzerinden duzenlenebilir.

### 5.1 Risk-Per-Trade

- **Varsayilan: %1** (Quarter Kelly yaklasimina guvenli taban).
- **Max: %2** — UI bu ustu editleyemez; editlemek istiyorsa once `RiskProfile.OverrideCaps` admin action i gerek (audit log larda).
- Pozisyon sizing formulu: `positionSize = (equity * riskPerTrade) / (entry - stopLoss)`.
- `OrderPlacementService` her `PlaceOrderCommand` inde:
  - `RiskProfile.RiskPerTradePct` cek.
  - Gecerli `UserBalance` ve `stopLoss` ile pozisyon boyutunu hesapla.
  - Stratejinin istedigi boyut ustunde ise `Result.Invalid("position size exceeds risk limit")`.

### 5.2 Max Position Size

- **Varsayilan cap: sermayenin %10 u tek symbol de**; toplam gross exposure %30.
- Breach -> `RiskLimitBreachedEvent` + order reject.

### 5.3 Server-Side Stop-Loss (Sert Kural)

- **Her** yeni trade pozisyonu acildiginda, paired `STOP_LOSS_LIMIT` order Binance te yerlestirilir.
- `OrderPlacementService` atomik iki aşamali: (a) entry order, (b) entry fill sonrasi `OrderFilledNotification` -> `StopLossPlacementHandler` -> `STOP_LOSS_LIMIT` order. Stop yerlestirilemezse (OCO reddi vb.) entry order **market close** ile cikartilir (failsafe).
- **Client-side stop (bot icindeki if-else) YASAK.** `strategy-runner` background service i fiyat takibiyle stop uygulamaz — bag koparsa korumasiz kalir.
- `STOP_LOSS_LIMIT` mesafesi default `2 * ATR(14)` trend stratejiler, `1.5 * ATR(14)` mean reversion (binance-research.md §7).

### 5.4 Circuit Breaker

- **MaxDrawdownPct**: default %5 (24 saatlik pencere), hard cap %25 (tum zamanlar). Ikisi de `RiskLimitBreachedEvent` + `CircuitBreakerTrippedEvent` tetikler.
- **MaxConsecutiveLosses**: default 3. Ustune cikinca etken strateji otomatik `Deactivate()`.
- **Symbol ATR spike**: tek bar hareket > 5 * ATR(14) -> pause (flash crash korumasi).

### 5.5 Circuit Breaker State Machine

`RiskProfile.CircuitBreakerStatus` enum: `Healthy -> Warning -> Tripped -> Cooldown -> Healthy`.

| Gecis | Tetikleyici | Davranis |
|---|---|---|
| Healthy -> Warning | Ardisik 2 kayip veya draw %3 | Log + alarm, trade acik kalir |
| Warning -> Tripped | Ardisik 3 kayip veya draw %5 | **Trading pause**: yeni order yasak, `StrategyDeactivatedEvent` yayinla |
| Tripped -> Cooldown | Manuel `ResetCircuitBreakerCommand` | 1h cooldown; yeni order yasak devam |
| Cooldown -> Healthy | 1h sure dolum + manuel onay | Trading acik |

`ResetCircuitBreakerCommand` handler i user-authenticated admin rolu ister; audit log a kayit.

### 5.6 Pre-Trade Gate

`PlaceOrderCommand` handler inin ilk 5 satiri:

```
1. RiskProfile yukle
2. CircuitBreakerStatus Tripped/Cooldown mi? -> Result.Invalid
3. Position size limit kontrolu
4. Risk-per-trade kontrolu
5. Server-side stop emir hazirligi (paired)
```

Bu gate ge disinda order atilamaz. `OrderPlacementService` bu adimi bypass edemez.

## Consequences

### Pozitif

- Flash crash / bag kopmasi senaryolarinda otomatik koruma.
- Backtest overfitting in real-world basarisizligini 3 kayipla tespit edip durdurma.
- Akademik risk yonetimi pratiklerine (Quarter Kelly, %1-2 risk-per-trade) uyum.

### Negatif / Tradeoff

- Siki risk kurallari stratejinin win rate ini test ortaminda daha dusuk gosterir (bazi iyi giris leri cikartir). Kabul edilebilir — uretim guvenligi birinci oncelik.
- Server-side stop i her order la beraber acmak Binance ORDERS rate-limit quota sini hizli tuketir. Cozum: `LIMIT_MAKER` entry + OCO ile tek cagri (bkz. `/api/v3/order/oco/new` binance-research.md §3.3).
- Circuit breaker Tripped iken manuel reset gereklilik; bot tam otomatik degil — bilincli tasarim (insan gozetim disiplini).

### Notr

- `RiskProfile` tek row tutar ama audit gecmisi `RiskProfileHistory` tablosunda (her update snapshot).

## Alternatifler

1. **Full Kelly** — %50+ drawdown akademik olarak dogrulanmis, kabul edilemez. Reddedildi.
2. **Client-side trailing stop** — Bag kopmasi riski. Binance `trailingDelta` parametresi server-side zaten var; kullan. Client-side reddedildi.
3. **Sabit USD risk-per-trade** — Sermaye buyudukce/kuculdukce orantisiz kalir. Yuzdeye gore Reddedildi.
4. **Circuit breaker auto-reset** — Overfitting dongusune kapi acar. Manuel reset sart.

## Kaynak

- [docs/research/binance-research.md §4.3 Strateji Kataloğu](../research/binance-research.md)
- [docs/research/binance-research.md §6 Red Flag Taramasi](../research/binance-research.md)
- [docs/research/binance-research.md §6.7 Black Swan](../research/binance-research.md)
- [docs/research/binance-research.md §7 Ilk 3 Strateji](../research/binance-research.md)
- [binance-spot-api-docs — rest-api.md (STOP_LOSS_LIMIT, OCO)](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md)
- [Kelly Criterion fractional guide](https://cryptogambling.com/guides/sports-betting/fractional-kelly-practical)
- [May 19, 2021 crash (AUT Research)](https://acfr.aut.ac.nz/__data/assets/pdf_file/0009/686754/6b-Tim-Baumgartner-May19.pdf)
