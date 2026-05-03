# Loop 92 — Spec Synthesized (PM)

Tarih: 2026-05-03 | Author: PM | Status: Backend-dev pickup ready

4 paralel agent çıktısının birleşik özeti + çelişki çözümleri + backend-dev brief.

---

## 1. Kaynaklar

| # | Agent | Çıktı | LOC | Önem |
|---|---|---|---|---|
| 1 | binance-expert | `loops/loop_92/spec-binance-expert-futures.md` | 575 satır, 23.5KB | Resmi Binance API spec — canlı WebFetch ile doğrulanmış |
| 2 | architect | `docs/adr/0025-futures-short-pivot.md` + `loops/loop_92/architecture-plan.md` | ADR + 14-commit plan | Mimari karar + atomik adım planı |
| 3 | Explore | `loops/loop_92/code-audit.md` | Mevcut kod haritası | Risk haritası: 900-1500 LOC tahmini |
| 4 | backend-dev | `loops/loop_92/cash-ui-audit.md` | Cash bug audit | GetPortfolioSummaryQuery TEMİZ; balances endpoint P2 risk |

---

## 2. Kritik Çelişkiler ve Çözümler

### 2.1 Testnet URL — binance-expert haklı

- **architect**: `https://testnet.binancefuture.com` (architecture-plan.md sat 367)
- **binance-expert (WebFetch ile canlı doğrulama)**: `https://demo-fapi.binance.com` (REST), `wss://fstream.binancefuture.com` (WS)
- **Karar**: binance-expert URL'leri kullanılır. `appsettings.Development.json` Futures testnet config:
  ```json
  "Binance": {
    "RestBaseUrl": "https://demo-fapi.binance.com",
    "WsBaseUrl": "wss://fstream.binancefuture.com",
    "AllowMainnet": false
  }
  ```

### 2.2 Taker Fee Rate — binance-expert haklı

- **architect**: %0.04 (architecture-plan.md commit 6)
- **binance-expert (canlı doğrulama)**: %0.05 taker, %0.02 maker
- **Karar**: Taker %0.05 kullanılır. `PaperFeeOptions:TakerFeePct=0.0005` (5bp).

### 2.3 PositionSide enum vs Value Object

- **Explore bulgusu**: `PositionSide` ZATEN enum (Long=1, Short=2) — Position.cs:38-42'de
- **architect kararı**: "PositionSide value object silinir" → aslında enum'u rename değil, **TradeDirection enum** olarak Domain/Common'a taşı (concept duplication temizlik)
- **Karar**: Mevcut `Domain/ValueObjects/PositionSide.cs` (enum, value object değil) → `Domain/Common/TradeDirection.cs` rename. Enum value'lar aynı kalır (Long=1, Short=2). `Position.Side` field → `Position.Direction` rename. Migration: kolon adı değişir.

### 2.4 Spot Kod Tamamen Silinsin mi

- **Explore bulgusu**: Çoğu domain entity zaten side-agnostic (Position, Order, StrategySignal Direction enum'lara sahip)
- **architect kararı**: Spot kod TAMAMEN sil (CLAUDE.md §13 deprecated yasak)
- **Karar**: architect haklı. Spot-spesifik sınıflar silinir:
  - `BinanceTradingClient` (Spot REST)
  - `BinanceMarketDataClient` (Spot REST)
  - `BinanceWsSupervisor` (Spot WS)
  - `BinanceStreamParser` (Spot stream — Futures parser'a rewrite)
  - `PaperFillSimulator` (Spot — `FuturesPaperFillSimulator`'a rewrite)
  - Spot-only worker'lar (varsa)

  Ama **side-agnostic helper'lar korunur**: `BinanceSignatureHelper`, `OrderSide` enum, `OrderType` enum, `TimeInForce` enum (hepsi Futures'la uyumlu).

### 2.5 Cash UI Audit Aksiyonları

backend-dev cash-ui-audit.md'de 2 öneri verdi:
1. `/api/balances` endpoint stale snapshot riski (P2) → Loop 92 commit 7-8 (VirtualBalance refactor) ile zaten çözülecek (Wallet+AllocatedMargin+UnrealizedPnl ayrı). Endpoint kalırsa yeni shape'e döner.
2. `cashClamped` clamp ölü kod (P3) → `dashboard.js:339-349` Loop 92 commit 14 frontend update'inde temizlenir.

---

## 3. Final 14-Commit Plan (architect plan + çelişki düzeltmeleri)

architect'ın `architecture-plan.md` 14-commit listesi backend-dev'in tek referansıdır. Üst 5 düzeltme uygulanır:

| Commit | Konu | Düzeltme |
|---|---|---|
| 1 | Domain TradeDirection | Mevcut `PositionSide` enum'u → `TradeDirection` enum rename (`Domain/ValueObjects/` → `Domain/Common/`). Position.Side → Position.Direction property rename. |
| 2 | Migration AddTradeDirectionToSignalAndPosition | Kolon adı `Side` → `Direction` rename (data backfill default 1 = Long). |
| 3 | IExchangeClient port | `IBinanceTrading`/`IBinanceMarketData` → `IExchangeClient` (8 method) + `IExchangeMarketData`. |
| 4 | BinanceFuturesClient REST | RestBaseUrl `https://demo-fapi.binance.com`. Endpoint paths `/fapi/v1/order`, `/fapi/v1/exchangeInfo`, `/fapi/v3/balance`, `/fapi/v3/positionRisk`, `/fapi/v1/leverage`, `/fapi/v1/marginType`, `/fapi/v1/positionSide/dual`. positionSide=BOTH zorunlu (One-way mode). workingType=MARK_PRICE stop emirlerinde. |
| 5 | FuturesWsSupervisor + UserDataStream | WsBaseUrl `wss://fstream.binancefuture.com`. listenKey lifecycle: POST/PUT 30dk bir, DELETE shutdown'da. Reconnect+replay+heartbeat (3m server ping, 10m timeout). |
| 6 | FuturesPaperFillSimulator | Taker %0.05 (binance-expert canlı). Margin akışı: WalletBalance × Leverage = pozisyon büyüklüğü; close'da margin geri + realizedPnl. Funding fee 8h cycle (basit: 0 ilk MVP, sonra eklenir). |
| 7 | VirtualBalance Futures genişlemesi | WalletBalance/AllocatedMargin/UnrealizedPnl ayrı field. ApplyFill silinir, AllocateMarginForPosition + ReturnMarginAndApplyPnl + ApplyFundingFee yeni davranışlar. |
| 8 | Migration RenameVirtualBalanceCurrentToWallet | CurrentBalance → WalletBalance rename + AllocatedMargin/UnrealizedPnl yeni kolonlar. PeakMarkPrice → ExtremeMarkPrice rename Position'da. |
| 9 | MarkToMarketWorker Direction-aware | SL/TP/BE/Trailing Long+Short asimetrik (binance-expert spec §10 liquidation formülü). Liquidation guard: marginRatio > %80 → CloseSignalPositionCommand. workingType=MARK_PRICE Futures için kritik. |
| 10 | 7 yeni Short detector + IPatternDetector.Direction | BearishEngulfing, ShootingStar, BollingerUpperReversal, BollingerSqueezeBreakDown, RsiOverboughtPullback, Ema9SlopeDown, DonchianBreakdown. Mevcut 10 detector'a Direction property: 7 long-bias = Long, 3 ortak (Volume/Spread/Adx) = Neutral. |
| 11 | WeightedScorePatternComposer Direction-aware | İki-kova logic (Long bucket + Short bucket). "Both qualified" → skip (yön belirsizliği). |
| 12 | PatternCompositeEvaluator MTF gate Direction | Long: 15m EMA21 slope > 0 (uptrend), Short: slope < 0 (downtrend). RSI cap Long: > 85 skip, Short: < 15 skip. |
| 13 | DI Composition Root + appsettings + Spot kod kalıntı temizle | Trading:Mode=Futures sabit. AllowMainnet=false default. Workers: FuturesKlineWorker, FuturesBookTickerWorker, FuturesUserDataStreamWorker, FuturesFundingRateWorker. `grep -r "api/v3/order" src/` → 0. `grep -r "BinanceSpot" src/` → 0. |
| 14 | RiskProfile Futures fields + reset migration + Frontend Direction badge + Tester Playwright | Leverage default 1x (max 3x). MaintenanceMarginRatio %80 alarm. MaxFundingFeePerHour 0.001. Frontend: Long yeşil, Short kırmızı badge. Playwright `loop92-pivot.spec.ts`. |

---

## 4. Backend-Dev Tek Brief (Loop 92 büyük delegasyon)

### Kapsam
- 14 atomik commit, sırayla, her biri development branch'a doğrudan push (CLAUDE.md §10 — PR yok)
- Her commit dotnet build warn-free + dotnet test full pass
- Domain → Migration → Application → Infrastructure → DI sırası

### Disiplinler (CLAUDE.md altın kurallar)
1. Result<T> (Ardalis) — exception-for-flow yasak (§5)
2. AsNoTracking() read path (workers, queries) (§4)
3. Async + CancellationToken her method
4. ILogger structured (string concat yasak)
5. agent-bus MCP append_decision her commit sonrası
6. Repository-per-entity yasak — DbContext + aggregate root yeterli
7. Lazy loading yasak — Include() veya CQRS read-model
8. Deprecated kod yasak — silinen Spot dosyaları "// removed" yorumu yok, tamamen kaldır

### Kritik kod örnekleri (binance-expert spec'ten)

**Order placement payload (Futures Long entry)**:
```
POST /fapi/v1/order
symbol=BTCUSDT&side=BUY&positionSide=BOTH&type=MARKET&quantity=0.001
&recvWindow=5000&timestamp=<ms>&signature=<hmac>
```

**Order placement (Futures Short entry)**:
```
POST /fapi/v1/order
symbol=BTCUSDT&side=SELL&positionSide=BOTH&type=MARKET&quantity=0.001
&recvWindow=5000&timestamp=<ms>&signature=<hmac>
```

**SL emirinde workingType=MARK_PRICE zorunlu** (last price manipülasyona açık):
```
POST /fapi/v1/order
symbol=BTCUSDT&side=SELL&positionSide=BOTH&type=STOP_MARKET
&stopPrice=104500.00&closePosition=true&workingType=MARK_PRICE&priceProtect=true
&recvWindow=5000&timestamp=<ms>&signature=<hmac>
```

**İlk açılışta (her sembol için 1 kez)**:
```
POST /fapi/v1/marginType  (marginType=ISOLATED)
POST /fapi/v1/leverage    (leverage=1)
GET  /fapi/v1/positionSide/dual  (dualSidePosition=false → One-way mode)
```

### Done-Definition (architect plan §5)
- 14 commit hepsi development branch'a push'lu
- dotnet test 0 fail (~120 yeni/değişen test)
- dotnet ef database update lokal MSSQL smoke geçer
- Bot boot 30dk: ≥3 emit, ≥1 Short emit, 0 unhandled exception
- Reviewer grep kontrolleri (architecture-plan §5 listesi)
- Tester Playwright `loop92-pivot.spec.ts` yeşil

### Bağımlılık ve süre tahmini
- Toplam: ~2-3 saat backend-dev (architect tahmini) — gerçekçi olarak 4-6 saat
- Reviewer: 30 dk
- Tester: 30 dk
- PM senaryo onayı: anlık

---

## 5. PM Sıradaki Akış

1. ✓ Sentez tamam (bu doküman)
2. Sentez doc + ADR + architecture-plan + spec-binance-expert-futures + code-audit + cash-ui-audit hep birlikte commit + push (development)
3. backend-dev'e tek "Loop 92 büyük implementasyon" delegasyonu
4. backend-dev tamam → reviewer + tester gate
5. Onaylar geldiğinde: bot start (Futures testnet) + DB reset + Loop 92 boot.md commit + ScheduleWakeup t30
6. Standart loop döngüsü başlar

---

## 6. Risk Hatırlatması

12 loop (-$17.04) sonrası bu radikal pivot — risk taşıyor:
- Futures testnet API key Spot'tan farklı (kullanıcı user-secrets güncel tutmalı)
- Funding rate ilk 8h içinde tetiklenmez (audit boş kalabilir)
- Liquidation engine first-time integration (test coverage kritik)
- Composer "both qualified skip" frekans düşürebilir (Loop 92 t60 ölçer, Loop 93'te tune)

**Halt eşiği**: Loop 92 boot sonrası t30/t60 wakeup'larda Realized PnL < -$1.50 → halt + Loop 93 spec.

---

## 7. Kaynak

- `loops/loop_92/spec-binance-expert-futures.md` (binance-expert)
- `docs/adr/0025-futures-short-pivot.md` (architect ADR)
- `loops/loop_92/architecture-plan.md` (architect plan)
- `loops/loop_92/code-audit.md` (Explore)
- `loops/loop_92/cash-ui-audit.md` (backend-dev mini audit)
- CLAUDE.md altın kurallar
- Memory: `feedback_no_dead_code.md`, `feedback_frekans_kartopu.md`, `trading_vision.md`
