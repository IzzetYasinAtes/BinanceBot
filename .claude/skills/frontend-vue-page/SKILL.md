---
name: frontend-vue-page
description: Vue 3 CDN + ES module ile SFC-less sayfa iskelesi üretir. Tek HTML dosyası + bağımsız JS module'ları. createApp + template string + setup(). Bundler yok. frontend-dev agent'ının sayfa scaffold'unda kullandığı skill.
---

# frontend-vue-page

npm'siz ortamda Vue 3 sayfasının doğru iskelesi.

## Layout

```
src/Frontend/
  index.html                 # shell + importmap + root app
  js/
    app.js                   # createApp + router
    api.js                   # fetch wrapper (bkz. frontend-api-client)
    pages/
      dashboard.js
      klines.js
      orders.js
    components/
      kline-chart.js
      toolbar.js
  css/
    style.css
```

## index.html Şablonu

```html
<!DOCTYPE html>
<html lang="tr">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>BinanceBot</title>
  <link rel="stylesheet" href="/css/style.css" />
</head>
<body>
  <div id="app"></div>

  <!-- importmap — tüm CDN bağımlılıkları burada (bkz. frontend-importmap skill) -->
  <script type="importmap">
    {
      "imports": {
        "vue": "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js"
      }
    }
  </script>

  <script type="module" src="/js/app.js"></script>
</body>
</html>
```

## app.js — Root App

```javascript
import { createApp, ref, computed, onMounted } from 'vue';
import { api } from './api.js';
import DashboardPage from './pages/dashboard.js';
import KlinesPage from './pages/klines.js';

const App = {
  template: `
    <div class="layout">
      <nav>
        <button @click="route = 'dashboard'">Dashboard</button>
        <button @click="route = 'klines'">Kline'lar</button>
      </nav>
      <main>
        <component :is="current" />
      </main>
    </div>
  `,
  setup() {
    const route = ref('dashboard');
    const current = computed(() => route.value === 'klines' ? KlinesPage : DashboardPage);
    return { route, current };
  }
};

createApp(App).mount('#app');
```

## Page Component Şablonu

```javascript
// src/Frontend/js/pages/dashboard.js
import { reactive, onMounted } from 'vue';
import { api } from '../api.js';

export default {
  name: 'DashboardPage',
  template: `
    <section class="dashboard">
      <h1>Genel Durum</h1>
      <div v-if="state.loading">Yükleniyor…</div>
      <div v-else-if="state.error" class="error">Hata: {{ state.error }}</div>
      <ul v-else>
        <li v-for="kline in state.klines" :key="kline.openTime">
          {{ kline.symbol }} @ {{ kline.close }}
        </li>
      </ul>
      <button @click="load">Yenile</button>
    </section>
  `,
  setup() {
    const state = reactive({ klines: [], loading: false, error: null });

    async function load() {
      state.loading = true;
      state.error = null;
      const r = await api.get('/api/klines?symbol=BTCUSDT&interval=1m&limit=20');
      if (r.ok) state.klines = r.data;
      else state.error = r.error;
      state.loading = false;
    }

    onMounted(load);
    return { state, load };
  }
};
```

## Kural

- **Tek default export** page component'i, başka export yok.
- Template string backtick içinde — Vue template syntax'ını kullanabilirsin (v-if, v-for, @click, :prop).
- `setup()` Composition API; `data`/`methods` yerine `reactive`/`ref`/`computed`.
- Component adı PascalCase (`DashboardPage`), dosya kebab-case (`dashboard.js`).
- Stil için `<style>` block YOK — `css/*.css` dosyaları link'le; component-scoped stil için class namespacing (`.dashboard-...`).

## Serve Etme (dev)

CDN module'ları CORS gereği `file://` protocol'den bazı tarayıcılarda çalışmaz. Dev'de:

```bash
# Basit static server — python ile
python -m http.server 5500 --directory src/Frontend
```

Veya `dotnet run` sırasında `app.UseStaticFiles()` ile backend üzerinden serve et.

## Kaynak

- https://vuejs.org/guide/quick-start.html#using-vue-from-cdn
- https://vuejs.org/guide/essentials/application.html
