---
name: frontend-importmap
description: <script type="importmap"> yapısı + CDN dependency tanımları. Vue 3, isteğe bağlı vue-router, chart kütüphanesi (Chart.js CDN), date-fns vb. Tüm bağımlılıklar versiyon pinli. frontend-dev agent'ın dependency ekleme/güncelleme skill'i.
---

# frontend-importmap

Tarayıcı native import map — npm yok, bundler yok.

## Temel Form

```html
<script type="importmap">
{
  "imports": {
    "vue": "https://unpkg.com/vue@3.4.21/dist/vue.esm-browser.prod.js",
    "vue-router": "https://unpkg.com/vue-router@4.3.0/dist/vue-router.esm-browser.js",
    "chart.js": "https://cdn.jsdelivr.net/npm/chart.js@4.4.2/+esm",
    "date-fns": "https://cdn.jsdelivr.net/npm/date-fns@3.6.0/+esm"
  }
}
</script>
```

## Kurallar

- **Versiyon pin** zorunlu — `@3` gibi open range YASAK. Tam sürüm (`@3.4.21`) kullan.
- **Prod build** seç: Vue için `vue.esm-browser.prod.js`, dev için `vue.esm-browser.js` (warning'ler görünür).
- Her dependency için bir line; artık kullanılmayanı sil.
- `scopes` field'ı (submodule override) gerekmedikçe kullanma — karmaşıklık artar.

## CDN Tercihi

1. `https://unpkg.com/<pkg>@<version>/<path>` — npm registry mirror.
2. `https://cdn.jsdelivr.net/npm/<pkg>@<version>/+esm` — otomatik ESM dönüşüm.
3. `https://esm.sh/<pkg>@<version>` — modern ESM CDN.

**Tercih:** unpkg (stable, öngörülebilir path'ler). Chart.js gibi CommonJS-only kütüphaneler için `+esm` gerekir → jsdelivr.

## Versiyon Güncellemesi

Yeni sürüm çıktığında:
1. Kütüphanenin changelog'unu oku.
2. `importmap`'te tek line güncelle.
3. Browser'da F12 → Console temiz mi, deprecation warning var mı?
4. Tester agent ile smoke test.

## Yasaklar

- Latest-tag (`@latest`) — deterministic olmaz.
- Birden çok CDN aynı pakete (çakışma riski, büyük download).
- Script'i `importmap` dışında `<script src="https://...">` ile yüklemek (module graph'a girmez).

## Hata Teşhisi

- **"Failed to resolve module specifier"** — importmap'te key yok.
- **"CORS"** — CDN CORS header vermiyor; başka CDN dene.
- **"Unexpected token export"** — CommonJS dosyayı ES module olarak yüklemeye çalışıyorsun, `+esm` ekle.

## Kaynak

- https://developer.mozilla.org/en-US/docs/Web/HTML/Element/script/type/importmap
- https://vuejs.org/guide/quick-start.html#using-vue-from-cdn
- https://wicg.github.io/import-maps/
