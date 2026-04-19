// SymbolLogo — reusable coin logo image with safe fallback.
// Prop: symbol ("BTCUSDT" | "BTC"), size (px).
// Known bases: BTC, ETH, BNB, XRP — fallback: gradient dot with 3-letter text.

import { ref, computed } from "vue";

const KNOWN = new Set(["btc", "eth", "bnb", "xrp"]);

export const SymbolLogo = {
    name: "SymbolLogo",
    props: {
        symbol: { type: String, required: true },
        size:   { type: Number, default: 28 },
    },
    setup(props) {
        const errored = ref(false);

        const base = computed(() => {
            const s = String(props.symbol || "").toUpperCase();
            // strip common quote suffixes
            return s
                .replace(/USDT$/, "")
                .replace(/BUSD$/, "")
                .replace(/USDC$/, "")
                .replace(/USD$/,  "")
                .toLowerCase();
        });

        const src = computed(() => `/assets/logos/${base.value}.svg`);

        const hasLogo = computed(() => KNOWN.has(base.value) && !errored.value);

        const fallbackText = computed(() => base.value.toUpperCase().slice(0, 3));

        function onErr() { errored.value = true; }

        return { base, src, hasLogo, fallbackText, onErr };
    },
    template: `
        <img v-if="hasLogo"
             :src="src"
             :alt="base.toUpperCase()"
             :width="size"
             :height="size"
             class="sym-logo"
             @error="onErr" />
        <span v-else
              class="sym-dot"
              :style="{ width: size + 'px', height: size + 'px', lineHeight: size + 'px' }">
            {{ fallbackText }}
        </span>
    `,
};
