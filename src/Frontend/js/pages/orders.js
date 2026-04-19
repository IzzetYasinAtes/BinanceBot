// Emir Geçmişi — kart grid + status filtre.

import { createApp, ref, computed } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";

const STATUS_FILTERS = [
    { id: "all",       label: "Tümü" },
    { id: "filled",    label: "Gerçekleşen" },
    { id: "new",       label: "Bekleyen" },
    { id: "cancelled", label: "İptal" },
    { id: "rejected",  label: "Reddedilen" },
];

const App = {
    components: { Sidebar, ErrorBanner },
    template: `
        <div class="app">
            <Sidebar active="orders" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Emir Geçmişi</h1>
                    <p class="page-sub">Bot tarafından gönderilen tüm emirler — miktar, gerçekleşen fiyat ve durum.</p>
                </div>

                <ErrorBanner :error="poll.error.value" />

                <div class="block">
                    <div class="chip-group">
                        <button v-for="f in filters" :key="f.id"
                                class="chip" :class="{ active: active === f.id }"
                                @click="active = f.id">
                            {{ f.label }}
                            <span class="muted tiny">({{ countByFilter(f.id) }})</span>
                        </button>
                    </div>
                </div>

                <div v-if="!rows" class="card-grid">
                    <div v-for="i in 6" :key="i" class="skeleton" style="height:180px; border-radius:16px"></div>
                </div>

                <div v-else-if="visible.length === 0" class="empty-state">
                    <span class="emoji">∅</span>
                    Bu filtre için emir yok.
                </div>

                <div v-else class="card-grid-2">
                    <div v-for="o in visible" :key="o.clientOrderId" class="trade-card fade-in">
                        <div class="t-head">
                            <div class="trade-sym">
                                <span class="sym-dot">{{ fmt.baseAsset(o.symbol).slice(0,3) }}</span>
                                <span>{{ o.symbol }}</span>
                            </div>
                            <div class="row gap-2">
                                <span class="badge" :class="sideClass(o)">
                                    {{ o.side === 'Buy' ? 'ALIŞ' : 'SATIŞ' }}
                                </span>
                                <span class="badge" :class="statusBadge(o.status)">
                                    {{ statusLabel(o.status) }}
                                </span>
                            </div>
                        </div>

                        <div class="t-body">
                            <div class="kv">
                                <div class="k">Tip</div>
                                <div class="v">{{ o.type }} · {{ o.timeInForce }}</div>
                            </div>
                            <div class="kv">
                                <div class="k">Miktar</div>
                                <div class="v">{{ fmt.num4(o.quantity) }}</div>
                            </div>
                            <div class="kv">
                                <div class="k">Gerçekleşen</div>
                                <div class="v">{{ fmt.num4(o.executedQuantity) }}</div>
                            </div>
                            <div class="kv">
                                <div class="k">Ortalama Fiyat</div>
                                <div class="v">{{ avgPrice(o) }}</div>
                            </div>
                            <div class="kv">
                                <div class="k">Limit / Stop</div>
                                <div class="v">
                                    {{ o.price ? '$' + fmt.price(o.price) : '—' }}
                                    <span v-if="o.stopPrice" class="muted tiny"> · SL {{ '$' + fmt.price(o.stopPrice) }}</span>
                                </div>
                            </div>
                            <div class="kv">
                                <div class="k">Notional</div>
                                <div class="v">{{ fmt.money(o.cumulativeQuoteQty) }}</div>
                            </div>
                        </div>

                        <div class="t-foot">
                            <span class="muted tiny">{{ fmt.dateShort(o.createdAt) }}</span>
                            <span class="muted tiny mono">#{{ o.clientOrderId.slice(0, 12) }}</span>
                        </div>
                    </div>
                </div>
            </main>
        </div>
    `,
    setup() {
        const active = ref("all");
        const poll = usePolling(() => api.orders.history({ take: 100 }), 10000);

        const rows = computed(() => {
            const d = poll.data.value;
            if (!d) return null;
            if (Array.isArray(d)) return d;
            if (d.items && Array.isArray(d.items)) return d.items;
            return [];
        });

        function matches(o, f) {
            if (f === "all") return true;
            const s = String(o.status || "").toLowerCase();
            return s.includes(f);
        }

        const visible = computed(() => {
            if (!rows.value) return [];
            return rows.value.filter(o => matches(o, active.value));
        });

        function countByFilter(f) {
            if (!rows.value) return 0;
            return rows.value.filter(o => matches(o, f)).length;
        }

        function sideClass(o) {
            return o.side === "Buy" ? "up" : "down";
        }

        function statusLabel(s) {
            const map = {
                Filled: "GERÇEKLEŞTİ",
                PartiallyFilled: "KISMİ",
                New: "BEKLİYOR",
                Cancelled: "İPTAL",
                Canceled: "İPTAL",
                Rejected: "RED",
                Expired: "ZAMANAŞIMI",
            };
            return map[s] || s;
        }

        function statusBadge(s) {
            const k = String(s || "").toLowerCase();
            if (k === "filled")       return "good";
            if (k === "partiallyfilled") return "warn";
            if (k === "new")          return "info";
            if (k === "cancelled" || k === "canceled") return "closed";
            if (k === "rejected" || k === "expired") return "bad";
            return "";
        }

        function avgPrice(o) {
            const q = Number(o.executedQuantity || 0);
            const nq = Number(o.cumulativeQuoteQty || 0);
            if (q <= 0 || nq <= 0) return "—";
            return "$" + fmt.price(nq / q);
        }

        return {
            active, filters: STATUS_FILTERS, poll, rows, visible,
            countByFilter, sideClass, statusLabel, statusBadge, avgPrice, fmt,
        };
    },
};

createApp(App).mount("#app");
