# Loop 92 Boot — Futures Long+Short Live (Paper Mode)

Tarih: 2026-05-03 08:49 UTC | Bot PID: arka plan, port 5188 | Endpoint: Binance USDT-M Futures Testnet (demo-fapi.binance.com)

## Hipotez

Spot long-only 12 loop boyunca -$17.04 zarar yaptı (L80→L91, 0 pozitif loop). Pazar düşüşünde long-only çaresiz, MTF gate sıkı tutulunca 0 emit, açılınca sahte breakout. Çözüm: **Long+Short Futures**. Pazar her iki yönde de kar fırsatı; uptrend → Long emit, downtrend → Short emit.

## Yapılan Değişiklikler (14 atomik commit, bb1f580..63d717b)

1. Domain: `TradeDirection` enum (Long=1, Short=2), `Position.Side` → `Position.Direction` rename
2. Migration: `AddTradeDirectionToSignalAndPosition` (kolon rename, default 1=Long backfill)
3. Application: `IExchangeClient` port refactor (Spot `IBinanceTrading` silindi)
4. Infrastructure: `BinanceFuturesClient` REST (demo-fapi.binance.com, /fapi/v1/order, positionSide=BOTH One-way mode, workingType=MARK_PRICE stop'larda). Spot trading + market data client SİLİNDİ.
5. Infrastructure: `FuturesWsSupervisor` + `FuturesUserDataStreamWorker` (fstream.binancefuture.com, listenKey 30dk PUT cycle, reconnect+replay+heartbeat). Spot WS supervisor SİLİNDİ.
6. Infrastructure: `FuturesPaperFillSimulator` (margin akışı, taker %0.05). Spot `PaperFillSimulator` SİLİNDİ.
7. Domain: `VirtualBalance` Futures genişlemesi (WalletBalance/AllocatedMargin/UnrealizedPnl)
8. Migration: `RenameVirtualBalanceCurrentToWallet` + Position.PeakMarkPrice→ExtremeMarkPrice
9. Infrastructure: `MarkToMarketWorker` Direction-aware (Long/Short asimetrik SL/TP/BE/Trailing) + liquidation guard (%80 marginRatio)
10. Application: 7 yeni Short pattern detector (BearishEngulfing, ShootingStar, BollingerUpperReversal, BollingerSqueezeBreakDown, RsiOverboughtPullback, Ema9SlopeDown, DonchianBreakdown) + IPatternDetector.Direction
11. Application: `WeightedScorePatternComposer` Direction-aware iki-kova ("both qualified" → skip)
12. Application: `PatternCompositeEvaluator` MTF gate Direction (Long: slope>0, Short: slope<0; RSI Long>85 skip, Short<15 skip)
13. DI: appsettings.Development Futures testnet config (Trading:Mode=Futures, AllowMainnet=false). Spot kalıntı temizliği.
14. RiskProfile Futures (Leverage default 1x max 3x, MaintenanceMarginRatio 0.80, MaxFundingFeePerHour 0.001) + reset migration + Frontend Direction badge (Long yeşil, Short kırmızı)

## Reviewer + Tester Onay (Pre-Boot)

- **Reviewer**: ONAY (4 minor, blocker yok). Loop 93'e bırakılan: `AllocateMarginForPosition` wiring (AllocatedMargin hep 0 — accounting only), `FuturesFundingRateWorker` eksik, WS explicit ping frame eksik (TCP keepalive yeterli), `FuturesKlineWorker`/`FuturesBookTickerWorker` isim yerine mevcut `KlineIngestionWorker`/`BookTickerIngestionWorker` Futures URL'siyle çalışıyor (kabul edildi).
- **Tester**: PRE-BOOT READY. Build 0 hata 0 uyarı, 332/332 test pass. Migration sırası dotnet ef database update lokal MSSQL'de smoke geçti. 6 Playwright senaryo `loops/loop_92/test-scenarios.md` boot sonrası execute edilecek.

## Boot State

- Bot port: 5188
- VirtualBalance (Paper): WalletBalance=$500, AllocatedMargin=$0, UnrealizedPnl=$0, Equity=$500
- Open positions: 0
- Closed trades: 0
- RiskProfile: ConsecutiveLosses=0, CB=Healthy, MaxOpenPositions=3, RiskPerTradePct=0.02, Leverage=1x, MaintenanceMarginRatio=0.80
- Strategy: BTC-Pattern + ETH-Pattern + XRP-Pattern + SOL-Pattern + ADA-Pattern (PatternComposite, Active)
- Endpoint: REST `https://demo-fapi.binance.com`, WS `wss://fstream.binancefuture.com`
- AllowMainnet: false (ADR-0006 guard)

## KPI / Halt Eşikleri

- **Frekans hedefi (Memory #12)**: saatte 30+ trade, ≥5 coin
- **Halt eşiği**: Realized PnL < -$1.50 → halt + Loop 93 spec
- **CB**: Counter=4 → otomatik halt
- **Pivot eşiği**: 0 emit > 1 saat → ANINDA filtre gevşet veya pivot

## Beklenti (İlk t30/t60)

- 5 coin'den emit gelmesi (Long ve/veya Short, pazar yönüne göre)
- ≥1 Short emit (downtrend coin'lerden — pazar mixed olduğunda doğal beklenti)
- Composer "both qualified skip" oranı %20+ olursa Loop 93 tune
- Liquidation guard tetiklenmemesi (1x leverage, marginRatio çok düşük)

## Riskler

- Futures testnet API key kullanıcı user-secrets'ta yoksa: Paper mode `simulated_no_credentials` döner (BinanceFuturesClient defense), trade execution paper sim üzerinden gider (sorun yok).
- AllocatedMargin hep 0 görünür (Loop 93'e bırakılan minor). WalletBalance ve realized PnL doğru tutulur.
- Funding rate ilk 8h içinde tetiklenmez (MVP — funding worker yok). Liquidation engine 1x leverage'de pratikte uzak.

## Sonraki Adım

ScheduleWakeup t30 (1800s) → DB sayım + check-t30.md + commit + push + sonraki wakeup.

## Commit Aralığı

- Spec: `589dae1`
- Implementation: `bb1f580..63d717b` (14 commit)
- Boot: bu commit
