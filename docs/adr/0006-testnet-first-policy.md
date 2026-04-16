# 0006. Testnet-First Politikasi

Date: 2026-04-16
Status: Accepted

## Context

Crypto trading bot u **ilk gun production a aciksa paraniz gider**. Akademik ve sektor kaynaklari (binance-research.md §4, §6) konsensus:

- Paper trade **30+ gun**; uretim market data + test endpoint + gercek olmayan para.
- Testnet **30+ gun**; uretimle ayni API semasi + yapay bakiye.
- Walk-forward OOS gecisi backtest overfitting tespiti sart.

Binance bize iki ayri endpoint ekosistemi sunar ([binance-research.md §3.5](../research/binance-research.md)):

- **Testnet**: `https://testnet.binance.vision/api`, `wss://stream.testnet.binance.vision`. Virtuel bakiye, aylik reset, sadece `/api/*` (sapi yok). Ayni rate limit + filter.
- **Mainnet**: `https://api.binance.com`, `wss://stream.binance.com:9443`. Gercek para.

Kullanici talimati acik: **"Testnet-first: prod API key boot-time reddedilir."**

## Decision

BinanceBot uretim ortaminda dahi **default olarak testnet** moddur; mainnet'e gecis ancak asagidaki **3 kapili toggle** ile mumkundur. Herhangi bir kapi kapali kalirsa **boot reddi**.

### 6.1 Endpoint Secimi (Config Bazli)

```json
{
  "Binance": {
    "UseTestnet": true,
    "RestBaseUrl": "https://testnet.binance.vision",
    "WsBaseUrl": "wss://stream.testnet.binance.vision"
  }
}
```

- `UseTestnet: true` iken RestBaseUrl **mutlaka** `testnet.binance.vision` icermek zorunda; aksi halde boot reddi (bkz. [0004 §4.4](./0004-secret-management.md)).
- `UseTestnet: false` (mainnet) secilmesi icin **3 kapi birden acik olmak zorunda** (sonraki paragraflar).

### 6.2 Mainnet Gecisi — 3 Kapi

**Kapi 1: Environment flag**
`ASPNETCORE_ENVIRONMENT=Production` olmak zorunda. `Development` / `Staging` da mainnet **kesinlikle yasak**.

**Kapi 2: Explicit feature flag**
`Binance:AllowMainnet = true` config anahtari aranir. Default `false`. Bu anahtar bir **admin karari**, `appsettings.Production.json` ta bilincli olarak `true` yapilir. Host env var ile de set edilebilir.

**Kapi 3: Paper-trade gecis kaydi**
`DbContext.SystemEvents` tablosunda `KIND = "PaperTradeCompleted"` kaydi olmali; bu kayit ancak:
- En az 30 gun testnet bakiye history si +
- En az 30 gun paper trade (uretim veri + test endpoint) +
- Walk-forward OOS rapor uretimi

tamamlandiktan sonra admin tarafindan `CompletePaperTradePhaseCommand` ile yazilir. Yoksa boot reddi.

### 6.3 Boot-Time Guard Kodu (Tasarim)

`StartupBinanceEnvironmentGuard` (Api layer inda):

1. `Binance.UseTestnet` oku.
2. Eger `false`:
   - `ASPNETCORE_ENVIRONMENT != "Production"` -> **exit 1** (Kapi 1).
   - `Binance.AllowMainnet != true` -> **exit 1** (Kapi 2).
   - `await db.SystemEvents.AnyAsync(e => e.Kind == "PaperTradeCompleted")` false -> **exit 1** (Kapi 3).
   - `RestBaseUrl` `api.binance.com` icermiyorsa -> **exit 1** (config mismatch).
   - `WsBaseUrl` `stream.binance.com` icermiyorsa -> **exit 1**.
3. Eger `true`:
   - `RestBaseUrl` `testnet.binance.vision` icermiyorsa -> **exit 1**.
   - `WsBaseUrl` `stream.testnet.binance.vision` icermiyorsa -> **exit 1**.
4. Basarili gecisler log a WARN seviyesinde yazilir ("BinanceBot running in <testnet|mainnet> mode").

### 6.4 UI / API Yansimasi

- Frontend uzerinde kalici **"TESTNET" / "MAINNET"** banner. Mainnet modda kirmizi + konfirmasyon dialog lari trade butonlarina eklenir.
- `GET /api/system/status` endpoint i `{ "mode": "testnet", "paperTradeCompleted": false, "allowMainnet": false }` doner — dashboard bunu render eder.

### 6.5 Trade Validation: `/order/test` Disiplini

Binance `POST /api/v3/order/test` endpoint i matching engine e gitmez ama tum filter validasyonunu (LOT_SIZE, PRICE_FILTER, MIN_NOTIONAL) yapar. `OrderPlacementService` uretim modda bile **once `/order/test` ile dry-run** yapar (binance-research.md §3.4). Testnet te gereksiz duplicate degil mi? Hayir — filter mismatch tespitinde bile degerli; rate limit maliyeti yeterince dusuk (weight 1).

## Consequences

### Pozitif

- "Yanlislikla mainnet e gectim" vakasi imkansiz — 3 ayri kapi, her biri explicit.
- Paper trade + testnet disiplini domain kaydiyla zorlanir; dokuman onerisi degil, kod kurali.
- UI banner ile her an hangi modda olundugu net.

### Negatif / Tradeoff

- Mainnet e gecis 3 ayri hareket ister; rapid iteration un aleyhine. Bilincli kabul — trading guvenligi ustun.
- `SystemEvents` tablosuna `PaperTradeCompleted` kaydi sonradan silinirse guard tetiklenir. Yine de kabul: admin yanlislikla uymak yerine bilincli gecmeli.

### Notr

- Binance testnet key ve mainnet key ayri setlerdedir; karistirmak isteyen bile 401 alir. Ama base URL guard i yine de config mismatch i yakalar.

## Alternatifler

1. **Sadece env flag ile gecis** — Kolay ama yanlislikla set edilme riski yuksek. Reddedildi.
2. **Kullanicinin UI dan onay gunluk** — UI bug i ile bypass riski; boot-time guard bastada tutar. Ek katman olarak eklenebilir ama yeter sart degil.
3. **Mainnet i butunuyle `#if` preprocessor bayragiyla compile-out** — Esnek degil, staging senaryosunu bozar. Reddedildi.
4. **Paper trade suresini 7 gune dusur** — Akademik konsensus (30+) ile celisir. Reddedildi.

## Kaynak

- [docs/research/binance-research.md §3.5 Testnet](../research/binance-research.md)
- [docs/research/binance-research.md §6 Red Flag](../research/binance-research.md)
- [docs/research/binance-research.md §7 paper trade disiplini](../research/binance-research.md)
- [testnet.binance.vision](https://testnet.binance.vision/)
- [binance-spot-api-docs — rest-api.md (/order/test)](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md)
- [docs/adr/0004-secret-management.md](./0004-secret-management.md)
- CLAUDE.md "Testnet-first" direktif
