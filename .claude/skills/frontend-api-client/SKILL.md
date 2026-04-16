---
name: frontend-api-client
description: /api/* çağrıları için tek merkezi fetch wrapper'ı. Result<T> response sözleşmesi (ok/data/error), timeout, AbortController, basit error normalize, CSRF token desteği (cookie-based auth varsa). frontend-dev agent'ının her HTTP çağrısında kullandığı disiplin.
---

# frontend-api-client

Her yerde `fetch(...)` çağırmak yerine merkezi wrapper — hata/timeout/format tutarlılığı.

## Dosya: src/Frontend/js/api.js

```javascript
const BASE = '/api';
const DEFAULT_TIMEOUT = 10000; // 10s

async function request(method, path, body, { timeout = DEFAULT_TIMEOUT, headers = {} } = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeout);
  try {
    const res = await fetch(BASE + path, {
      method,
      headers: {
        'Content-Type': 'application/json',
        ...headers,
      },
      body: body ? JSON.stringify(body) : undefined,
      credentials: 'include',
      signal: controller.signal,
    });

    const contentType = res.headers.get('content-type') || '';
    const payload = contentType.includes('application/json') ? await res.json() : await res.text();

    if (res.ok) {
      return { ok: true, data: payload, status: res.status };
    }

    // Backend Result<T> failure → ProblemDetails veya validation errors
    const errorMessage = (payload && (payload.detail || payload.title)) || `HTTP ${res.status}`;
    return {
      ok: false,
      error: errorMessage,
      status: res.status,
      details: payload,
    };
  } catch (e) {
    if (e.name === 'AbortError') {
      return { ok: false, error: 'İstek zaman aşımına uğradı.', status: 0 };
    }
    return { ok: false, error: e.message || 'Ağ hatası', status: 0 };
  } finally {
    clearTimeout(timer);
  }
}

export const api = {
  get:    (path, opts)      => request('GET', path, null, opts),
  post:   (path, body, opts)=> request('POST', path, body, opts),
  put:    (path, body, opts)=> request('PUT', path, body, opts),
  delete: (path, opts)      => request('DELETE', path, null, opts),
};
```

## Kullanım

```javascript
import { api } from '../api.js';

const r = await api.get('/api/klines?symbol=BTCUSDT&limit=20');
if (r.ok) {
  state.klines = r.data;
} else {
  state.error = r.error;
  if (r.status === 401) { /* logout akışı */ }
}
```

## Kurallar

- **Her HTTP çağrısı** bu wrapper'dan gitsin — raw `fetch` kullanma.
- Response şekli: `{ ok: bool, data: any, error?: string, status: number, details?: any }`.
- 401 / 403 akış kararı component'in — wrapper sadece normalize eder.
- Timeout default 10s; uzun endpoint'ler için call-site'ta override.
- CSRF: cookie-auth varsa `headers: { 'X-XSRF-TOKEN': readCookie('XSRF-TOKEN') }` eklenecek; şu an auth yok.

## Hata Teşhisi

- **`AbortError`** → client timeout, backend yavaş veya down.
- **status=0** → network unreachable, CORS hata, DNS fail.
- **400 validation** → `r.details` içinde `errors` dictionary'si; form'a bas.

## Kaynak

- https://developer.mozilla.org/en-US/docs/Web/API/fetch
- https://developer.mozilla.org/en-US/docs/Web/API/AbortController
