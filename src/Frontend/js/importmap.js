// Shared importmap reference — included inline in each HTML to avoid duplication.
// CDN pins (check vendor fallback in §3 frontend-design.md if blocked).
export const importmap = {
    imports: {
        "vue": "https://unpkg.com/vue@3.5.13/dist/vue.esm-browser.prod.js",
        "chart.js/auto": "https://cdn.jsdelivr.net/npm/chart.js@4.4.7/+esm",
    },
};
