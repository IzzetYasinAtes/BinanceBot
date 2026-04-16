# 0004. Secret Management Politikasi

Date: 2026-04-16
Status: Accepted

## Context

BinanceBot uretimde iki sinif secret tutar:

1. **Binance API key + secret** — trade yetkisi; sizinti = fon kaybi.
2. **MSSQL connection string** — database credentials.
3. (Ikincil) Opsiyonel telemetry / alarm webhook key leri.

CLAUDE.md Altin Kural 2: **"Secret commit yasak — `appsettings.json` sadece template; gercek baglanti string i `.env`/user-secrets ta."**. Ayrica testnet-first politikasi (bkz. [0006](./0006-testnet-first-policy.md)) kullanicinin yanlislikla production key koyup trade baslatmamasini zorlar.

## Decision

Secret yonetimi ortamlara gore katmanlanir. Kayit oncelik sirasi (yuksek -> dusuk): **Environment Variables** > **User Secrets (dev)** > **appsettings.{Environment}.json** > **appsettings.json (template, secret icermez)**.

### 4.1 Dev (developer makinesi)

- `dotnet user-secrets init` ile her developer kendi makinesinde secret tutar.
- UserSecretsId `.csproj` te (commit te ama secret in kendi disarda).
- Gercek key ler `secrets.json` dosyasinda (%APPDATA%\Microsoft\UserSecrets\...).
- `appsettings.Development.json` sadece `UseTestnet: true` ve non-sensitive degerler.

### 4.2 Prod / Staging

- Linux/Windows host: `export Binance__ApiKey=...`, `export Binance__ApiSecret=...`, `export ConnectionStrings__Default=...` (double-underscore = config section ayraci).
- Docker/Compose: `env_file: .env` (her host makinede, commit yok; `.gitignore` ile korunur).
- Ileri fazda (k8s): Azure Key Vault / AWS Secrets Manager / Kubernetes Secrets.

### 4.3 appsettings.json Template

Commit edilen `appsettings.json`:

```json
{
  "Binance": {
    "UseTestnet": true,
    "RestBaseUrl": "https://testnet.binance.vision",
    "WsBaseUrl": "wss://stream.testnet.binance.vision",
    "ApiKey": "",
    "ApiSecret": "",
    "RecvWindowMs": 5000
  },
  "ConnectionStrings": {
    "Default": ""
  }
}
```

Bos string ler `""` — kod bos string i "yok" sayar ve **boot-time reddeder**.

### 4.4 Boot-Time Validation

`Program.cs` sonunda, `Host.Run()` oncesinde `StartupSecretValidator` calisir:

1. `Binance.ApiKey` bos / "REPLACE_ME" / null -> **exit 1**.
2. `Binance.ApiSecret` bos -> **exit 1**.
3. `ConnectionStrings.Default` bos -> **exit 1**.
4. `Binance.UseTestnet == false` **ve** `ASPNETCORE_ENVIRONMENT != "Production"` -> **exit 1** (development te mainnet yasak).
5. `Binance.UseTestnet == true` fakat `Binance.RestBaseUrl` mainnet URL i (`api.binance.com`) iceriyorsa -> **exit 1** (config mismatch).
6. **Testnet key prefix kontrolu**: Testnet API key leri normalde base64 formatta ama prefix ayirici yok. Bu sebeple zorunlu ek kural: `UseTestnet == true` iken `RestBaseUrl` `testnet.binance.vision` **icermek zorunda**; `WsBaseUrl` `stream.testnet.binance.vision` icermek zorunda. Mismatch -> exit 1.
7. Exit kodu 1 + structured log FATAL ("secret missing: <key>"); hata mesajinda **secret degeri kesinlikle log a yazilmaz**.

### 4.5 Secret Redaction (Logging)

Uygulama log larinda secret in asla yer almamasi icin:

- `ILogger` kullanimlari structured — object dump yasak (`_logger.LogInformation("config {@Config}", config)` YASAK).
- HttpClient logging handler i `X-MBX-APIKEY` header ini `[REDACTED]` ile degistirir (DelegatingHandler).
- Exception detaylarinda Binance secret `BalanceException` vb. ile karismamali — `BinanceHttpClient` exception olustururken request-body yi log lamaz.

### 4.6 Rotation

- Binance API key rotasyonu manuel (dev karari). Yeni key uretildiginde env var update + uygulama restart.
- DB credential rotasyonu icin connection pool shutdown/restart.

## Consequences

### Pozitif

- `git grep` ile hicbir secret commit icinde bulunmaz.
- Yanlislikla production key ile dev baslatmak imkansiz — boot-time reddeder.
- Testnet-first politikasi (bkz. 0006) secret katmaninda da zorunlu hale gelir.

### Negatif / Tradeoff

- Her dev makinesinde `user-secrets set` calistirmasi gerek; onboarding sturtunmesi (tek kisilik projede problem degil).
- CI/CD de secret env var yonetimi: GitHub Actions `secrets.*` ile map lenmeli.

### Notr

- `Microsoft.Extensions.Configuration` zincirine ozel bir saglayici eklenmiyor; standart ASP.NET Core setup yeterli.

## Alternatifler

1. **Secret i appsettings.Development.json a koy, `.gitignore` a ekle** — Reddedildi: commit kaza olasiligi yuksek, user-secrets buna ozel cozum.
2. **HashiCorp Vault / Azure Key Vault simdiden** — Solo-dev icin over-engineering. Ileri faz.
3. **.env + dotenv nuget** — User Secrets zaten ayni is icin; ikinci araci surmek DRY ihlali.
4. **Production key de dev te kabul + manual flag** — Kaza faktoru kabul edilemez. Reddedildi.

## Kaynak

- [Microsoft — Safe storage of app secrets (User Secrets)](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [Microsoft — Configuration providers](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [OWASP — Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)
- CLAUDE.md Altin Kural 2 (secret commit yasak)
- [docs/adr/0006-testnet-first-policy.md](./0006-testnet-first-policy.md)
- [testnet.binance.vision](https://testnet.binance.vision/)
