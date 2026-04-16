---
name: reviewer-security-scan
description: Security açığı tarar — secret leak, SQL injection, unauthenticated endpoint, CORS misconfig, DTO over-posting, XSS, CSRF, insecure deserialization. OWASP ASP.NET Core cheat sheet tabanlı. reviewer agent PR'larda uygular.
---

# reviewer-security-scan

Güvenlik hatası ortalama review'dan kaçar. Checklist disiplini şart.

## Secret Leak

- [ ] `appsettings.json`'da `ConnectionStrings`, `ApiKey`, `Secret` gibi alanlarda **gerçek değer** var mı?
- [ ] Commit'e `.env`, `appsettings.Production.json`, `user-secrets.json` giriyor mu?
- [ ] Kod içinde hardcoded API key / connection string?
- [ ] `.gitignore` bu dosyaları ignore'lıyor mu?

**Fix:** Environment variable + `IConfiguration` + User Secrets (dev) + Azure Key Vault / AWS Secrets Manager (prod).

## SQL Injection

- [ ] `DbContext.Database.ExecuteSqlRaw(userInput)` — parametresiz raw SQL **YASAK**.
- [ ] `FromSqlRaw(...)` kullanılıyorsa `{0}` placeholder + interpolation ÖZELLİKLE `FromSqlInterpolated` kullan.
- [ ] Dynamic LINQ (`Dynamic.Linq`) kullanıyorsa input sanitize edilmiş mi?

**Fix:** Parameterized LINQ, FromSqlInterpolated, `EF.Parameter(...)`.

## Unauthenticated Endpoint

- [ ] `Program.cs`'te default olarak `.RequireAuthorization()` var mı, yoksa her endpoint manuel mi?
- [ ] `[AllowAnonymous]` attribute'u bilinçli mi eklenmiş? (login, health check hariç)
- [ ] Admin endpoint'leri özel policy ile (`RequireRole`, `RequirePolicy`)?

**Fix:** `app.MapGroup("/api").RequireAuthorization();` default.

## CORS

- [ ] `AllowAnyOrigin()` + `AllowCredentials()` → YASAK kombine (spec ihlali ve güvenlik açığı).
- [ ] `AllowAnyOrigin()` prod'da — kısıtla (`WithOrigins("https://yourapp")`).
- [ ] Preflight için `WithMethods` + `WithHeaders` belirli mi?

## DTO Over-posting / Mass Assignment

- [ ] API endpoint Command record'unu direkt alıyor mu? — client `IsAdmin = true` gibi hassas field inject edebilir.
- [ ] Sadece beklenen field'ları explicitly binding ediyor musun? (Command record'larında sadece ihtiyaç alanlar)

**Fix:** Request DTO ayrı tut; handler'a mapping'de hassas field'ı zorla override.

## XSS (Frontend)

- [ ] `v-html` direktifi kullanılıyor mu? Kullanıcı verisini `v-html` ile basmak XSS.
- [ ] Third-party embed (iframe) sanitize ediliyor mu?
- [ ] Backend'den gelen response `Content-Type: application/json` + JSON escape otomatik mi?

**Fix:** `{{ }}` ile escape; `v-html` sadece güvenilir kaynak.

## CSRF (cookie-auth varsa)

- [ ] `SameSite=Lax` veya `Strict` cookie?
- [ ] POST/PUT/DELETE endpoint'lerde CSRF token (XSRF) zorunlu mu?
- [ ] `AddAntiforgery()` + `RequireAntiforgeryToken()` filtre?

(Şu an auth yok; auth eklenince bu checklist aktif.)

## Insecure Deserialization

- [ ] `BinaryFormatter` kullanımı? → YASAK (CVE kaynağı).
- [ ] `TypeNameHandling.All` Newtonsoft.Json? → YASAK.
- [ ] `System.Text.Json` default güvenli, ama custom converter varsa audit.

## HTTPS / HSTS

- [ ] `UseHttpsRedirection()` aktif mi?
- [ ] `AddHsts()` prod'da? (dev'de skip)
- [ ] HTTP endpoint'e redirect 301 mi, 307 mi? (307 tercih — method preserve)

## Log Leak

- [ ] Connection string / API key log'a basılıyor mu? (ILogger argümanları)
- [ ] User PII (email, telefon) structured log'da açık mı?

## Verdict

- Secret leak → 🚫 blocker, revert commit
- SQL injection → 🚫 blocker
- Unauthenticated critical endpoint → 🚫 blocker
- CORS `AllowAnyOrigin + AllowCredentials` → 🚫 blocker
- DTO over-posting riski → ⚠️ minor + fix öner
- Diğer → duruma göre

## Kaynak

- https://owasp.org/www-project-cheat-sheets/cheatsheets/Dotnet_Security_Cheat_Sheet.html
- https://learn.microsoft.com/en-us/aspnet/core/security/
- https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Scripting_Prevention_Cheat_Sheet.html
