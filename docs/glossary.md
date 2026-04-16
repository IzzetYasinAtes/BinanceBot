# Glossary — BinanceBot Ubiquitous Language

DDD'nin "ubiquitous language" kuralı: kod, dokümantasyon, konuşma dili **aynı terimleri** kullanır. Yeni terim eklenince buraya.

İki dilli: TR karar dili, EN kod tarafı.

## Market Data

| TR | EN (kod) | Tanım |
|---|---|---|
| Mum (Kline) | `Kline` | OHLCV + openTime/closeTime; tek bir interval'ın özet barı. |
| Sembol | `Symbol` | İşlem çifti (örn. `BTCUSDT`). |
| Aralık | `Interval` | Kline periyodu (`1m`, `5m`, `1h`, `1d`). |
| İşlem | `Trade` | Tek alış-satış; executed quantity + price. |
| Derinlik (Orderbook) | `Depth`, `OrderBook` | Buy/sell seviyeleri. |
| En iyi alış-satış | `BookTicker` | Best bid + best ask snapshot. |
| Stream | `Stream` | Binance WS kanal adı (`btcusdt@kline_1m`). |
| Birleşik stream | `CombinedStream` | Çok kanalı tek connection'da (`/stream?streams=a/b/c`). |

## Order

| TR | EN | Tanım |
|---|---|---|
| Emir | `Order` | Kullanıcının Binance'e gönderdiği trade isteği. |
| Piyasa emri | `MARKET` | Anlık fiyatta al/sat. |
| Limit emri | `LIMIT` | Belirli fiyat; maker veya taker. |
| Zarar durdur | `STOP_LOSS`, `STOP_LOSS_LIMIT` | Fiyat X'e ulaşınca tetikle. |
| Kar al | `TAKE_PROFIT`, `TAKE_PROFIT_LIMIT` | Fiyat X'e ulaşınca kar realize. |
| Sadece maker | `LIMIT_MAKER` | Maker olamazsa reddet. |
| OCO | `OCO` | One-Cancels-Other; LIMIT + STOP birlikte. |
| Emir geçerliliği | `timeInForce` | GTC / IOC / FOK. |

## Risk / Strateji

| TR | EN | Tanım |
|---|---|---|
| Pozisyon büyüklüğü | `PositionSize` | Tek trade'deki risk. |
| Slipaj | `Slippage` | Beklenen ile gerçek fill fiyatı farkı. |
| Spread | `Spread` | Bid-ask farkı. |
| Likidite | `Liquidity` | Piyasa derinliği. |
| Ücret | `Fee`, `Commission` | Taker/maker komisyonu. |
| Max düşüş | `MaxDrawdown` | Tarihsel en kötü zarar. |

## Mimari

| TR | EN | Tanım |
|---|---|---|
| Aggregate | `Aggregate`, `AggregateRoot` | Transaction sınırı; tek root entity. |
| Entity | `Entity` | Kimliği önemli (Id ile eşit). |
| Değer nesnesi | `ValueObject` | Immutable; tüm field'lar eşitse eşit. |
| Domain olayı | `DomainEvent` | Aggregate değişiminin iş anlamı; geçmiş zamanda. |
| Komut | `Command` | State değiştirir. |
| Sorgu | `Query` | State değiştirmez. |
| Handler | `Handler` | Command/Query'yi işleyen sınıf (MediatR). |
| Doğrulayıcı | `Validator` | FluentValidation kuralları. |

## Infrastructure

| TR | EN | Tanım |
|---|---|---|
| Kanal | `Channel<T>` | Producer/consumer FIFO. |
| Gözetmen | `Supervisor` | BackgroundService reconnect yöneticisi (WS). |
| Dayanıklılık | `Resilience` | Retry + circuit breaker + timeout (Polly). |
| İsimli client | `NamedHttpClient` | `IHttpClientFactory` ile adlandırılmış HttpClient. |

## AI Workspace

| TR | EN | Tanım |
|---|---|---|
| Agent | `agent` | `.claude/agents/<name>.md` — uzman. |
| Beceri | `skill` | `.claude/skills/<name>/SKILL.md` — yeniden kullanılır talimat. |
| Devir | `handoff` | PM'den subagent'a görev geçişi. |
| Karar logu | `decision log` | `.ai-trace/decisions.jsonl`. |
| Parça | `chunk` | PM'in ≤5 adımlık görev birimi. |
| Kontrol noktası | `checkpoint` | Chunk sonu özet + user onay. |
| Kullanıcı notu | `user note` | `/tell-pm` ile yazılan mid-task mesaj. |

## Nasıl Katkı Verilir

Yeni terim doğarsa:
1. Önce burada tanımla (TR + EN + açıklama).
2. Kod identifier'ını tabloya koy.
3. Dokümantasyonda/skill'de aynı terimi kullan.
4. Çelişki varsa architect ADR açar (`architect-ddd-review` ile denetim).
