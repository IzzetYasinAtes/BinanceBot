# 0007. Admin Auth Model — Solo-Dev Local Kiosk

Date: 2026-04-17
Status: Accepted

## Context

Master plan review'inda (docs/plan-review-notes.md §D1) blocker tespit edildi: frontend-design.md:362 admin API key'inin browser `localStorage`'ta tutulmasini oneriyor. Bu yaklasim XSS vektoru altinda tam exfil riski tasir. Trading admin endpoint'leri (ornegin `POST /api/strategies/{id}/activate`, `POST /api/risk/override-caps`, `POST /api/instruments/{symbol}/halt`) para transfer potansiyeli olan is mantigi ici gorur — guvenlik seviyesi yetersiz.

Projenin gercek kullanim kalibi **solo-dev local** bir trading bot'dur (tek kullanici: repo sahibi; calisma ortami: localhost; icsel tehdit modeli yok). Tam kapsamli OAuth/OIDC + refresh rotation + CSRF double-submit hem asiri muhendislik hem zamansiz bir yatirim olur. Ayrica ADR-0006 (testnet-first) geregi MVP kapsaminda production trade dahi yapilmayacak; paper trade + testnet live limitleri calisir.

Secenekler:

1. **(a) HttpOnly secure cookie + CSRF double-submit** — tam web-app tarzi; cookie-based oturum yonetimi + anti-CSRF token; backend `AntiforgeryToken` middleware; frontend double-submit pattern. MVP icin asiri agir.
2. **(b) Short-lived JWT + refresh rotation** — OAuth2-benzeri pattern; yine cok parca (authorization server, refresh store, rotation disiplini); solo-dev icin gereksiz.
3. **(c) Solo-dev local kiosk mode** — Admin UI frontend'de yok; admin endpoint'leri sadece Swagger "Authorize" butonu + `.http` test dosyasi + curl uzerinden, `X-Admin-Key` header ile manuel cagrilir. Key dev makinesinde user-secrets'ta tutulur, browser'a hic kopyalanmaz. Production mode (AllowMainnet=true) endpoint'ler 403 doner; prod'a gidis icin ADR-0009 (JWT+CSRF) kacinilmaz.

## Decision

MVP icin **(c) Solo-dev local kiosk mode** secildi.

- Backend'e `ApiKeyAuthenticationHandler` eklenir (scheme adi: `AdminApiKey`). Handler `X-Admin-Key` header'ini okur, `BinanceOptions.AdminApiKey` (user-secrets) ile sabit-zaman esitlik karsilastirir (`CryptographicOperations.FixedTimeEquals`).
- Endpoint authorization policy `"AdminPolicy"` bu scheme'i gerektirir; policy admin-only CQRS slice'larina uygulanir (ADR-0005 + plan.md §9 envanterinde "Admin" flag'li endpoint'ler).
- **Frontend admin UI YOKTUR.** `strategies.html` sayfasindaki "Activate/Deactivate" butonlari MVP'de **devre disi** render edilir; sayfa sadece read-only liste + parametre goruntuleme yapar. Activate/Deactivate/Override aksiyonlari **sadece**:
  - Swagger UI "Authorize" dialog'unda manuel `X-Admin-Key` girisi (development ortaminda Swagger acik),
  - `tests/manual/*.http` test dosyalari (gercek `@adminKey` degiskeni user-secrets'tan gelir),
  - Curl / Postman / benzeri CLI tool'lar
  uzerinden yapilir.
- Production guard: `BinanceOptions.AllowMainnet == true` oldugunda `AdminApiKeyAuthenticationHandler` **her istegi 403 Forbidden** ile reddeder — "nafile kapi". Production'a gidis ADR-0009 (JWT + CSRF) sartini acar; ADR-0009 kabul edilmeden mainnet'te admin endpoint kullanilamaz. Bu kasitli bir engel; operator ADR-0009'u ciktan onaylamadan uretim trade'i yapmasin.
- `X-Admin-Key` header degeri: environment variable `BINANCEBOT_Binance__AdminApiKey` (user-secrets dev, env var prod); boot-time `ValidateOnStart` ile bos olamaz (eger AllowMainnet=false bile olsa admin endpoint'ler erisilebilir ama key zorunlu); key uzunlugu min 32 bayt / base64.
- Secret rotation: manuel; `dotnet user-secrets set "Binance:AdminApiKey" "<yeni-key>"` + restart. Key dizin/dosya sistemine yazilmaz.

## Consequences

### Pozitif

- XSS ile admin key calinma riski sifirlanir (browser'a hic kopyalanmaz).
- Solo-dev is akisinda Swagger + .http dosyasi + user-secrets zaten mevcut; ek altyapi sifir.
- Production'a gecis explicit bir ADR (0009) gerektirdigi icin kaza ile live trade riskini azaltir.
- Frontend sade kalir — admin UI olmadiginda "yetki atlatma" saldiri vektoru dogmaz (clickjacking, CSRF).
- Kisitli yuzey olmasi reviewer-security-scan skill'inin kontrollerini kolaylastirir.

### Negatif / Tradeoff

- Frontend UX'te strategy activate/override butonu yok — her admin aksiyonu icin developer CLI/Swagger'a gitmek zorunda. Uzun vadede friction.
- `.http` dosyalarindaki degisken-substitution disiplini gerektirir — `@adminKey` environment degiskeni veya user-secrets fetch; hata yaparsa placeholder commit olabilir (reviewer-diff-review grep kurali ile yakalanacak).
- `AllowMainnet=true` oldugunda **tamamen** admin yolu kapanir; acil mudahale gerekirse (orn. panic-close) manuel DB update ya da ADR-0009 gecis sartini hizlandirma gerekir. Bu kasten yavaslatma (guvenlik kazanci > convenience).
- Sabit-zaman esitlik compare (`FixedTimeEquals`) gerekli; naive `==` timing attack riski.

### Notr

- ApiKeyAuthenticationHandler implementasyonu `Microsoft.AspNetCore.Authentication` standart pipeline'i kullanir; ekstra NuGet paketi gereksiz.
- `AdminPolicy` policy adi backend-design.md §11.2'de "Admin -> `.RequireAuthorization(\"AdminPolicy\")`" olarak referans edilmis; bu ADR o adla tutarli.
- `OverrideRiskCapsCommand` gibi kritik komutlar zaten `AdminNote` (audit string) zorunlu tutuyor; admin kimligi anonim olmasa bile ek audit alani degismiyor.

## Alternatifler

- **(a) Cookie + CSRF double-submit** — Reddedildi: solo-dev icin asiri muhendislik; cookie session store, antiforgery middleware, browser logout UX vs. MVP degeri dusuk.
- **(b) JWT + refresh rotation** — Reddedildi: OAuth2 endpoint'leri (token/introspect/revoke), refresh store (Redis/DB), rotation disiplini, frontend sessionStorage vs. cookie karari — hepsi kapsam disi. Faz-2 ADR-0009'a birakildi.
- **(d) IP whitelist + bind 127.0.0.1** — Reddedildi (yedek olarak yasar ama tek basina yetersiz): `Kestrel.Endpoints.Http.Url = "http://127.0.0.1:5000"` prod'da da gecerli; ancak kimlik bilgisi hala gerekli. Kiosk mode'un yanina yardimci katman olarak eklenir (plan.md §12 CORS/bind policy).
- **(e) Admin UI + OIDC external IdP (Azure AD, Google)** — Reddedildi: tek kullanicili localhost bot icin cloud IdP bagimliligi yaratmak gereksiz.

## Kaynak

- [docs/plan-review-notes.md §D1](../plan-review-notes.md) — blocker tanimi
- [ADR 0004 — Secret Management](./0004-secret-management.md) — user-secrets + env var katmanlari
- [ADR 0006 — Testnet-First Policy](./0006-testnet-first-policy.md) — `AllowMainnet` guard
- [Microsoft Docs — Authentication Handler](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/) — `AuthenticationHandler<TOptions>` pattern
- [Microsoft Docs — Antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery) — Faz-2 ADR-0009 girdisi
- [OWASP — XSS Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html)
- Frontend patch referansi: [frontend-design.md](../frontend-design.md) §5.3 "Admin UI MVP'de YOK" yumusatmasi bu ADR'ye atifli.
