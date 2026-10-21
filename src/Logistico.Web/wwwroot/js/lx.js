// Browser helpers for Logistico: theme, clipboard, focus, multi-hub WebAudio cues, print, and USB/Bluetooth barcode wedge capture.
(() => {
    try {
        const origFetch = window.fetch;
        if (typeof origFetch === "function") {
            window.fetch = function (input, init) {
                const url = typeof input === "string" ? input : input?.url || "";
                if (url.includes("/_framework/") && init && init.cache === "force-cache") {
                    init = { ...init, cache: "no-cache" };
                }
                return origFetch.call(this, input, init);
            };
        }
        if (window.caches && typeof window.caches.keys === "function") {
            window.caches.keys().then((keys) => {
                for (const k of keys) {
                    if (k.startsWith("dotnet-resources") || k.startsWith("blazor-resources")) {
                        window.caches.delete(k);
                    }
                }
            }).catch(() => {});
        }
    } catch { /* ignore */ }
})();

window.lx = {
    culture: () => document.documentElement.lang || "ar-IQ",
    // Dark is the default look (an operations console); people who pick light keep it.
    isDark: () => { try { return localStorage.getItem("lx.theme") !== "light"; } catch { return true; } },
    applyTheme: () => { if (window.lx.isDark()) document.body.classList.add("bp6-dark"); },
    setDark: (on) => {
        try { localStorage.setItem("lx.theme", on ? "dark" : "light"); } catch { /* private mode */ }
        document.body.classList.toggle("bp6-dark", on);
    },
    isMuted: () => { try { return localStorage.getItem("lx.muted") === "true"; } catch { return false; } },
    setMuted: (muted) => {
        try { localStorage.setItem("lx.muted", muted ? "true" : "false"); } catch { /* private mode */ }
    },
    copy: async (text) => {
        try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
    },
    focus: (id) => {
        const el = document.getElementById(id);
        if (el) { el.focus(); if (el.select) el.select(); }
    },
    scrollIntoView: (id) => {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ block: "nearest", inline: "nearest" });
    },
    print: () => {
        try { window.print(); } catch { /* ignore */ }
    },
    // Local scanner tone: high = OK (1046Hz), double-low = error (220Hz -> 180Hz).
    beep: (ok) => {
        if (window.lx.isMuted()) return;
        try {
            const ctx = window.lx._audio ??= new (window.AudioContext || window.webkitAudioContext)();
            if (ctx.state === "suspended") ctx.resume();
            const now = ctx.currentTime;
            if (ok) {
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = "sine";
                osc.frequency.setValueAtTime(1046.5, now);
                gain.gain.setValueAtTime(0.09, now);
                gain.gain.exponentialRampToValueAtTime(0.001, now + 0.12);
                osc.connect(gain).connect(ctx.destination);
                osc.start(now);
                osc.stop(now + 0.12);
            } else {
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = "sawtooth";
                osc.frequency.setValueAtTime(220, now);
                osc.frequency.setValueAtTime(175, now + 0.16);
                gain.gain.setValueAtTime(0.11, now);
                gain.gain.exponentialRampToValueAtTime(0.001, now + 0.36);
                osc.connect(gain).connect(ctx.destination);
                osc.start(now);
                osc.stop(now + 0.36);
            }
        } catch { /* audio not allowed */ }
    },
    // Remote warehouse radar chime (e.g. Basra Hub scans -> Baghdad HQ hears sonar ping).
    remoteBeep: (ok, isMisrouted) => {
        if (window.lx.isMuted()) return;
        try {
            const ctx = window.lx._audio ??= new (window.AudioContext || window.webkitAudioContext)();
            if (ctx.state === "suspended") ctx.resume();
            const now = ctx.currentTime;
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();
            if (!ok || isMisrouted) {
                osc.type = "triangle";
                osc.frequency.setValueAtTime(587.33, now);
                osc.frequency.setValueAtTime(440.0, now + 0.11);
                osc.frequency.setValueAtTime(329.63, now + 0.22);
                gain.gain.setValueAtTime(0.1, now);
                gain.gain.exponentialRampToValueAtTime(0.001, now + 0.36);
                osc.connect(gain).connect(ctx.destination);
                osc.start(now);
                osc.stop(now + 0.36);
            } else {
                osc.type = "sine";
                osc.frequency.setValueAtTime(880.0, now);
                osc.frequency.setValueAtTime(1318.5, now + 0.09);
                gain.gain.setValueAtTime(0.08, now);
                gain.gain.exponentialRampToValueAtTime(0.001, now + 0.24);
                osc.connect(gain).connect(ctx.destination);
                osc.start(now);
                osc.stop(now + 0.24);
            }
        } catch { /* audio not allowed */ }
    },
    // Places a popover next to its trigger with fixed positioning: aligned to the trigger's inline end (or start),
    // below it (or above when there is no room), kept inside the viewport. Works the same in Arabic and English.
    place: (trigger, pop, align) => {
        if (!trigger || !pop) return;
        try {
            const r = trigger.getBoundingClientRect();
            const rtl = document.documentElement.dir === "rtl";
            const gap = 6, margin = 8;
            pop.style.position = "fixed";
            pop.style.right = "auto";
            pop.style.bottom = "auto";
            const pw = pop.offsetWidth, ph = pop.offsetHeight;
            const startEdge = align === "start" ? !rtl : rtl;
            let left = startEdge ? r.left : r.right - pw;
            left = Math.max(margin, Math.min(left, window.innerWidth - pw - margin));
            let top = r.bottom + gap;
            if (top + ph > window.innerHeight - margin && r.top - gap - ph > margin) top = r.top - gap - ph;
            top = Math.max(margin, Math.min(top, window.innerHeight - ph - margin));
            pop.style.left = left + "px";
            pop.style.top = top + "px";
        } finally {
            pop.classList.add("lx-placed");
        }
    },
    // Keyboard: Ctrl/⌘+K or "/" opens the command palette, "?" lists shortcuts, "g" then a letter jumps to a page,
    // "n" starts a new order. A keyboard-wedge barcode scanner typing outside inputs opens that shipment.
    registerShortcuts: (dotNetRef) => {
        if (window.lx._shortcutsRegistered) return;
        window.lx._shortcutsRegistered = true;
        let buffer = "", lastKeyTime = 0, pendingG = 0;

        window.addEventListener("keydown", (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
                e.preventDefault();
                dotNetRef.invokeMethodAsync("OnShortcut", "palette");
                return;
            }

            const el = document.activeElement;
            const typing = el && (el.tagName === "INPUT" || el.tagName === "TEXTAREA" || el.tagName === "SELECT" || el.isContentEditable);
            if (typing || e.ctrlKey || e.metaKey || e.altKey) return;
            if (document.querySelector(".bp6-overlay-open")) return;

            const now = performance.now();
            if (now - lastKeyTime > 65) buffer = "";
            lastKeyTime = now;

            if (e.key === "Enter" && buffer.length >= 5) {
                const code = buffer.trim();
                buffer = "";
                e.preventDefault();
                dotNetRef.invokeMethodAsync("OnGlobalBarcodeScanned", code);
                return;
            }

            if (e.key.length === 1) buffer += e.key;
            if (buffer.length > 1) return; // a scanner is typing; wait for Enter

            if (pendingG && now - pendingG < 1200 && /^[a-z]$/i.test(e.key)) {
                pendingG = 0;
                dotNetRef.invokeMethodAsync("OnShortcut", "go:" + e.key.toLowerCase());
                return;
            }

            if (e.key === "/") { e.preventDefault(); dotNetRef.invokeMethodAsync("OnShortcut", "palette"); }
            else if (e.key === "?") { e.preventDefault(); dotNetRef.invokeMethodAsync("OnShortcut", "help"); }
            else if (e.key === "g") { pendingG = now; }
            else if (e.key === "n") { dotNetRef.invokeMethodAsync("OnShortcut", "new-order"); }
        });
    },
    getPref: (key) => { try { return localStorage.getItem("lx." + key); } catch { return null; } },
    setPref: (key, value) => { try { localStorage.setItem("lx." + key, value); } catch { /* private mode */ } },
    openWhatsApp: (url) => { window.open(url, "_blank", "noopener"); },
    startCameraScan: async (videoId, dotNetRef) => {
        window.lx.stopCameraScan();
        if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
            return "no-camera";
        }
        try {
            const stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } },
                audio: false
            });
            window.lx._camStream = stream;
            const video = document.getElementById(videoId);
            if (!video) {
                window.lx.stopCameraScan();
                return "error";
            }
            video.srcObject = stream;
            await video.play();
            if (typeof window.BarcodeDetector !== "function") {
                return "no-detector";
            }
            let detector;
            try {
                detector = new window.BarcodeDetector({ formats: ["code_128", "code_39", "ean_13", "qr_code"] });
            } catch {
                detector = new window.BarcodeDetector();
            }
            let lastCode = "", lastAt = 0;
            window.lx._camTimer = window.setInterval(async () => {
                if (!video || video.readyState < 2) return;
                try {
                    const codes = await detector.detect(video);
                    if (codes && codes.length > 0) {
                        const raw = (codes[0].rawValue || "").trim();
                        const now = performance.now();
                        if (raw.length >= 4 && (raw !== lastCode || now - lastAt > 2500)) {
                            lastCode = raw;
                            lastAt = now;
                            await dotNetRef.invokeMethodAsync("OnCameraBarcodeDetected", raw);
                        }
                    }
                } catch { /* frame busy */ }
            }, 180);
            return "ok";
        } catch {
            window.lx.stopCameraScan();
            return "error";
        }
    },
    stopCameraScan: () => {
        if (window.lx._camTimer) {
            window.clearInterval(window.lx._camTimer);
            window.lx._camTimer = null;
        }
        if (window.lx._camStream) {
            try {
                for (const track of window.lx._camStream.getTracks()) track.stop();
            } catch { /* ignore */ }
            window.lx._camStream = null;
        }
    },
    // Map units under the pointer for an SVG element (placing a warehouse by clicking the map).
    svgPoint: (svg, clientX, clientY) => {
        if (!svg || !svg.getScreenCTM) return null;
        const point = svg.createSVGPoint();
        point.x = clientX;
        point.y = clientY;
        const local = point.matrixTransform(svg.getScreenCTM().inverse());
        return [local.x, local.y];
    }
};
