---
name: tester-api-contract
description: API endpoint'lerini curl/HttpClient ile çağırır, request/response şema + status code + content-type kontratına uyuyor mu doğrular. Backend-dev'in ürettiği endpoint'in UI'ın beklediğiyle uyumlu olduğunu test eder. tester agent'ın kullandığı skill.
---

# tester-api-contract

UI ve backend uyuşmazlığı feature'ı sessizce bozar. Contract test bunu önler.

## Temel Kalıp

```bash
# 1) Happy path — GET
curl -sS -w '\nHTTP_CODE=%{http_code}\n' \
     -H 'Accept: application/json' \
     'http://localhost:5000/api/klines?symbol=BTCUSDT&interval=1m&limit=5'

# 2) POST
curl -sS -w '\nHTTP_CODE=%{http_code}\n' \
     -X POST \
     -H 'Content-Type: application/json' \
     -d '{"symbol":"BTCUSDT","interval":"1m","openTime":1712345678000,"open":65000,"high":65100,"low":64950,"close":65050,"volume":1.23}' \
     'http://localhost:5000/api/klines'

# 3) Validation hata (beklenen 400)
curl -sS -w '\nHTTP_CODE=%{http_code}\n' \
     -X POST -H 'Content-Type: application/json' \
     -d '{"symbol":""}' \
     'http://localhost:5000/api/klines'
```

## Contract Doğrulama Checklist

### Response Body Schema
- [ ] Beklenen top-level field'lar var mı? (örn. `data`, `errors`, `status`)
- [ ] Field tipleri doğru mu? (string, number, boolean, array)
- [ ] Null beklenmedik yerde mi geliyor?
- [ ] Date format ISO 8601 mi?

### HTTP Status Code
- [ ] Happy path 200/201 mi?
- [ ] Validation 400 + ValidationProblem response mu?
- [ ] Not found 404 mi?
- [ ] Conflict 409 mu?
- [ ] Unauthorized 401 / Forbidden 403 mu?

### Headers
- [ ] `Content-Type: application/json; charset=utf-8`?
- [ ] CORS headers beklendiği gibi?
- [ ] Auth gereken endpoint'te `401 WWW-Authenticate` dönüyor mu?

### Idempotency (POST/PUT)
- [ ] Aynı request iki kere gönderince aynı sonuç mu (idempotent endpoint'ler için)?
- [ ] Duplicate request'te 409 mı?

### OpenAPI Uyumu
- [ ] `/openapi/v1.json` (ASP.NET Core 10 default) endpoint erişilebilir mi?
- [ ] Gerçek response OpenAPI spec'i ile uyumlu mu?
  ```bash
  curl -s http://localhost:5000/openapi/v1.json | node -e "
    const s = JSON.parse(require('fs').readFileSync(0, 'utf8'));
    console.log(Object.keys(s.paths).length, 'paths');
    // path başına method + response check
  "
  ```

## Hızlı Smoke Test Script

```bash
#!/usr/bin/env bash
set -eu
BASE="http://localhost:5000"

echo "[1] Health"
curl -fsS "$BASE/health" || echo "⚠ health down"

echo "[2] OpenAPI spec"
curl -fsS "$BASE/openapi/v1.json" > /tmp/openapi.json

echo "[3] GET /api/klines"
curl -fsS "$BASE/api/klines?symbol=BTCUSDT&limit=5" | node -e "
  const arr = JSON.parse(require('fs').readFileSync(0,'utf8'));
  if (!Array.isArray(arr)) { console.error('❌ not array'); process.exit(1); }
  if (arr.length > 0 && !arr[0].symbol) { console.error('❌ missing symbol field'); process.exit(1); }
  console.log('✓ array with', arr.length, 'items');
"
```

## Kural

- Lokal endpoint'e ve staging'e test — **prod'a YASAK**.
- Contract kırılmış (schema uyumsuzluğu) → 🚫 blocker.
- Secret/token içeren request header'ını logla yazma.
- Her test için `.ai-trace/api-contract-<date>.log` kaydet.

## Kaynak

- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi
- https://curl.se/docs/manpage.html
