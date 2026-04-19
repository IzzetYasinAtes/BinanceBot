// Loop 21 UI smoke — console errors + key elements.
// Run: npx playwright@1.47.2 node loops/loop_21/smoke-ui.mjs

// Dinamik playwright locator — npx cache'inden çöz (npm install yasak).
import { readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

function findPlaywright() {
    const cacheDir = "C:/Users/iyasi/AppData/Local/npm-cache/_npx";
    const dirs = readdirSync(cacheDir)
        .map(d => join(cacheDir, d, "node_modules", "playwright"))
        .filter(p => {
            try { return statSync(p).isDirectory(); } catch { return false; }
        });
    if (!dirs.length) throw new Error("playwright not found in npx cache");
    return pathToFileURL(join(dirs[0], "index.mjs")).href;
}

const pwUrl = findPlaywright();
const { chromium } = await import(pwUrl);

const BASE = "http://localhost:5188";
const PAGES = [
    { path: "/index.html",      label: "Ana Panel",        required: ["Ana Panel", "Toplam Net"] },
    { path: "/positions.html",  label: "Pozisyonlar",      required: ["Pozisyon"] },
    { path: "/orders.html",     label: "Emir Geçmişi",     required: ["Emir"] },
    { path: "/strategies.html", label: "Stratejiler",      required: ["Strateji"] },
    { path: "/risk.html",       label: "Risk",             required: ["Risk", "Drawdown"] },
    { path: "/klines.html",     label: "Mum Grafikleri",   required: ["Mum"] },
    { path: "/orderbook.html",  label: "Emir Defteri",     required: ["Emir Defteri"] },
    { path: "/logs.html",       label: "Sistem Olayları",  required: ["Sistem Olay"] },
];

// Mevcut chromium-1208 binary'sini executablePath ile göster (npx cache version mismatch workaround).
const pwBrowsersDir = "C:/Users/iyasi/AppData/Local/ms-playwright";
const candidatePaths = [
    `${pwBrowsersDir}/chromium-1208/chrome-win64/chrome.exe`,
    `${pwBrowsersDir}/chromium-1208/chrome-win/chrome.exe`,
    `${pwBrowsersDir}/chromium-1134/chrome-win64/chrome.exe`,
    `${pwBrowsersDir}/chromium-1134/chrome-win/chrome.exe`,
];
let execPath = null;
const { existsSync } = await import("node:fs");
for (const p of candidatePaths) {
    if (existsSync(p)) { execPath = p; break; }
}

const browser = await chromium.launch({
    headless: true,
    executablePath: execPath || undefined,
});
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await context.newPage();

let totalErrors = 0;
const results = [];

// Bir kez kur; per-page bağlam sahasına current{} üzerinden yönlendir.
const current = { errors: [], consoleErrors: [] };

page.on("console", (msg) => {
    if (msg.type() === "error") current.consoleErrors.push(msg.text());
});
page.on("pageerror", (err) => {
    current.errors.push(`[pageerror] ${err.message}`);
});
page.on("requestfailed", (req) => {
    current.errors.push(`[netfail] ${req.url()} ${req.failure()?.errorText || ""}`);
});
page.on("response", (res) => {
    if (res.status() >= 400) {
        current.errors.push(`[http${res.status()}] ${res.url()}`);
    }
});

for (const p of PAGES) {
    current.errors = [];
    current.consoleErrors = [];

    try {
        await page.goto(`${BASE}${p.path}`, { waitUntil: "networkidle", timeout: 15000 });
        await page.waitForTimeout(1800);
        const bodyText = await page.textContent("body");
        const missing = p.required.filter(r => !bodyText.includes(r));
        const errs = [...current.errors, ...current.consoleErrors];
        totalErrors += errs.length;
        results.push({ path: p.path, ok: errs.length === 0 && missing.length === 0, errs, missing });
    } catch (e) {
        totalErrors++;
        results.push({ path: p.path, ok: false, errs: [`[goto-fail] ${e.message}`], missing: [] });
    }
}

await browser.close();

console.log("\n=== Loop 21 UI Smoke ===\n");
for (const r of results) {
    const mark = r.ok ? "OK  " : "FAIL";
    console.log(`${mark}  ${r.path}`);
    if (r.errs.length) console.log(`      errors: ${r.errs.join(" | ")}`);
    if (r.missing.length) console.log(`      missing text: ${r.missing.join(", ")}`);
}
console.log(`\nTotal console/page errors: ${totalErrors}`);
process.exit(totalErrors > 0 ? 1 : 0);
