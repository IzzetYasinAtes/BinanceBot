# Frontend Design — BinanceBot

**Durum:** One-shot master plan adim 4/6 (frontend-dev) + adim 6 final sentez patch'leri. `docs/plan.md` frontend bolumleri icin ham materyal.
**Kapsam:** Vue 3 CDN + importmap tabanli SFC-suz SPA tasarimi, 8 sayfanin envanteri, fetch wrapper sozlesmesi, page-scoped reactive state pattern, polling stratejisi, CSS/UX ve Clean Architecture backend kontrat eslemesi.
**Referanslar:** [docs/architecture-notes.md](./architecture-notes.md), [docs/research/binance-research.md](./research/binance-research.md), [docs/adr/0002-binance-ws-supervisor-pattern.md](./adr/0002-binance-ws-supervisor-pattern.md), [docs/adr/0006-testnet-first-policy.md](./adr/0006-testnet-first-policy.md), [docs/adr/0007-admin-auth-model.md](./adr/0007-admin-auth-model.md).

**Yasaklar (CLAUDE.md ile tutarli):** npm / bundler / SFC / `<script setup>` / Pinia. Build-time TypeScript yok. Vue ve tum kutuphaneler CDN + importmap ile pin'lenir.

**SignalR (A1 cozumu):** `NOT_IN_SCOPE` — MVP'de HTTP polling yeterli; push gereksinimi ortaya cikarsa Faz-2 ADR (SignalR veya SSE) ile ele alinir. CLAUDE.md yasaklar listesine eklenmedi (reviewer onayli).

---

## 1. Teknoloji Stack Kararlari

| Kutuphane | Versiyon | Rol | Gerekce |
|---|---|---|---|
| **Vue** | `3.4.38` (`vue.esm-browser.prod.js`) | Reaktif runtime | 3.4+ stabil; `prod` build size kucuk, tree-shake gerek yok (CDN tek dosya). Template string + `createApp` + `setup()` — SFC olmadan cozulur. |
| **lightweight-charts** | `4.1.3` (TradingView) | Candlestick + OHLCV cizim | Chart.js ile karsilastirma 1.3'te. Trading UX standardi. |
| **date-fns** | `3.6.0` (+esm) | UTC / local tarih format | Binance tum timestamp'leri `ms` (UTC). `format`, `parseISO`, `differenceInSeconds`. Tree-shake gerekmiyor, ES module +esm yeterli. Luxon alternatifi daha agir, date-fns mimari daha saf fonksiyonel. |
| **vanilla CSS** | — | `src/Frontend/css/style.css` tek dosya | Tailwind CDN runtime JIT JS parse'i ~40KB + production'da `cdn.tailwindcss.com` uyarisini ekler. Tek renk tokeni + ~50 utility class vanilla + CSS variables ile yeterli. Trade UI dark tema. |
| **vue-router** | **NOT_IN_SCOPE** | — | Her sayfa ayri HTML dosyasi (MPA). Tek `index.html` + SPA yaklasimi, page state'i reset edemedigi icin basitlik adina MPA secildi. Navigasyon normal `<a href="klines.html">`. Tasinacak shared state (testnet banner, symbol secici) `localStorage` + `js/store.js` ile koprulenir. |
| **Pinia / Vuex** | **YASAK** (CLAUDE.md) | — | `reactive()` / `ref()` + `js/store.js` cross-page minimal pub-sub. Gerekcesi: tek kullanici, karmasik global state yok. |

**Karar gerekcesi Chart.js vs lightweight-charts:**

| Kriter | Chart.js 4 | lightweight-charts 4 |
|---|---|---|
| Candlestick native | Hayir (chartjs-chart-financial eklenti) | Evet |
| Gzipped size (CDN) | ~85 KB | ~44 KB |
| OHLCV interaction (crosshair, zoom, pan) | Ekstra plugin | Built-in |
| Canvas vs WebGL | Canvas | Canvas tabanli, ticker guncelleme 60fps |
| Learning curve | Dusuk | Orta |
| Trading UI standardi | Hayir | Evet (TradingView runtime) |
| **Sonuc** | Reddedildi | **Secildi** |

**Karar gerekcesi Tailwind CDN vs vanilla CSS:** Tailwind runtime JIT (`cdn.tailwindcss.com`) production'da Tailwind takiminin kendisi tarafindan "not for production" uyarisi alir. MPA + 8 sayfa icin ~200 class kafi; tema degistirme kolay; vanilla secildi.

---

## 2. Sayfa Envanteri

Tum 8 sayfa MVP kapsaminda hazir. Hepsinin `<body>` ustunde ortak bir **TestnetBanner** componenti vardir (bkz. §6.2). Sayfa detay sozlesmeleri:

### 2.1 Dashboard (`/index.html`)

- **Amac:** Genel operasyonel saglik + ozet piyasa + aktif pozisyonlar + gunluk PnL.
- **Gosterilen data:** 3 sembol (BTCUSDT, ETHUSDT, BNBUSDT) icin `{ lastPrice, priceChange24h, priceChangePct24h, volume24h }`; acik pozisyon sayisi + toplam unrealized PnL; gun icinde kapanan pozisyon realized PnL toplami; system health (`api: ok/degraded/down`, `wsLastHeartbeatSec`, `circuitBreakerStatus`).
- **State shape:**
  ```js
  reactive({
    summary: { BTCUSDT: null, ETHUSDT: null, BNBUSDT: null },
    positions: { open: [], count: 0, unrealizedPnl: 0 },
    pnl: { realizedToday: 0, unrealizedTotal: 0 },
    health: { api: 'unknown', wsAgeSec: null, circuitBreaker: 'unknown' },
    loading: true, error: null
  })
  ```
- **API cagrilari:** `GET /api/market/summary?symbols=BTCUSDT,ETHUSDT,BNBUSDT`, `GET /api/positions?status=open`, `GET /api/positions/pnl/today`, `GET /api/health/ready`, `GET /api/risk/circuit-breaker`.
- **Real-time:** Polling 2s (visibility paused).
- **Kullanici aksiyonu:** Her sembol card'ina click -> `klines.html?symbol=...` navigate.

### 2.2 Kline'lar (`/klines.html`)

- **Amac:** Candlestick + indikator overlay ile sembol bar analizi.
- **Gosterilen data:** 500 bar (default `1m`), candlestick seri, toggle'li RSI / Bollinger Band overlay, son bar detay table'i (timestamp, O/H/L/C, volume, closed flag).
- **State shape:**
  ```js
  reactive({
    symbol: 'BTCUSDT',
    interval: '1m',
    klines: [],
    indicators: { rsi: false, bollinger: false },
    chartInstance: null, // lightweight-charts ref
    loading: true, error: null
  })
  ```
- **API cagrilari:** `GET /api/klines?symbol=BTCUSDT&interval=1m&count=500`.
- **Real-time:** Polling 2s -> son bar `closed==false` ise `updateOngoing`, `closed==true` geldiyse prepend new bar + cap at 500.
- **Kullanici aksiyonlari:** Symbol switcher (3 option), interval switcher (`1m/5m/15m/1h/4h/1d`), indicator toggle checkbox'lari, zoom/pan (lightweight-charts built-in).

### 2.3 OrderBook (`/orderbook.html`)

- **Amac:** Depth derinligi + imbalance gostergesi.
- **Gosterilen data:** Best bid / ask (highlight), top 20 level her iki yan (price, qty, cumulative), spread (bps), 20-level toplam bid/ask likidite, imbalance `(bidSum-askSum)/(bidSum+askSum)`.
- **State shape:**
  ```js
  reactive({
    symbol: 'BTCUSDT',
    depth: { bids: [], asks: [], lastUpdateId: null, snapshotFetchedAt: null },
    spread: { abs: 0, bps: 0 },
    liquidity: { bidSum: 0, askSum: 0, imbalance: 0 },
    loading: true, error: null
  })
  ```
- **API cagrilari:** `GET /api/depth?symbol=BTCUSDT&depth=20`.
- **Real-time:** Polling 1s (hizli degisim; backend cache + `@depth@100ms` WS stream'inden read-model icin maliyet asgari).
- **Kullanici aksiyonu:** Symbol switcher. Level row'larina hover -> cumulative highlight.

### 2.4 Positions (`/positions.html`)

- **Amac:** Acik pozisyonlar + kapatma + gecmis.
- **Gosterilen data:**
  - Acik tablo: `symbol | qty | avgEntry | markPrice | unrealizedPnl | unrealizedPct | durationMin | strategyName | actions`.
  - Kapanmis tablo (filter: son 7 gun default): `symbol | openedAt | closedAt | qty | entry | exit | realizedPnl | reason`.
- **State shape:**
  ```js
  reactive({
    open: [], closed: [],
    filter: { from: null, to: null, symbol: null },
    loading: true, error: null, closing: {} // symbol -> bool
  })
  ```
- **API cagrilari:** `GET /api/positions?status=open`, `GET /api/positions?status=closed&from=&to=&symbol=` (tek `ListPositionsQuery` slice — F3), `POST /api/positions/{symbol}/close` (body: `{ reason }`).
- **Real-time:** Polling 2s (open); closed 30s.
- **Kullanici aksiyonu:** "Kapat" butonu -> confirm dialog -> `ClosePositionCommand`. Filtre degisince list refetch.

### 2.5 Orders (`/orders.html`)

- **Amac:** Aktif emirler + cancel + filtreli gecmis.
- **Gosterilen data:**
  - Aktif: `clientOrderId | exchangeOrderId | symbol | side | type | qty | filledQty | price | stopPrice | status | placedAt | actions`.
  - Gecmis (paged): ayni sutunlar + `averageFillPrice`, `updatedAt`, `reason`.
- **State shape:**
  ```js
  reactive({
    active: [], history: { items: [], total: 0, skip: 0, take: 50 },
    filter: { symbol: null, status: null, from: null, to: null },
    loading: true, error: null, canceling: {} // clientOrderId -> bool
  })
  ```
- **API cagrilari:** `GET /api/orders/open?symbol=`, `GET /api/orders/history?symbol=&status=&from=&to=&skip=&take=`, `DELETE /api/orders/{clientOrderId}`.
- **Real-time:** Polling 3s (active), history 30s / manuel refresh.
- **Kullanici aksiyonu:** "Iptal" butonu -> `CancelOrderCommand`. Filter formu (date-range picker, symbol dropdown, status dropdown).

### 2.6 Strategy Config (`/strategies.html`)

- **Amac:** Aktif/pasif strateji goruntuleme + parametre inceleme + signal feed. (MVP read-only — admin UI YOK, bkz. ADR-0007.)
- **Gosterilen data:**
  - Liste: `id | name | type | status | symbols | activatedAt | lastSignalAt` (actions sutunu MVP'de bos/devre disi).
  - Detay paneli: secili strateji icin parametre goruntulemesi (type'a gore `LevelCount/SpreadPct` grid, `FastMA/SlowMA/AtrPeriod/StopMultiplier` trend, `RsiPeriod/BbPeriod/BbStdDev` mean-reversion), son 50 emit'li signal.
- **State shape:**
  ```js
  reactive({
    strategies: [], selected: null,
    paramsForm: { /* read-only MVP */ },
    signals: [],
    loading: true, error: null
  })
  ```
- **API cagrilari:** `GET /api/strategies`, `GET /api/strategies/{id}` (F1 — `GetStrategyDetailQuery` ile detay), `GET /api/strategies/{id}/signals?from=&to=`.
- **Admin aksiyonlar (Activate/Deactivate/UpdateParameters) FRONTEND'DE YOK.** Bkz. ADR-0007: `POST /api/strategies/{id}/activate`, `POST /api/strategies/{id}/deactivate`, `PUT /api/strategies/{id}/parameters` **sadece** Swagger "Authorize" + `X-Admin-Key` header veya `tests/manual/*.http` test dosyasi uzerinden cagrilir.
- **Real-time:** Polling 10s (yavas degisim).

### 2.7 Risk Profile (`/risk.html`)

- **Amac:** Risk limit goruntuleme + circuit breaker durumu. (MVP read-only — admin aksiyonlar yok.)
- **Gosterilen data:**
  - Form: `RiskPerTradePct`, `MaxPositionSizePct`, `MaxGrossExposurePct`, `MaxDrawdown24hPct`, `MaxDrawdownAllTimePct`, `MaxConsecutiveLosses` (readonly number display, cap uyarili).
  - Circuit breaker panel: `status { Healthy | Warning | Tripped | Cooldown }`, `trippedAt`, `consecutiveLossCount`. Reset butonu MVP'de YOK (admin aksiyon — ADR-0007).
  - Son 30 gun drawdown sparkline (vanilla SVG).
- **State shape:**
  ```js
  reactive({
    profile: null,
    circuitBreaker: null,
    history: [], // drawdown
    loading: true, error: null
  })
  ```
- **API cagrilari:** `GET /api/risk/profile`, `GET /api/risk/circuit-breaker`, `GET /api/risk/drawdown-history?days=30` (F1).
- **Admin aksiyonlar frontend'de YOK.** Bkz. ADR-0007: `PUT /api/risk/profile` (UpdateRiskProfileCommand), `POST /api/risk/override-caps` (OverrideRiskCapsCommand — body `{ riskPerTradeCap, maxPositionCap, adminNote }` — F5), `POST /api/risk/circuit-breaker/reset` sadece Swagger/.http uzerinden.
- **Real-time:** Polling 10s (slow).

### 2.8 Logs (`/logs.html`)

- **Amac:** Canli SystemEvents tail (backend audit tablosu).
- **Gosterilen data:** Scroll-following log list (`timestamp | level | source | type | message`), level filter (`Info/Warning/Error`).
- **State shape:**
  ```js
  reactive({
    lines: [], // cap 500, ring buffer
    filter: { level: 'Info' },
    follow: true, // auto-scroll
    since: null, // last cursor
    loading: true, error: null
  })
  ```
- **API cagrilari:** `GET /api/logs/tail?since=<iso>&level=&limit=200` (F1 — `TailSystemEventsQuery`; SystemEvents tablosu tek kaynak, Serilog dosya tail YASAK).
- **Real-time:** Polling 2s; response `nextSince` cursor ile delta pull.
- **Kullanici aksiyonu:** Filter dropdown, follow toggle, clear button, search box (client-side grep).

---

## 3. importmap Dependency Pin

Tum sayfalar ayni importmap'i paylasir. Dosya konumu her HTML'in `<head>` bolumunde. Minimum set:

```html
<script type="importmap">
{
  "imports": {
    "vue": "https://cdn.jsdelivr.net/npm/vue@3.4.38/dist/vue.esm-browser.prod.js",
    "lightweight-charts": "https://unpkg.com/lightweight-charts@4.1.3/dist/lightweight-charts.standalone.production.mjs",
    "date-fns": "https://cdn.jsdelivr.net/npm/date-fns@3.6.0/+esm",
    "date-fns/format": "https://cdn.jsdelivr.net/npm/date-fns@3.6.0/format/+esm",
    "date-fns/parseISO": "https://cdn.jsdelivr.net/npm/date-fns@3.6.0/parseISO/+esm",
    "date-fns/differenceInSeconds": "https://cdn.jsdelivr.net/npm/date-fns@3.6.0/differenceInSeconds/+esm",
    "@app/api": "/js/api.js",
    "@app/store": "/js/store.js",
    "@app/ui": "/js/ui.js"
  }
}
</script>
```

**Notlar:**

- `@app/*` kendi ES module'lerimiz; her sayfanin `<script type="module" src="/pages/<page>.js">` entry'si `import { ... } from '@app/api'` yapar. Boylece HTML ve JS arasinda temiz bir import arayuzu olur.
- Vue `vue.esm-browser.prod.js` runtime + compiler **ICERIR**. Runtime-only template string kullandigimiz icin YETMEZ — template derleyici lazim.
- Lightweight-charts `.standalone.production.mjs` — CDN genelde dogru Content-Type doner; sorun cikarsa `.standalone.production.js` fallback.
- `+esm` jsDelivr konvansiyonu; her submodule'a ayri map gerekir.

**SRI (Subresource Integrity) onerisi:** importmap-script tagleri icin SRI desteklenmiyor (spec); ancak `<link rel="modulepreload" href="..." integrity="sha384-...">` ile kritik modul (vue) preload + hash dogrulamasi yapilabilir. MVP'de versiyon pin yeterli; CVE cikarsa manuel bump. Supply-chain ADR sonraki faz.

**Offline/Kiosk senaryosu:** Gerekirse bu 3 paket `src/Frontend/vendor/` altina commit + importmap'te `/vendor/...` gosterilir. MVP'de CDN.

---

## 4. Real-time Update Stratejisi (KARAR)

### 4.1 Alternatif Matrisi

| Alternatif | Artilar | Eksiler | MVP? |
|---|---|---|---|
| **HTTP polling** | Backend tarafinda ek infra YOK; mevcut cercevede henuz SignalR kurulumu yok. Debug ve cache kolay. Backend already has read-models. | Network traffic fazla; refresh gorunumu. | **Evet — MVP** |
| SignalR (backend push) | Latency dusuk, verimli. | `Microsoft.AspNetCore.SignalR` package + hub + yeniden abonelik disiplini. Bundle size +. | **NOT_IN_SCOPE** — push gereksinimi dogarsa Faz-2 ADR. |
| WS proxy (backend Binance WS'ini frontend'e relay) | En gercek zamanli. | Backend kendi WS server'i yazmali, auth karmasasi, CORS. Fazladan kompleksite, MVP'de gereksiz. | Reddedildi. |
| Server-Sent Events | Tek yonlu push, HTTP/1.1 uzerinde basit. | Tarayici sinirlari (Edge 6 connection), reconnect dusuk. | Alternatif (SignalR'e gore zayif). |

**Karar:** **MVP = HTTP polling**. Backend tarafinda zaten read-model guncellemesi Depth/BookTicker/Kline WS supervisor icinde olur (bkz. [ADR 0002](./adr/0002-binance-ws-supervisor-pattern.md)), frontend bu read-model'i okur. SignalR **NOT_IN_SCOPE** (A1 cozumu — CLAUDE.md yasaklar listesinde degil; push gereksinimi dogarsa ayri ADR).

### 4.2 Polling Interval Haritasi

| Sayfa | Interval | Gerekce |
|---|---|---|
| Dashboard | **2s** | Ana izleme; summary + health combine. |
| Kline'lar | **2s** | Son bar update + candlestick refresh. 1m bar'da cok hizli degisim yok. |
| OrderBook | **1s** | Depth en hizli degisen data; backend read-model zaten 100ms WS ile tazelenir. |
| Positions (open) | **2s** | Mark-to-market PnL. |
| Positions (closed) | **30s** | Yavas degisir (pozisyon kapandiktan sonra immutable). |
| Orders (active) | **3s** | Cancel/fill gelene kadar relevant. |
| Orders (history) | **30s** / manuel | Yavas degisim. |
| Strategy Config | **10s** | Yavas degisim + aktivasyon durumu. |
| Signals feed (strategy detail) | **5s** | Yeni signal esigi. |
| Risk Profile + Circuit Breaker | **10s** | Yavas + reset anlik kullanici eylemi. |
| Logs tail | **2s** | Delta pull (since cursor). |

### 4.3 Polling Disiplini

- `document.visibilityState !== 'visible'` -> interval durdur. Geri gelirse bir kere cek + interval baslat.
- `navigator.onLine === false` -> polling durdur + banner "Baglanti yok". Tekrar online -> auto-resume.
- Exponential backoff on error: 2 ardarda `api.ok=false` -> interval 2x, max 30s. `ok=true` -> reset.
- Request in-flight iken yeni tick gelirse abort eski request (`AbortController`).

Referans kod iskeleti `js/polling.js`:
```js
export function startPolling(fn, { intervalMs, pauseWhenHidden = true } = {}) {
  let timer = null; let ctl = null; let currentInterval = intervalMs; let failCount = 0;
  const tick = async () => {
    if (pauseWhenHidden && document.visibilityState !== 'visible') return;
    ctl?.abort(); ctl = new AbortController();
    const res = await fn(ctl.signal);
    if (res && res.ok === false) { failCount++; currentInterval = Math.min(intervalMs * 2 ** failCount, 30000); }
    else { failCount = 0; currentInterval = intervalMs; }
    timer = setTimeout(tick, currentInterval);
  };
  const onVis = () => { if (document.visibilityState === 'visible') tick(); };
  document.addEventListener('visibilitychange', onVis);
  tick();
  return () => { clearTimeout(timer); ctl?.abort(); document.removeEventListener('visibilitychange', onVis); };
}
```

---

## 5. api.js Fetch Wrapper Sozlesmesi

Dosya: `src/Frontend/js/api.js` (ES module, default export yok, named `api` object). `frontend-api-client` skill pattern'i.

### 5.1 Sozlesme

- Butun backend endpoint'leri **Ardalis.Result sarmali** donusturur. Frontend sarmalı: `{ ok: boolean, status: number, data: T | null, error: string | null, correlationId: string | null }`.
- Her request'te `X-Correlation-Id` gonderilir (crypto.randomUUID); backend de ayni header'i response'ta yansitir -> hata izi.
- 10sn default timeout; caller `timeoutMs` ile override.
- External signal (caller'in `AbortController.signal`'i) iç signal ile birleşir -> ikisinden biri abort olursa request iptal.
- Network / parse / timeout hatalari da `{ ok: false, error: 'timeout' | 'network' | 'parse' | ... }` olarak normalize.
- 4xx -> backend'in `problem+json` veya `{ ok:false, errors: [...] }` sarmasini parse eder; 5xx -> generic error.

### 5.2 Referans Implementasyon Iskeleti

```js
// src/Frontend/js/api.js
const BASE = '/api';
const DEFAULT_TIMEOUT_MS = 10_000;

function mergeAbort(...signals) {
  const ctl = new AbortController();
  for (const s of signals) {
    if (!s) continue;
    if (s.aborted) { ctl.abort(s.reason); break; }
    s.addEventListener('abort', () => ctl.abort(s.reason), { once: true });
  }
  return ctl.signal;
}

async function call(path, { method = 'GET', body, timeoutMs = DEFAULT_TIMEOUT_MS, signal, headers = {} } = {}) {
  const correlationId = (globalThis.crypto?.randomUUID?.() ?? Math.random().toString(36).slice(2));
  const inner = new AbortController();
  const merged = mergeAbort(signal, inner.signal);
  const tid = setTimeout(() => inner.abort(new DOMException('timeout', 'AbortError')), timeoutMs);
  try {
    const res = await fetch(`${BASE}${path}`, {
      method,
      signal: merged,
      credentials: 'same-origin',
      headers: {
        'Accept': 'application/json',
        'X-Correlation-Id': correlationId,
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...headers,
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    const text = await res.text();
    let payload = null;
    try { payload = text ? JSON.parse(text) : null; } catch { return { ok: false, status: res.status, data: null, error: 'parse', correlationId }; }
    if (!res.ok) {
      const err = payload?.errors?.join?.('; ') ?? payload?.title ?? payload?.error ?? res.statusText;
      return { ok: false, status: res.status, data: null, error: err, correlationId };
    }
    return { ok: true, status: res.status, data: payload?.data ?? payload, error: null, correlationId };
  } catch (e) {
    const error = e?.name === 'AbortError' ? (e.message || 'aborted') : (e?.message || 'network');
    return { ok: false, status: 0, data: null, error, correlationId };
  } finally {
    clearTimeout(tid);
  }
}

export const api = {
  call,
  get: (p, o) => call(p, { ...o, method: 'GET' }),
  post: (p, b, o) => call(p, { ...o, method: 'POST', body: b }),
  put: (p, b, o) => call(p, { ...o, method: 'PUT', body: b }),
  delete: (p, o) => call(p, { ...o, method: 'DELETE' }),
};
```

### 5.3 Auth Model (ADR-0007)

- **Admin UI MVP'de YOK.** bkz. [ADR-0007](./adr/0007-admin-auth-model.md). `localStorage.getItem('admin.key')` yaklasimi XSS exfil riski nedeniyle **reddedildi**. `.http` dosyasi + Swagger "Authorize" butonu only — admin anahtari browser'a hic kopyalanmaz.
- User endpoint'leri (read-only sorgular) same-origin + MVP localhost bind ile acik; kimlik dogrulama katmani yok. Production'a gecerken ADR-0009 (JWT + CSRF) zorunlu.
- Frontend `api.js` wrapper'inda `headers` parametresi yine acik (teknik olarak); ama **hic bir frontend kodu `X-Admin-Key` set etmez**. Admin aksiyonlar sadece Swagger/.http uzerinden.

### 5.4 DTO ve Backend Contract

Backend [architecture-notes.md §3](./architecture-notes.md) CQRS slice'larinin her biri Application layerinde `Result<T>` doner. API controller/endpoint adaptor:

- **Basari:** HTTP 200 + JSON `{ "data": <T>, "successMessage": null }` veya sadece `<T>`.
- **Hata:** HTTP 400/404/409/500 + JSON `{ "errors": ["..."], "title": "...", "status": <int> }`.
- **Validasyon hatasi:** 400 `problem+json` ile `errors` alani; form'a field-level mapping.

---

## 6. State Pattern (Page-scoped)

### 6.1 Kural

- **Global Pinia YASAK.** Shared state icin `js/store.js` tek dosyada, `reactive()` ile.
- **Page-scoped state** her sayfada `setup()` icinde `reactive()` / `ref()` ile olusur; sayfa unmount olunca GC.
- **Component composition:** Her sayfa bir kok `createApp({ template: ..., setup() {...} }).mount('#app')` yaratir.

### 6.2 Shared Store Ornegi

```js
// src/Frontend/js/store.js
import { reactive, readonly } from 'vue';
import { api } from '@app/api';

const _state = reactive({
  env: { mode: 'unknown', allowMainnet: false, paperTradeCompleted: false }, // /api/system/status
  symbol: localStorage.getItem('ui.symbol') ?? 'BTCUSDT',
  lastHealthFetch: 0,
});

export const appStore = {
  state: readonly(_state),
  setSymbol(s) { _state.symbol = s; localStorage.setItem('ui.symbol', s); },
  async refreshEnv() {
    const r = await api.get('/system/status');
    if (r.ok) _state.env = r.data;
    return r;
  },
};
```

### 6.3 Page Iskeleti Ornegi (Dashboard)

```js
// src/Frontend/pages/dashboard.js
import { createApp, reactive, onMounted, onBeforeUnmount } from 'vue';
import { api } from '@app/api';
import { appStore } from '@app/store';
import { startPolling } from '@app/ui';

const template = `
  <section class="page-dashboard">
    <h1>Dashboard</h1>
    <div v-if="state.error" class="banner-error">{{ state.error }}</div>
    <div class="grid-summary">
      <article v-for="(s, sym) in state.summary" :key="sym" class="card-summary" @click="go(sym)">
        <h3>{{ sym }}</h3>
        <div v-if="!s" class="skeleton"></div>
        <div v-else>
          <strong>{{ s.lastPrice }}</strong>
          <span :class="{ up: s.priceChangePct24h >= 0, down: s.priceChangePct24h < 0 }">{{ s.priceChangePct24h }}%</span>
        </div>
      </article>
    </div>
  </section>
`;

createApp({
  template,
  setup() {
    const state = reactive({
      summary: { BTCUSDT: null, ETHUSDT: null, BNBUSDT: null },
      positions: { open: [], count: 0, unrealizedPnl: 0 },
      pnl: { realizedToday: 0, unrealizedTotal: 0 },
      health: { api: 'unknown', wsAgeSec: null, circuitBreaker: 'unknown' },
      loading: true, error: null,
    });
    let stopPoll;
    async function refresh(signal) {
      const [sum, pos, pnl, health, cb] = await Promise.all([
        api.get('/market/summary?symbols=BTCUSDT,ETHUSDT,BNBUSDT', { signal }),
        api.get('/positions?status=open', { signal }),
        api.get('/positions/pnl/today', { signal }),
        api.get('/health/ready', { signal }),
        api.get('/risk/circuit-breaker', { signal }),
      ]);
      if (sum.ok) for (const s of sum.data ?? []) state.summary[s.symbol] = s;
      if (pos.ok) { state.positions.open = pos.data ?? []; state.positions.count = state.positions.open.length; }
      if (pnl.ok) state.pnl = pnl.data;
      if (health.ok) state.health = { ...state.health, api: health.data.status, wsAgeSec: health.data.wsAgeSec };
      if (cb.ok) state.health.circuitBreaker = cb.data.status;
      state.loading = false;
      state.error = [sum, pos, pnl, health, cb].find(r => !r.ok)?.error ?? null;
      return { ok: true };
    }
    function go(symbol) { appStore.setSymbol(symbol); location.href = `klines.html?symbol=${symbol}`; }
    onMounted(() => { stopPoll = startPolling(refresh, { intervalMs: 2000 }); });
    onBeforeUnmount(() => stopPoll?.());
    return { state, go };
  },
}).mount('#app');
```

### 6.4 Shared UI Primitives (`js/ui.js`)

`startPolling`, `formatPrice`, `formatPct`, `formatDuration`, `debounce`, basit `<TestnetBanner>` component factory.

---

## 7. Sayfa x State x API Tablosu (Nihai — F1-F5 Cozumu)

| Sayfa | State anahtarlari | Polling | API endpoint'leri (backend CQRS slice) | Domain event eslemesi |
|---|---|---|---|---|
| Dashboard | `summary, positions, pnl, health, error, loading` | 2s | `GET /api/market/summary?symbols=...` (**GetMarketSummaryQuery** — F1), `GET /api/positions?status=open` (**ListPositionsQuery** — F3), `GET /api/positions/pnl/today` (**GetTodayPnlQuery** — F1), `GET /api/health/ready`, `GET /api/risk/circuit-breaker` (`GetCircuitBreakerStatusQuery`) | `KlineClosedEvent`, `PositionOpened/Closed`, `CircuitBreakerTripped/Reset` (polling ile yansir). |
| Kline'lar | `symbol, interval, klines, indicators, chartInstance, error, loading` | 2s | `GET /api/klines?symbol&interval&count=500` (`GetLatestKlinesQuery`) | `KlineIngestedEvent`, `KlineClosedEvent`. |
| OrderBook | `symbol, depth, spread, liquidity, error, loading` | 1s | `GET /api/depth?symbol&depth=20` (`GetDepthSnapshotQuery`) | `DepthSnapshotRefreshedEvent`. |
| Positions | `open, closed, filter, closing, error, loading` | open=2s, closed=30s | `GET /api/positions?status=open` + `GET /api/positions?status=closed&from&to&symbol` (**ListPositionsQuery** F3), `POST /api/positions/{symbol}/close` (`ClosePositionCommand`), `GET /api/positions/{symbol}/pnl` (`GetPositionPnlQuery`) | `PositionOpened/Increased/Reduced/Closed`. |
| Orders | `active, history, filter, canceling, error, loading` | active=3s, history=30s | `GET /api/orders/open?symbol` (`ListOpenOrdersQuery`), `GET /api/orders/history?symbol&status&from&to&skip&take` (`ListOrderHistoryQuery`), `DELETE /api/orders/{clientOrderId}` (`CancelOrderCommand`), `GET /api/orders/{clientOrderId}` (`GetOrderByClientIdQuery`) | `OrderPlaced/Acknowledged/PartiallyFilled/Filled/Canceled/Rejected`. |
| Strategy Config (read-only) | `strategies, selected, paramsForm, signals, error, loading` | 10s, signals 5s | `GET /api/strategies?status` (`ListStrategiesQuery`), `GET /api/strategies/{id}` (**GetStrategyDetailQuery** — F1), `GET /api/strategies/{id}/signals?from&to` (`GetStrategySignalsQuery`) | `StrategyActivated/Deactivated/ParametersUpdated/SignalEmitted`. Admin aksiyonlar frontend'de YOK (ADR-0007). |
| Risk Profile (read-only) | `profile, circuitBreaker, history, error, loading` | 10s | `GET /api/risk/profile` (`GetRiskProfileQuery`), `GET /api/risk/circuit-breaker` (`GetCircuitBreakerStatusQuery`), `GET /api/risk/drawdown-history?days=30` (**GetDrawdownHistoryQuery** — F1) | `RiskLimitUpdated/Breached`, `CircuitBreakerTripped/Reset`. Admin aksiyonlar frontend'de YOK (ADR-0007). |
| Logs | `lines, filter, follow, since, error, loading` | 2s | `GET /api/logs/tail?since&level&limit` (**TailSystemEventsQuery** — F1; SystemEvent tablosu) | N/A — SystemEvent audit tablosu okuma. |

---

## 8. CSS Stratejisi

### 8.1 Dosya

- Tek dosya: `src/Frontend/css/style.css`.
- Her sayfanin HTML'inde `<link rel="stylesheet" href="/css/style.css">`.

### 8.2 Katmanlar

```
/* 1. RESET + TOKEN */
:root {
  --bg: #0f1115; --panel: #161a21; --muted: #9aa3af; --fg: #e7e9ee;
  --accent: #22c55e; --danger: #ef4444; --warn: #f59e0b; --info: #3b82f6;
  --border: #242a33; --tick-up: #22c55e; --tick-down: #ef4444;
  --font: 'Inter', system-ui, sans-serif; --mono: 'JetBrains Mono', monospace;
  --space-1: 4px; --space-2: 8px; --space-3: 12px; --space-4: 16px; --space-6: 24px;
}
*, *::before, *::after { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--fg); font-family: var(--font); }

/* 2. LAYOUT PRIMITIVES */
.container { max-width: 1280px; margin: 0 auto; padding: var(--space-4); }
.grid { display: grid; gap: var(--space-4); }
.grid-3 { grid-template-columns: repeat(3, 1fr); }
.flex-row { display: flex; gap: var(--space-3); align-items: center; }

/* 3. COMPONENTS */
.btn { padding: var(--space-2) var(--space-4); border-radius: 4px; border: 1px solid var(--border); background: var(--panel); color: var(--fg); cursor: pointer; }
.btn.primary { background: var(--info); border-color: var(--info); }
.btn.danger { background: var(--danger); border-color: var(--danger); }
.btn[disabled] { opacity: 0.5; cursor: not-allowed; }
.banner-testnet { background: var(--warn); color: #000; padding: var(--space-2) var(--space-4); text-align: center; font-weight: 600; }
.banner-error { background: var(--danger); color: #fff; padding: var(--space-3); border-radius: 4px; }

/* 4. PAGE SCOPES */
.page-dashboard .card-summary { background: var(--panel); padding: var(--space-4); border-radius: 6px; cursor: pointer; }
.page-klines .chart-container { height: 480px; background: var(--panel); }
.page-orderbook .bid { color: var(--tick-up); }
.page-orderbook .ask { color: var(--tick-down); }

/* 5. UTILITIES */
.up { color: var(--tick-up); }
.down { color: var(--tick-down); }
.muted { color: var(--muted); }
.skeleton { height: 16px; background: linear-gradient(90deg, #1f252d, #2a313a, #1f252d); animation: shimmer 1.4s infinite; border-radius: 4px; }
@keyframes shimmer { 0% { background-position: -200px 0; } 100% { background-position: 200px 0; } }
```

### 8.3 Dark Tema Karari

Trading UI default dark. Light mode toggle **NOT_IN_SCOPE** (MVP); CSS variable'lar hazir biraktigi icin faz 2'de `data-theme="light"` attribute override'i ile cozulur.

### 8.4 Icon Stratejisi

- Emoji yok (CLAUDE.md + markdown kurali).
- `<svg>` inline minimal ikon seti `js/ui.js`'te component factory.
- Ikon kutuphanesi CDN (heroicons vb.) **NOT_IN_SCOPE**.

---

## 9. Erisilebilirlik + UX

### 9.1 Semantic HTML

- `<header>` (navbar), `<main>` (content), `<nav>` (sekme degistirici), `<footer>` (version).
- Table'lar icin `<table><thead><tbody>`; `scope="col"` header'lar; `aria-sort` sortable sutunlarda.
- Form elemanlari `<label>` + `<input id>` association; `aria-describedby` validasyon mesajlari.

### 9.2 Keyboard Navigation

- Tum interaktif eleman focus-visible (outline CSS).
- Modal dialog (confirm cancel) `role="dialog" aria-modal="true"`; focus trap + Esc kapatma.
- Tablo klavye navigasyon MVP'de NOT_IN_SCOPE.

### 9.3 Loading State

- Skeleton placeholder ilk yukleme.
- Inline spinner + `aria-busy="true"` container'da.

### 9.4 Error State

- Ust banner kritik hata.
- Toast-style inline notification.
- Validasyon hatalari field-scoped kirmizi metin.

### 9.5 Empty State

- "Acik pozisyon yok", "Henuz signal gelmedi" vb.
- Tablolarda zero-row durumunda `<tr><td colspan=...>Veri yok</td></tr>`.

### 9.6 XSS Koruma (D4 net yazim)

- Vue `{{ interpolation }}` otomatik HTML escape eder — standart davranis.
- **`v-html` YASAK** (istisna: sadece sanitize edilmis, allow-list filtreli source icin. MVP'de hic kullanilmaz.)
- User input'tan gelen string her yerde interpolation ile renderlenir; HTML parse gerekiyorsa server tarafinda sanitize edilir ve flag'lenir.
- Logs sayfasi mesaj stringleri `{{ }}` ile basilir — markdown/html render yok.

### 9.7 Testnet Banner (ADR 0006 zorunlulugu)

```html
<header class="banner-testnet" v-if="env.mode !== 'mainnet'">
  TESTNET — Sanal bakiye; gercek trade yapilmaz. (mod: {{ env.mode }})
</header>
<header class="banner-testnet mainnet" v-else>
  <strong>MAINNET</strong> — Gercek para ile trade. Circuit breaker: {{ env.circuitBreaker ?? 'bilinmiyor' }}.
</header>
```

- Tum sayfalarda sabit. `appStore.refreshEnv()` boot'ta bir kere + 60s'de bir yenilenir.
- Mainnet bandi kirmizi background + paper-trade / circuit-breaker uyari.

---

## 10. Frontend-Backend Kontrat Eslemesi

### 10.1 Response Sarmasi

Backend Application layer [`Result<T>`](https://github.com/ardalis/Result) doner; API adapter JSON sarmasi:

**Basari** (`Result<T>.Success(value)`):
```json
{ "data": { "...": "..." }, "successMessage": null }
```

**Hata** (`Result<T>.Invalid(errors)`):
```json
{ "status": 400, "errors": ["RiskPerTradePct <= 0.02 olmalidir"], "title": "Validation failed" }
```

**Hata** (generic `Result<T>.Error`):
```json
{ "status": 500, "errors": ["database timeout"], "correlationId": "..." }
```

### 10.2 DTO Field Kataloğu

Backend [architecture-notes.md §3](./architecture-notes.md) slice'larinin query response tipleri. Frontend'te runtime JSON schema **NOT_IN_SCOPE**; type hinting icin JSDoc + dokuman.

#### `MarketSummaryDto` — `GetMarketSummaryQuery` (F1 yeni)
| Alan | Tip |
|---|---|
| `symbol` | string |
| `lastPrice` | string (decimal) |
| `priceChange24h` | string |
| `priceChangePct24h` | number |
| `volume24h` | string |
| `bestBid` | string |
| `bestAsk` | string |

#### `KlineDto` — `GetLatestKlinesQuery`
| Alan | Tip |
|---|---|
| `symbol` | string (BTCUSDT) |
| `interval` | string (1m/1h/...) |
| `openTime` | ISO8601 (UTC) |
| `closeTime` | ISO8601 |
| `open`, `high`, `low`, `close` | number |
| `volume`, `quoteVolume` | number |
| `tradeCount` | int |
| `closed` | boolean |

#### `DepthSnapshotDto` — `GetDepthSnapshotQuery`
| Alan | Tip |
|---|---|
| `symbol` | string |
| `lastUpdateId` | number |
| `bids` | `Array<{ price: string, qty: string }>` |
| `asks` | `Array<{ price: string, qty: string }>` |
| `snapshotFetchedAt` | ISO8601 |

#### `BookTickerDto` — `GetBookTickerQuery`
`symbol, bidPrice, bidQty, askPrice, askQty, updateTime`.

#### `OrderDto`
`clientOrderId, exchangeOrderId?, symbol, side, type, status, quantity, filledQty, price?, stopPrice?, averageFillPrice?, timeInForce, placedAt, updatedAt, strategyId?, fills[]`.

#### `OrderFillDto`
`exchangeTradeId: number, price: string, qty: string, commission: string, commissionAsset: string, filledAt: ISO`.

#### `PositionDto` — `ListPositionsQuery` (F3)
| Alan | Tip |
|---|---|
| `id` | number |
| `symbol` | string |
| `status` | `"Open" \| "Closed"` |
| `quantity` | string (signed) |
| `averageEntryPrice` | string |
| `markPrice` | string? |
| `realizedPnl`, `unrealizedPnl` | string |
| `openedAt` | ISO |
| `closedAt` | ISO? |
| `strategyId` | string? |
| `durationSec` | int |

#### `TodayPnlDto` — `GetTodayPnlQuery` (F1 yeni)
| Alan | Tip |
|---|---|
| `realizedToday` | string (decimal) |
| `unrealizedTotal` | string |
| `openPositionCount` | int |
| `closedTodayCount` | int |

#### `StrategyDto` / `StrategyDetailDto` (F1 yeni)
| Alan | Tip |
|---|---|
| `id` | string (ULID) |
| `name` | string |
| `type` | `"Grid" \| "TrendFollow" \| "MeanReversion"` |
| `parameters` | object (type'a gore) |
| `symbols` | string[] |
| `status` | `"Draft" \| "Active" \| "Paused" \| "DeactivatedBySystem"` |
| `isActive` | boolean |
| `activatedAt`, `deactivatedAt` | ISO? |
| `lastSignalAt` | ISO? |
| `recentSignals` (detail only) | `StrategySignalDto[]` |

#### `StrategySignalDto`
`id, barOpenTime, symbol, signalType, confidence, emittedAt`.

#### `RiskProfileDto`
`riskPerTradePct, maxPositionSizePct, maxGrossExposurePct, maxDrawdown24hPct, maxDrawdownAllTimePct, maxConsecutiveLosses, caps: { riskPerTradeCap, maxPositionCap }, updatedAt`.

#### `CircuitBreakerStatusDto`
`status, trippedAt?, consecutiveLossCount, reason?`.

#### `DrawdownPointDto` — `GetDrawdownHistoryQuery` (F1 yeni)
`date, equity, drawdown, drawdownPct`.

#### `SystemStatusDto` — `GetSystemStatusQuery` (F1 yeni)
`env: "testnet"|"mainnet", allowMainnet, circuitBreaker, wsSupervisorHeartbeatAt, appVersion, lastMigration, paperTradeCompleted`.

#### `SystemEventTailDto` — `TailSystemEventsQuery` (F1 yeni)
`events: Array<{ timestamp, level, source, type, message, payload? }>, nextSince: ISO`.

#### `OverrideRiskCapsRequest` — body shape (F5)
Admin endpoint body shape — frontend'de kullanilmaz (ADR-0007 admin UI yok), Swagger/.http referansi:
```json
{ "riskPerTradeCap": 0.015, "maxPositionCap": 0.15, "adminNote": "manuel override - test run" }
```

### 10.3 Decimal Hassasiyeti

Binance fiyat ve qty `decimal` (string tabanli). JavaScript `number` IEEE-754 binary float; `1e-8` hassasiyet kayiplari olasi. Kural:
- **Backend** decimal'leri `string` olarak dondurur (JSON).
- **Frontend** goruntu icin `parseFloat` + `toFixed(filters.tickSize)` yapar ama form input'ta string kalir.
- Agirlikli hesaplar (imbalance, spread) frontend'te `parseFloat` ile yaklasik; kritik PnL turetimi backend tarafinda yapilir ve string doner.

### 10.4 Date / Timezone

- Butun backend ISO8601 UTC (`...Z`). Frontend `date-fns` `parseISO` + `format(date, 'yyyy-MM-dd HH:mm:ss')` local gosterim.

### 10.5 CORS

- MVP: Frontend ayni origin'de serve edilir (ASP.NET Core `UseStaticFiles` ile `src/Frontend/` wwwroot'a point). CORS gereksiz.
- Eger ayri domain'den serve edilirse backend'te named policy + explicit origin whitelist (`appsettings.json Cors.Origins`). Wildcard YASAK (D2).

---

## Ozet / Plan.md Icin Cikarim

- **Tek sayfa = tek HTML + tek JS entry**; MPA. 8 sayfa.
- **importmap**: Vue 3.4.38 + lightweight-charts 4.1.3 + date-fns 3.6.0. SFC/bundler yok.
- **MVP real-time = HTTP polling**. SignalR **NOT_IN_SCOPE** (A1 cozumu).
- **`api.js` fetch wrapper** Ardalis.Result sarmasini normalize eder.
- **Pinia yasak**; page-scoped `reactive()` + tek `store.js`.
- **CSS**: tek `style.css` + CSS variables; dark tema default.
- **Testnet banner** ADR 0006 gereksinimi.
- **Admin UI MVP'de YOK** — ADR-0007 (strategies/risk sayfalari read-only; activate/deactivate/override Swagger/.http uzerinden).
- **`v-html` YASAK** (D4).
- **SystemEvents tablosu tek log kaynagi** — `/api/logs/tail` (F1).

Kaynaklar:
- [docs/architecture-notes.md](./architecture-notes.md)
- [docs/research/binance-research.md](./research/binance-research.md)
- [docs/adr/0002-binance-ws-supervisor-pattern.md](./adr/0002-binance-ws-supervisor-pattern.md)
- [docs/adr/0006-testnet-first-policy.md](./adr/0006-testnet-first-policy.md)
- [docs/adr/0007-admin-auth-model.md](./adr/0007-admin-auth-model.md)
- [Vue 3 — Using Vue from CDN](https://vuejs.org/guide/quick-start.html#using-vue-from-cdn)
- [Vue 3 — Simple State Management with Reactivity API](https://vuejs.org/guide/scaling-up/state-management.html#simple-state-management-with-reactivity-api)
- [MDN — importmap](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/script/type/importmap)
- [TradingView Lightweight Charts](https://tradingview.github.io/lightweight-charts/)
- [date-fns docs](https://date-fns.org/)
- [ardalis/Result](https://github.com/ardalis/Result)
- [OWASP — XSS Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html)
