// Stratejiler — kart grid + aktif/pasif toggle + son sinyaller.

import { createApp, ref, computed } from "vue";
import { api, getAdminKey } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";

const App = {
    components: { Sidebar, ErrorBanner },
    template: `
        <div class="app">
            <Sidebar active="strategies" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Stratejiler</h1>
                    <p class="page-sub">VWAP + EMA21 hibrit kısa vadeli stratejiler, aktif/pasif toggle ve son sinyal akışı.</p>
                </div>

                <ErrorBanner :error="listPoll.error.value" />
                <div v-if="actionError" class="alert">{{ actionError }}</div>

                <section class="block">
                    <h2 class="section-title">Stratejiler</h2>

                    <div v-if="!strategies" class="card-grid">
                        <div v-for="i in 3" :key="i" class="skeleton" style="height:200px; border-radius:16px"></div>
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

                                <button v-if="s.status === 'Active'"
                                        class="btn btn-sm btn-ghost"
                                        @click="toggle(s, false)"
                                        :disabled="busy === s.id">
                                    Duraklat
                                </button>
                                <button v-else
                                        class="btn btn-sm btn-good"
                                        @click="toggle(s, true)"
                                        :disabled="busy === s.id">
                                    Aktif Et
                                </button>
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
                                    <span class="sym-dot">{{ fmt.baseAsset(sig.symbol).slice(0,3) }}</span>
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
        const actionError = ref(null);
        const busy = ref(null);

        const strategies = computed(() => {
            const d = listPoll.data.value;
            return Array.isArray(d) ? d : null;
        });

        const signals = computed(() => {
            const d = signalsPoll.data.value;
            return Array.isArray(d) ? d : null;
        });

        function statusLabel(s) {
            const map = { Active: "AKTİF", Paused: "DURAKLAMIŞ", Draft: "TASLAK", Deactivated: "KAPALI" };
            return map[s] || s;
        }
        function statusBadge(s) {
            if (s === "Active") return "good";
            if (s === "Paused") return "warn";
            return "closed";
        }

        async function toggle(s, activate) {
            actionError.value = null;
            const key = getAdminKey({ promptMessage: "Admin key (strateji aç/kapa):" });
            if (!key) return;
            busy.value = s.id;
            try {
                const path = activate
                    ? `/api/strategies/${s.id}/activate`
                    : `/api/strategies/${s.id}/deactivate`;
                const body = activate ? undefined : { reason: "user_toggle" };
                await api.raw(path, {
                    method: "POST",
                    body,
                    headers: { "X-Admin-Key": key },
                });
                await listPoll.refresh();
            } catch (e) {
                actionError.value = `Strateji güncellenemedi: ${e.message || e}`;
            } finally {
                busy.value = null;
            }
        }

        return { listPoll, signalsPoll, strategies, signals, statusLabel, statusBadge, toggle, busy, actionError, fmt };
    },
};

createApp(App).mount("#app");
