// Numerik counter — requestAnimationFrame ease-out geçişi.
// Vue 3 CDN uyumlu; prop değişiminde otomatik re-animate.
//
// Kullanım:
//   <animated-number :value="summary.netPnl" :decimals="2" prefix="$" />
//   <animated-number :value="count" :decimals="0" :duration-ms="800" />

import { ref, watch, onBeforeUnmount } from "vue";

// easeOutCubic — yumuşak sonlanma.
function easeOutCubic(t) {
    return 1 - Math.pow(1 - t, 3);
}

export const AnimatedNumber = {
    template: `<span class="animated-number tabular-nums">{{ display }}</span>`,
    props: {
        value: { type: [Number, String], default: 0 },
        decimals: { type: Number, default: 2 },
        prefix: { type: String, default: "" },
        suffix: { type: String, default: "" },
        // negatif dahil işaretli göster: "+" prefix'i otomatik eklenir.
        signed: { type: Boolean, default: false },
        // grup ayıracı (binlik) — tr-TR locale kullan.
        group: { type: Boolean, default: true },
        durationMs: { type: Number, default: 700 },
    },
    setup(props) {
        const display = ref(formatValue(Number(props.value) || 0, props));
        let rafId = null;
        let fromVal = Number(props.value) || 0;
        let toVal = fromVal;
        let startTs = 0;

        function stopRaf() {
            if (rafId != null) {
                cancelAnimationFrame(rafId);
                rafId = null;
            }
        }

        function step(ts) {
            if (!startTs) startTs = ts;
            const elapsed = ts - startTs;
            const dur = Math.max(1, props.durationMs);
            const t = Math.min(1, elapsed / dur);
            const eased = easeOutCubic(t);
            const cur = fromVal + (toVal - fromVal) * eased;
            display.value = formatValue(cur, props);
            if (t < 1) {
                rafId = requestAnimationFrame(step);
            } else {
                display.value = formatValue(toVal, props);
                rafId = null;
            }
        }

        function animateTo(target) {
            const n = Number(target);
            if (!isFinite(n)) {
                display.value = formatValue(0, props);
                fromVal = 0;
                toVal = 0;
                return;
            }
            stopRaf();
            // başlangıç = mevcut display sayısı (parse), hedef = yeni değer.
            fromVal = parseDisplay(display.value);
            if (!isFinite(fromVal)) fromVal = 0;
            toVal = n;
            if (Math.abs(toVal - fromVal) < Math.pow(10, -(props.decimals + 1))) {
                display.value = formatValue(toVal, props);
                return;
            }
            startTs = 0;
            rafId = requestAnimationFrame(step);
        }

        watch(() => props.value, (nv) => animateTo(nv));

        onBeforeUnmount(stopRaf);

        // ilk mount'ta da küçük bir animasyon güzel hissettiriyor; 0'dan hedefe.
        animateTo(props.value);

        return { display };
    },
};

function formatValue(n, props) {
    if (!isFinite(n)) n = 0;
    const abs = Math.abs(n);
    const fixed = abs.toFixed(Math.max(0, props.decimals));
    const [int, frac] = fixed.split(".");
    const grouped = props.group ? groupDigits(int) : int;
    const body = frac ? `${grouped},${frac}` : grouped;
    const sign = n < 0 ? "-" : (props.signed ? "+" : "");
    return `${sign}${props.prefix}${body}${props.suffix}`;
}

function groupDigits(intStr) {
    // tr-TR binlik ayırıcı: nokta.
    return intStr.replace(/\B(?=(\d{3})+(?!\d))/g, ".");
}

function parseDisplay(s) {
    if (typeof s !== "string") return Number(s) || 0;
    // prefix/suffix çıkar, grup ayırıcı noktaları sil, ondalık virgülü noktaya çevir.
    const cleaned = s.replace(/[^\d\-,\.]/g, "");
    // "1.234,56" -> "1234.56"; "1234.56" -> önce . -> '' sonra , -> .
    // heuristic: en az bir virgül varsa, virgül ondalık.
    let normalized;
    if (cleaned.includes(",")) {
        normalized = cleaned.replace(/\./g, "").replace(",", ".");
    } else {
        normalized = cleaned;
    }
    const n = parseFloat(normalized);
    return isFinite(n) ? n : 0;
}
