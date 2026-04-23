// Stratejiler — readonly durum + son sinyaller (Loop 30: aç/kapa panel kaldırıldı).
// Stratejiler `src/Api/appsettings.json` Seeds dizisinden kod seviyesinde yönetiliyor.

import { createApp, computed } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";
import { SymbolLogo } from "../components/symbolLogo.js";

const App = {
    components: { Sidebar, ErrorBanner, SymbolLogo },
    template: `
        <div class="app">
            <Sidebar active="strategies" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Stratejiler</h1>
                    <p class="page-sub">Stratejiler kod seviyesinden yönetiliyor. Aç/kapa için <code>src/Api/appsettings.json</code> Seeds dizisinden <code>Activate</code> alanını değiştirin.</p>
                </div>

                <ErrorBanner :error="listPoll.error.value" />

                <section class="block">
                    <h2 class="section-title">Tanımlı Stratejiler</h2>

                    <div v-if="!strategies" class="card-grid">
                        <div v-for="i in 3" :key="i" class="skeleton" style="height:180px; border-radius:16px"></div>
                    </div>

                    <div v-else-if="strategies.length === 0" class="empty-state">
                        <span class="emoji">∅</span>
                        Henüz strateji tanımlı değil.
                    </div>

                    <div v-else class="card-grid-2">
                        <div v-for="s in strategies" :key="s.id" class="trade-card fade-in"
                             :class="s.status === 'Active' ? 'card-good' : ''">
                            <div class="t-head">
                                <div class="trade-sym">
                                    <span class="sym-dot">{{ s.name.slice(0, 3).toUpperCase() }}</span>
                                    <span>{{ s.name }}</span>
                                </div>
                                <span class="badge" :class="statusBadge(s.status)">
                                    {{ statusLabel(s.status) }}
                                </span>
                            </div>

                            <div class="t-body">
                                <div class="kv">
                                    <div class="k">Tip</div>
                                    <div class="v">{{ s.type }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Oluşturuldu</div>
                                    <div class="v">{{ fmt.dateShort(s.createdAt) }}</div>
                                </div>
                            </div>

                            <div class="kv">
                                <div class="k">Semboller</div>
                                <div class="chip-group mt-2">
                                    <span v-for="sym in s.symbols" :key="sym" class="chip" style="cursor:default;">
                                        {{ sym }}
                                    </span>
                                </div>
                            </div>

                            <div class="t-foot">
                                <span class="muted tiny" v-if="s.activatedAt">
                                    Aktif: {{ fmt.dateShort(s.activatedAt) }}
                                </span>
                                <span class="muted tiny" v-else>Hiç aktifleştirilmemiş</span>
                                <span class="muted tiny">Kod yönetimli (readonly)</span>
                            </div>
                        </div>
                    </div>
                </section>

                <section class="block">
                    <h2 class="section-title">
                        Son Sinyaller
                        <span class="tools muted tiny">son 12 sinyal</span>
                    </h2>

                    <div v-if="!signals" class="card-grid">
                        <div v-for="i in 3" :key="i" class="skeleton" style="height:100px; border-radius:16px"></div>
                    </div>
                    <div v-else-if="signals.length === 0" class="empty-state">
                        <span class="emoji">·</span>
                        Bekleniyor — henüz sinyal üretilmedi.
                    </div>
                    <div v-else class="card-grid-2">
                        <div v-for="sig in signals" :key="sig.id" class="trade-card fade-in card-tight">
                            <div class="t-head">
                                <div class="trade-sym">
                                    <SymbolLogo :symbol="sig.symbol" :size="28" />
                                    <span>{{ sig.symbol }}</span>
                                </div>
                                <span class="badge" :class="sig.direction === 'Long' ? 'up' : 'down'">
                                    {{ sig.direction === 'Long' ? 'LONG' : 'SHORT' }}
                                </span>
                            </div>
                            <div class="t-body">
                                <div class="kv">
                                    <div class="k">Miktar</div>
                                    <div class="v">{{ fmt.num4(sig.suggestedQuantity) }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Teklif Fiyat</div>
                                    <div class="v">{{ sig.suggestedPrice ? fmt.price(sig.suggestedPrice) : '—' }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Stop</div>
                                    <div class="v">{{ sig.suggestedStopPrice ? fmt.price(sig.suggestedStopPrice) : '—' }}</div>
                                </div>
                                <div class="kv">
                                    <div class="k">Zaman</div>
                                    <div class="v">{{ fmt.timeHm(sig.emittedAt) }}</div>
                                </div>
                            </div>
                        </div>
                    </div>
                </section>
            </main>
        </div>
    `,
    setup() {
        const listPoll    = usePolling(() => api.strategies.list(), 10000);
        const signalsPoll = usePolling(() => api.strategies.latestSignals(12), 8000);

        const strategies = computed(() => {
            const d = listPoll.data.value;
            return Array.isArray(d) ? d : null;
        });

        const signals = computed(() => {
            const d = signalsPoll.data.value;
            return Array.isArray(d) ? d : null;
        });

        function statusLabel(s) {
            const map = { Active: "AKTİF", Paused: "PASİF", Draft: "TASLAK", Deactivated: "KAPALI" };
            return map[s] || s;
        }
        function statusBadge(s) {
            if (s === "Active") return "good";
            if (s === "Paused") return "warn";
            return "closed";
        }

        return { listPoll, signalsPoll, strategies, signals, statusLabel, statusBadge, fmt };
    },
};

createApp(App).mount("#app");
