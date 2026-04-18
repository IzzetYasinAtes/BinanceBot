# Loop 1 Özeti

## Süre
- Başlangıç: 2026-04-17 20:13 UTC → Bitiş: 2026-04-17 ~21:00 UTC
- **4h evolution cycle iptal edildi** — kullanıcı erken müdahale: kod değişikliği gerekti.

## Sonuç
- Loop 1 ölçüm baseline olarak kullanılmadı.
- Tetikleyici: kullanıcı "geçmiş kripto verisi az değil mi" sorusu → tespit: KlineIngestionWorker yalnızca WS persist ediyor, REST backfill yok. Stratejiler her DB drop sonrası 20-30dk warmup yaşıyor → 4h ölçümün ilk %10-15'i ölü.
- Kullanıcı kararı: **Seçenek #1 (REST backfill on boot)**.

## Strateji Performansı
| Strateji | Sinyal | Fill | PnL | WR | Karar |
|---|---|---|---|---|---|
| BTC-Trend-Fast | n/a | n/a | n/a | n/a | iptal — baseline değil |
| BNB-MeanRev | n/a | n/a | n/a | n/a | iptal |
| XRP-Grid | n/a | n/a | n/a | n/a | iptal |

## Karar: MUTATE (kod değişikliği)

## Agent Zinciri
- **binance-expert** ✅ → loop_1/research.md (limit=1000 OK, weight=2/req, WS-önce + REST-sonra, 1m yeterli, idempotent upsert hazır)
- **architect** ✅ → loop_1/decision.md + docs/adr/0009-rest-kline-backfill-on-boot.md (KlineBackfillWorker IHostedService, IWsReadinessProbe, KlinePersister DRY extract, BinanceOptions 3 yeni alan)
- **backend-dev** (round 1) ✅ → 4 yeni dosya + 5 değişen, build 0/0, test 46/46
- **reviewer** (round 1) → NEEDS_CHANGES (5 minor)
- **backend-dev** (round 2) ✅ → 4/5 düzeltildi, smoke test PM onayıyla atlandı, build 0/0, test 46/46
- **reviewer** (round 2) ✅ → **READY**
- **PM** → ADR-0009 satır 28 "100ms→200ms" tek satır quick fix.

## Bir Sonraki Loop İçin Değişiklik
- Yeni `KlineBackfillWorker`: boot'ta her sembol için 1m × 1000 mum REST'ten çekilir.
- `IWsReadinessProbe` ile WS soketi hazır olduktan sonra backfill başlar (gap yok).
- `KlinePersister` DRY upsert: WS ve REST aynı persist mantığını paylaşır.
- BinanceOptions: `BackfillEnabled=true`, `BackfillLimit=1000`, `BackfillIntervals=["1m"]`.
- **Beklenti:** Loop 2 başlangıcında sıfır warmup; tüm 4h ölçüm değerli.

## Değişen Dosyalar (commit içeriği)
- src/Application/Abstractions/Binance/IWsReadinessProbe.cs (yeni)
- src/Application/Abstractions/Binance/IKlinePersister.cs (yeni)
- src/Infrastructure/Binance/Workers/KlinePersister.cs (yeni)
- src/Infrastructure/Binance/Workers/KlineBackfillWorker.cs (yeni)
- src/Infrastructure/Binance/Workers/KlineIngestionWorker.cs (refactor → KlinePersister)
- src/Infrastructure/Binance/Streams/BinanceWsSupervisor.cs (IWsReadinessProbe impl)
- src/Infrastructure/Binance/BinanceOptions.cs (3 yeni alan)
- src/Infrastructure/DependencyInjection.cs (DI sırası)
- src/Infrastructure/BinanceBot.Infrastructure.csproj (InternalsVisibleTo)
- src/Api/appsettings.json (Binance section 3 alan)
- tests/Tests/Infrastructure/Binance/KlineBackfillWorkerTests.cs (yeni 4 senaryo)
- docs/adr/0009-rest-kline-backfill-on-boot.md (yeni ADR)
- loop_1/{research.md, decision.md, review.md, summary.md} (loop dokümanları)
