// Sistem Olayları — severity filter + timeline.

import { createApp, ref, computed } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";

const FILTERS = [
    { id: "",        label: "Tümü" },
    { id: "Info",    label: "Bilgi" },
    { id: "Warning", label: "Uyarı" },
    { id: "Error",   label: "Hata" },
];

function severityCls(s) {
    const k = String(s || "").toLowerCase();
    if (k === "error" || k === "critical") return "error";
    if (k === "warning" || k === "warn")   return "warn";
    if (k === "info" || k === "information") return "info";
    return "info";
}

function severityLabel(s) {
    const map = { Info: "BİLGİ", Information: "BİLGİ", Warning: "UYARI", Error: "HATA", Critical: "KRİTİK" };
    return map[s] || s || "";
}

function severityIcon(s) {
    const c = severityCls(s);
    if (c === "error") return "!";
    if (c === "warn")  return "▲";
    if (c === "info")  return "i";
    return "·";
}

const App = {
    components: { Sidebar, ErrorBanner },
    template: `
        <div class="app">
            <Sidebar active="logs" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Sistem Olayları</h1>
                    <p class="page-sub">Bot tarafından yayılan olay akışı — bilgi, uyarı ve hata kayıtları zaman sıralı.</p>
                </div>

                <ErrorBanner :error="poll.error.value" />

                <section class="block">
                    <div class="chip-group">
                        <button v-for="f in filters" :key="f.id"
                                class="chip" :class="{ active: level === f.id }"
                                @click="level = f.id">
                            {{ f.label }}
                        </button>
                    </div>
                </section>

                <div class="card card-static">
                    <div v-if="!events" class="col gap-3">
                        <div v-for="i in 6" :key="i" class="skeleton" style="height:40px"></div>
                    </div>
                    <div v-else-if="events.length === 0" class="empty-state">
                        <span class="emoji">·</span>
                        Bu filtreyle olay yok.
                    </div>
                    <div v-else class="timeline">
                        <div v-for="e in events" :key="e.id" class="tl-item fade-in">
                            <div class="tl-icon" :class="severityCls(e.severity)">{{ severityIcon(e.severity) }}</div>
                            <div class="tl-time">
                                {{ fmt.timeHms(e.occurredAt) }}
                                <div class="muted tiny">{{ fmt.dateShort(e.occurredAt) }}</div>
                            </div>
                            <div>
                                <div class="tl-type">
                                    {{ e.eventType }}
                                    <span class="muted small"> · {{ e.source }}</span>
                                </div>
                                <div class="tl-msg">{{ payloadSummary(e.payloadJson) }}</div>
                            </div>
                            <div>
                                <span class="badge" :class="severityCls(e.severity) === 'error' ? 'bad'
                                            : severityCls(e.severity) === 'warn' ? 'warn' : 'info'">
                                    {{ severityLabel(e.severity) }}
                                </span>
                            </div>
                        </div>
                    </div>
                </div>
            </main>
        </div>
    `,
    setup() {
        const level = ref("");
        const poll = usePolling(() => api.systemEvents.tail(undefined, level.value || undefined, 80), 4000);

        const events = computed(() => {
            const d = poll.data.value;
            if (!d) return null;
            const items = Array.isArray(d.items) ? d.items : Array.isArray(d) ? d : [];
            return items.slice().sort((a, b) =>
                new Date(b.occurredAt) - new Date(a.occurredAt));
        });

        // payload json'ı kısa bir okunabilir özete çevir
        function payloadSummary(raw) {
            if (!raw) return "";
            if (typeof raw !== "string") raw = JSON.stringify(raw);
            try {
                const o = JSON.parse(raw);
                if (typeof o === "string") return o;
                const interestingKeys = ["message", "Message", "reason", "Reason", "symbol", "Symbol", "error", "Error"];
                for (const k of interestingKeys) {
                    if (o && typeof o === "object" && k in o && o[k]) return String(o[k]);
                }
                // key=value özeti (max 6 çift)
                const kvs = [];
                if (o && typeof o === "object") {
                    for (const k of Object.keys(o).slice(0, 6)) {
                        const v = o[k];
                        if (v == null || typeof v === "object") continue;
                        kvs.push(`${k}=${v}`);
                    }
                }
                return kvs.length ? kvs.join(" · ") : raw.slice(0, 160);
            } catch {
                return String(raw).slice(0, 160);
            }
        }

        return {
            level, filters: FILTERS, poll, events,
            severityCls, severityLabel, severityIcon,
            payloadSummary, fmt,
        };
    },
};

createApp(App).mount("#app");
