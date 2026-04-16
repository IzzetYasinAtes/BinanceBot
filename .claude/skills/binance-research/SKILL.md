---
name: binance-research
description: Binance resmi dokümantasyonundan canlı bilgi çeker. Önce memory MCP'de cache kontrol eder, yoksa WebFetch ile binance-docs.github.io veya github.com/binance/binance-spot-api-docs okur, Türkçe özet + kaynak URL döner. binance-expert agent'ının birincil araştırma aracı.
---

# binance-research

Her Binance sorusunun başlangıç noktası. Cache → WebFetch → özet + kaynak.

## Sıra

1. **Cache kontrol:** MCP `memory` server'ında `binance:<topic>` key'i var mı?
2. Varsa: son update >7 gün geçmişse tazele, aksi halde cache'ten dön.
3. Yoksa:
   - İlgili endpoint/topic için URL belirle (aşağıdaki tabloya bak).
   - `WebFetch(url, prompt="[topic]-özetle: endpoint, params, limits, error codes, örnek payload")`.
   - Çıktıyı Türkçe'ye özetle (5-10 cümle).
   - `memory` MCP'ye yaz: key=`binance:<topic>`, value=özet+url+timestamp.
4. Cevabı dön: **Türkçe özet + `Kaynak: <URL>`**.
5. MCP `agent-bus.append_decision` — "binance-research: <topic> → özetlendi".

## URL Haritası (hızlı referans)

| Topic | URL |
|---|---|
| Genel giriş / filtreler | https://binance-docs.github.io/apidocs/spot/en/#general-information |
| REST rate limit | https://binance-docs.github.io/apidocs/spot/en/#limits |
| Spot market data endpoints | https://binance-docs.github.io/apidocs/spot/en/#market-data-endpoints |
| WS market streams | https://binance-docs.github.io/apidocs/spot/en/#websocket-market-streams |
| WS user data stream | https://binance-docs.github.io/apidocs/spot/en/#user-data-streams |
| Order endpoints + types | https://binance-docs.github.io/apidocs/spot/en/#spot-account-trade |
| Error codes | https://binance-docs.github.io/apidocs/spot/en/#error-codes |
| Filters (LOT_SIZE, MIN_NOTIONAL, ...) | https://binance-docs.github.io/apidocs/spot/en/#filters |

Test net: `https://testnet.binance.vision/` — testnet doc.

## Kural

- Spekülatif cevap yasak. Cache veya fetch dışında başka yerden bilgi verme.
- Cache TTL 7 gün. Daha eski → WebFetch.
- Kaynak URL her zaman cevabın altında.
- Hata durumunda (network, 404) kullanıcıya dürüst söyle, tahmin yürütme.

## Kaynak

- https://binance-docs.github.io/apidocs/spot/en/
- https://github.com/binance/binance-spot-api-docs
