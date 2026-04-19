// Risk Ayarları — form kart + kill switch + canlı durum.

import { createApp, ref, computed, watch } from "vue";
import { api, getAdminKey } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";

const App = {
    components: { Sidebar, ErrorBanner },
    template: `
        <div class="app">
            <Sidebar active="risk" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Risk Ayarları</h1>
                    <p class="page-sub">Pozisyon başı risk, açık limit ve drawdown sınırları. Acil durdurma için kill switch.</p>
                </div>

                <ErrorBanner :error="poll.error.value" />
                <div v-if="saveError" class="alert">{{ saveError }}</div>

                <!-- Anlık durum -->
                <section class="block">
                    <h2 class="section-title">Anlık Durum</h2>
                    <div class="kpi-grid" v-if="profile">
                        <div class="card card-static">
                            <div class="card-head"><h3 class="card-title">Mevcut Drawdown</h3></div>
                            <div class="card-value" :class="ddClass">
                                {{ fmt.pctFracSigned(-Math.abs(profile.currentDrawdownPct)) }}
                            </div>
                            <div class="card-hint">24h limit %{{ fmt.num2(profile.maxDrawdown24hPct * 100) }}</div>
                        </div>
                        <div class="card card-static">
                            <div class="card-head"><h3 class="card-title">Zirve Özkaynak</h3></div>
                            <div class="card-value">{{ fmt.money(profile.peakEquity) }}</div>
                            <div class="card-hint">Tarihi en yüksek equity</div>
                        </div>
                        <div class="card card-static">
                            <div class="card-head"><h3 class="card-title">Üst Üste Zarar</h3></div>
                            <div class="card-value" :class="consLossClass">
                                {{ profile.consecutiveLosses }}
                            </div>
                            <div class="card-hint">Limit {{ profile.maxConsecutiveLosses }}</div>
                        </div>
                        <div class="card card-static" :class="cbCard">
                            <div class="card-head"><h3 class="card-title">Devre Kesici</h3></div>
                            <div class="card-value">
                                <span class="badge" :class="cbBadge">{{ cbLabel }}</span>
                            </div>
                            <div class="card-hint">{{ cb?.reason || '—' }}</div>
                        </div>
                    </div>
                    <div v-else class="kpi-grid">
                        <div v-for="i in 4" :key="i" class="skeleton" style="height:130px; border-radius:16px"></div>
                    </div>
                </section>

                <!-- Form -->
                <section class="block" v-if="profile">
                    <h2 class="section-title">Limitleri Düzenle</h2>
                    <div class="card">
                        <div class="kpi-grid">
                            <div class="field">
                                <label>İşlem Başı Risk (%)</label>
                                <input class="input" type="number" step="0.01" v-model.number="form.riskPerTradePct" />
                                <span class="muted tiny">Her pozisyonda tehlikeye atılabilecek equity oranı.</span>
                            </div>
                            <div class="field">
                                <label>Pozisyon Başı Üst Limit (%)</label>
                                <input class="input" type="number" step="0.01" v-model.number="form.maxPositionSizePct" />
                                <span class="muted tiny">Tek pozisyonun maksimum notional büyüklüğü.</span>
                            </div>
                            <div class="field">
                                <label>Max DD 24h (%)</label>
                                <input class="input" type="number" step="0.01" v-model.number="form.maxDrawdown24hPct" />
                                <span class="muted tiny">24 saatlik kayıp tavanı.</span>
                            </div>
                            <div class="field">
                                <label>Max DD (All-Time) (%)</label>
                                <input class="input" type="number" step="0.01" v-model.number="form.maxDrawdownAllTimePct" />
                                <span class="muted tiny">Tarihi equity zirvesinden düşüş limiti.</span>
                            </div>
                            <div class="field">
                                <label>Max Üst Üste Zarar</label>
                                <input class="input" type="number" step="1" v-model.number="form.maxConsecutiveLosses" />
                                <span class="muted tiny">Aşılırsa kesici tetiklenir.</span>
                            </div>
                            <div class="field">
                                <label>Max Açık Pozisyon</label>
                                <input class="input" type="number" step="1" v-model.number="form.maxOpenPositions" />
                                <span class="muted tiny">Eş zamanlı pozisyon üst limiti.</span>
                            </div>
                        </div>
                        <div class="row mt-4" style="justify-content:flex-end;">
                            <button class="btn btn-ghost" @click="resetForm" :disabled="saving">Sıfırla</button>
                            <button class="btn btn-primary" @click="save" :disabled="saving">
                                {{ saving ? 'Kaydediliyor…' : 'Kaydet' }}
                            </button>
                        </div>
                    </div>
                </section>

                <!-- Kill switch -->
                <section class="block">
                    <h2 class="section-title">Acil Durdurma</h2>
                    <p class="muted small mb-4">
                        Devre kesici açıkken yeni pozisyon alınmaz. Tetiklenmişse operatör notu ile sıfırlanabilir.
                    </p>
                    <button class="kill-switch" @click="resetCb" :disabled="resetting">
                        {{ resetting ? 'İşleniyor…' : (cbTripped ? 'Devre Kesiciyi Sıfırla' : 'Manuel Durdurma (yakında)') }}
                    </button>
                </section>

            </main>
        </div>
    `,
    setup() {
        const poll   = usePolling(() => api.risk.profile(), 5000);
        const cbPoll = usePolling(() => api.risk.circuitBreaker(), 5000);

        const profile = computed(() => poll.data.value || null);
        const cb      = computed(() => cbPoll.data.value || null);

        const form = ref({
            riskPerTradePct: 0,
            maxPositionSizePct: 0,
            maxDrawdown24hPct: 0,
            maxDrawdownAllTimePct: 0,
            maxConsecutiveLosses: 0,
            maxOpenPositions: 0,
        });

        const saving = ref(false);
        const resetting = ref(false);
        const saveError = ref(null);

        function loadFormFromProfile(p) {
            if (!p) return;
            form.value = {
                riskPerTradePct: Number(p.riskPerTradePct || 0),
                maxPositionSizePct: Number(p.maxPositionSizePct || 0),
                maxDrawdown24hPct: Number(p.maxDrawdown24hPct || 0),
                maxDrawdownAllTimePct: Number(p.maxDrawdownAllTimePct || 0),
                maxConsecutiveLosses: Number(p.maxConsecutiveLosses || 0),
                maxOpenPositions: Number(p.maxOpenPositions || 0),
            };
        }
        watch(profile, (p) => { if (p) loadFormFromProfile(p); }, { immediate: true });

        function resetForm() { loadFormFromProfile(profile.value); }

        async function save() {
            saveError.value = null;
            const key = getAdminKey({ promptMessage: "Admin key (risk profili güncelle):" });
            if (!key) return;
            saving.value = true;
            try {
                await api.raw("/api/risk/profile", {
                    method: "PUT",
                    body: { ...form.value },
                    headers: { "X-Admin-Key": key },
                });
                await poll.refresh();
            } catch (e) {
                saveError.value = `Kayıt başarısız: ${e.message || e}`;
            } finally {
                saving.value = false;
            }
        }

        const cbTripped = computed(() => cb.value?.status === "Tripped");
        const cbLabel = computed(() => {
            const s = cb.value?.status;
            if (s === "Tripped")  return "KAPALI";
            if (s === "Armed" || s === "Active") return "AKTİF";
            return s || "—";
        });
        const cbBadge = computed(() => cbTripped.value ? "bad" : "good");
        const cbCard  = computed(() => cbTripped.value ? "card-bad" : "card-good");

        async function resetCb() {
            saveError.value = null;
            if (!cbTripped.value) return; // trip değilse no-op
            const key = getAdminKey({ promptMessage: "Admin key (kesici sıfırla):" });
            if (!key) return;
            resetting.value = true;
            try {
                await api.raw("/api/risk/circuit-breaker/reset", {
                    method: "POST",
                    body: { adminNote: "ui_reset" },
                    headers: { "X-Admin-Key": key },
                });
                await cbPoll.refresh();
            } catch (e) {
                saveError.value = `Sıfırlama başarısız: ${e.message || e}`;
            } finally {
                resetting.value = false;
            }
        }

        const ddClass = computed(() => {
            const v = profile.value?.currentDrawdownPct;
            if (v == null) return "";
            return v > 0 ? "metric-bad" : "metric-neutral";
        });
        const consLossClass = computed(() => {
            const p = profile.value;
            if (!p) return "";
            const r = p.consecutiveLosses / Math.max(1, p.maxConsecutiveLosses);
            if (r >= 1) return "metric-bad";
            if (r >= 0.7) return "metric-warn";
            return "metric-neutral";
        });

        return {
            poll, cbPoll, profile, cb,
            form, saving, resetting, saveError,
            resetForm, save, resetCb,
            cbTripped, cbLabel, cbBadge, cbCard,
            ddClass, consLossClass, fmt,
        };
    },
};

createApp(App).mount("#app");
