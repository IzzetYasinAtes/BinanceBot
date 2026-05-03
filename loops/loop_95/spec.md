# Loop 95 Spec — Parametrik Tune (Long-only emit + R:R + Frekans)

Tarih: 2026-05-03 | Author: PM | Status: Backend-dev pickup ready

## Bağlam

Loop 94 mekanik fix'leri (peak/Wallet/AllocateMargin/MaxOpen) doğrulandı. AMA Loop 94 t120'de 3 stratejik sorun:
1. **Short bias toxic** — 2/2 Short pos SL hit (-$1.247 toplam loss), pazar uptrend
2. **R:R 1:14** — winning trade trailing ile çok erken çıkıyor (+$0.045 avg vs -$0.624 avg loss)
3. **Frekans dondu** — son 60dk 0 emit

Detay: `loops/loop_94/halt-t120.md`

## Tune (Backend-dev, ~30dk iş)

### Tune #1: Short Detector Ağırlıkları → 0 (Long-only emit)

**Kaynak (Explore audit)**:
- `D:/repos/BinanceBot/src/Infrastructure/Strategies/Patterns/WeightedScorePatternComposer.cs:219-241` ResolveWeight switch
- 7 Short detector default weight: BearishEngulfing=2, ShootingStar=2, BollingerUpperReversal=1.5, BollingerSqueezeBreakDown=1.5, RsiOverboughtPullback=1.5, Ema9SlopeDown=1, DonchianBreakdown=1.5
- Override mekanizması: `PatternComposerOptions.WeightOverrides` (ParametersJson dict)

**Yöntem**: 5 strateji (BTC/ETH/XRP/SOL/ADA) için DB Strategies.ParametersJson `WeightOverrides` field ekle:
```json
{
  "RequiredScore": 5,
  "WeightOverrides": {
    "BearishEngulfing": 0.0,
    "ShootingStar": 0.0,
    "BollingerUpperReversal": 0.0,
    "BollingerSqueezeBreakDown": 0.0,
    "RsiOverboughtPullback": 0.0,
    "Ema9SlopeDown": 0.0,
    "DonchianBreakdown": 0.0
  }
}
```

PowerShell SQL UPDATE veya backend-dev migration. Strategies tablosunda 5 row (Id 901, 902, 903, 904, 905 muhtemelen).

### Tune #2: TrailPct 0.005 → 0.003 (winning trade'e pencere ver)

**Kaynak**: `D:/repos/BinanceBot/src/Api/appsettings.json:61`
```json
"TrailPct": 0.0050
```
Değiştir:
```json
"TrailPct": 0.0030
```

**Etki**: BE armed sonrası trailing exit threshold mark'tan %0.5 → %0.3'e gevşer. Winning trade daha fazla run alır.

### Tune #3: TriggerPct 0.002 → 0.003 (BE eşiği geç arm)

**Kaynak**: `D:/repos/BinanceBot/src/Api/appsettings.json:56`
```json
"TriggerPct": 0.0020
```
Değiştir:
```json
"TriggerPct": 0.0030
```

**Etki**: BE move +%0.20 → +%0.30 eşiği geç arm. Daha büyük profit pencere açıldıktan sonra BE/trailing aktif olur.

### Tune #4: MTF Threshold Gevşet (frekans donma)

**Kaynak**: `D:/repos/BinanceBot/src/Infrastructure/Strategies/Evaluators/PatternCompositeEvaluator.cs:118`
```csharp
var mtfThreshold = snapshot.Ema21_15m * 0.001m;  // ±%0.1
```
Değiştir:
```csharp
var mtfThreshold = snapshot.Ema21_15m * 0.0005m;  // ±%0.05
```

**Etki**: 15m EMA21 slope skip eşiği gevşer. Pazar yatay zamanlarda Long emit kapanmaz (frekans durmasının olası nedeni).

## Done-Definition

- 1-2 atomik commit (kod + DB update)
- dotnet build 0 hata 0 uyarı
- dotnet test 0 fail
- DB update: 5 Strategies.ParametersJson satırı WeightOverrides eklenmiş
- Commit + push development

## Disiplin

CLAUDE.md altın kurallar geçerli (Result<T>, async+ct, structured logging, deprecated yok).

## Sonraki

Backend-dev tune → PM bot restart + DB reset + Loop 95 boot.md + ScheduleWakeup t30.

Hipotez: Tune'larla birlikte:
- Long-only emit → Short systematic loss yok
- TrailPct geniş → winning trade daha çok run
- TriggerPct geç → BE armada pos kapanma yok küçük profit
- MTF gevşek → frekans durmaz

Beklenti: Loop 94'tekilerden farklı pattern: avg win büyür, frekans 30+/h'a yükselir, net realized pozitife döner (veya en azından eşiğin altına gerek yok).
