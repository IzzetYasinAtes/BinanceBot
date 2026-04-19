// Sistem Olayları — severity filter + timeline.

import { createApp, ref, computed } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";

// Chip id -> severity string eşlemesi. 'all' → filter yok.
const FILTERS = [
    { id: "all",   label: "Tümü" },
    { id: "info",  label: "Bilgi" },
    { id: "warn",  label: "Uyarı" },
    { id: "error", label: "Hata" },
];

// API severity değerlerini chip id'sine normalize et.
function sevToFilterId(s) {
    const k = String(s || "").toLowerCase();
    if (k === "error" || k === "critical") return "error";
    if (k === "warning" || k === "warn")   return "warn";
    if (k === "info" || k === "information") return "info";
    return "info";
}

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
                                class="chip" :class="{ active: filter === f.id }"
                                @click="filter = f.id">
                            {{ f.label }}
                            <span class="muted tiny">({{ counts[f.id] ?? 0 }})</span>
                        </button>
                    </div>
                </section>

                <div class="card card-static">
                    <div v-if="!events" class="col gap-3">
                        <div v-for="i in 6" :key="i" class="skeleton" style="height:40px"></div>
                    </div>
                    <div v-else-if="filtered.length === 0" class="empty-illust">
                        <svg width="120" height="92" viewBox="0 0 120 92" fill="none" xmlns="http://www.w3.org/2000/svg">
                            <defs>
                                <linearGradient id="logsGrad" x1="0" y1="0" x2="1" y2="1">
                                    <stop offset="0%" stop-color="#06b6d4" stop-opacity="0.35" />
                                    <stop offset="100%" stop-color="#6366f1" stop-opacity="0.18" />
                                </linearGradient>
                            </defs>
                            <rect x="12" y="14" width="96" height="68" rx="8" fill="url(#logsGrad)" opacity="0.35" />
                            <rect x="22" y="24" width="60" height="5" rx="2.5" fill="#64748b" opacity="0.6" />
                            <rect x="22" y="34" width="44" height="5" rx="2.5" fill="#64748b" opacity="0.45" />
                            <rect x="22" y="44" width="72" height="5" rx="2.5" fill="#64748b" opacity="0.35" />
                            <rect x="22" y="54" width="52" height="5" rx="2.5" fill="#64748b" opacity="0.25" />
                            <rect x="22" y="64" width="66" height="5" rx="2.5" fill="#64748b" opacity="0.15" />
                            <circle cx="86" cy="26" r="3" fill="#06b6d4" opacity="0.9" />
                            <circle cx="86" cy="26" r="6" fill="#06b6d4" opacity="0.25" />
                        </svg>
                        <div class="title">{{ emptyTitle }}</div>
                        <div class="sub">
                            {{ emptySub }}
                        </div>
                        <div class="hint-row">
                            <span class="pulse-dot"></span>
                            <span>Akış dinleniyor · her 4 sn yenileniyor</span>
                        </div>
                    </div>
                    <div v-else class="timeline">
                        <div v-for="e in filtered" :key="e.id" class="tl-item fade-in">
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
        const filter = ref("all"); // all | info | warn | error
        // Client-side filter — sunucudan hep 'all' çek, chip'leri reactive uygula.
        const poll = usePolling(() => api.systemEvents.tail(undefined, undefined, 120), 4000);

        const events = computed(() => {
            const d = poll.data.value;
            if (!d) return null;
            const items = Array.isArray(d.items) ? d.items : Array.isArray(d) ? d : [];
            return items.slice().sort((a, b) =>
                new Date(b.occurredAt) - new Date(a.occurredAt));
        });

        const filtered = computed(() => {
            const list = events.value;
            if (!Array.isArray(list)) return [];
            if (filter.value === "all") return list;
            return list.filter(e => sevToFilterId(e.severity) === filter.value);
        });

        const counts = computed(() => {
            const out = { all: 0, info: 0, warn: 0, error: 0 };
            for (const e of (events.value || [])) {
                out.all++;
                const id = sevToFilterId(e.severity);
                if (out[id] != null) out[id]++;
            }
            return out;
        });

        const emptyTitle = computed(() => {
            if (filter.value === "warn")  return "Uyarı yok";
            if (filter.value === "error") return "Hata yok";
            if (filter.value === "info")  return "Bilgi olayı yok";
            return "Henüz sistem olayı yok";
        });
        const emptySub = computed(() => {
            if (filter.value !== "all") {
                return "Filtreyi temizleyerek tüm olay akışını görebilirsin.";
            }
            return "API çalışıyor ve olaylar kaydediliyor. Bot bir şey yaptığında (sinyal, emir, hata) burada akış olarak görünecek.";
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
            filter, filters: FILTERS, poll, events, filtered, counts,
            emptyTitle, emptySub,
            severityCls, severityLabel, severityIcon,
            payloadSummary, fmt,
        };
    },
};

createApp(App).mount("#app");
