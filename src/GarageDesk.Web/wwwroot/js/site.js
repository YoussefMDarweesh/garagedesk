// GarageDesk front-end behaviour. Vanilla JavaScript, no framework, no build step.
// Everything here is an enhancement: the pages work without it (forms post, links link, the chart's data is
// also in an accessible table).
(() => {
  "use strict";

  const root = document.documentElement;
  const t = (el, key, fallback) => (el && el.dataset[key]) || fallback;

  // ---- theme: light / dark, remembered in the browser. The <head> script has already applied it. ----
  const isDark = () =>
    (root.getAttribute("data-bs-theme") ||
      (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")) === "dark";

  const syncThemeButtons = () =>
    document.querySelectorAll(".js-theme-toggle").forEach((b) => b.setAttribute("aria-pressed", String(isDark())));

  document.addEventListener("click", (event) => {
    const button = event.target.closest(".js-theme-toggle");
    if (!button) return;
    const next = isDark() ? "light" : "dark";
    root.setAttribute("data-bs-theme", next);
    try { localStorage.setItem("gd-theme", next); } catch { /* storage blocked: still switches for this visit */ }
    syncThemeButtons();
    document.dispatchEvent(new CustomEvent("gd:themechange"));
  });
  syncThemeButtons();

  // ---- print: <button data-print> opens the browser's print dialog (Save as PDF is a printer choice there) ----
  document.addEventListener("click", (event) => {
    if (event.target.closest("[data-print]")) {
      window.print();
    }
  });

  // ---- sidebar: pin it open ----
  const pin = document.getElementById("sidebar-pin");
  if (pin) {
    const syncPin = () => pin.setAttribute("aria-pressed", String(root.getAttribute("data-sidebar") === "pinned"));
    syncPin();
    pin.addEventListener("click", () => {
      const pinned = root.getAttribute("data-sidebar") === "pinned";
      if (pinned) root.removeAttribute("data-sidebar"); else root.setAttribute("data-sidebar", "pinned");
      try { localStorage.setItem("gd-sidebar", pinned ? "" : "pinned"); } catch { /* ignore */ }
      syncPin();
    });
  }

  // ---- toasts ----
  const toasts = document.getElementById("toasts");
  const icons = {
    success: '<svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>',
    danger: '<svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
    info: '<svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>',
  };

  // `action` (optional): { label, run } adds a button to the toast, e.g. "Tell the customer on WhatsApp".
  function toast(message, kind = "info", action = null) {
    if (!toasts || !message) return;
    const card = document.createElement("div");
    card.className = `toast-card ${kind}`;
    card.setAttribute("role", kind === "danger" ? "alert" : "status");
    card.innerHTML = (icons[kind] || icons.info) + '<span class="flex-fill"></span><button type="button" aria-label="Close">&times;</button>';
    card.querySelector("span").textContent = message;
    const close = () => {
      card.classList.add("leaving");
      setTimeout(() => card.remove(), 260);
    };
    card.querySelector("button").addEventListener("click", close);
    if (action) {
      const act = document.createElement("button");
      act.type = "button";
      act.className = "btn btn-primary btn-sm toast-action";
      act.textContent = action.label;
      act.addEventListener("click", () => { action.run(); close(); });
      card.insertBefore(act, card.querySelector("button"));
    }
    toasts.appendChild(card);
    setTimeout(close, action ? 15000 : kind === "danger" ? 7000 : 4500);
  }
  window.gdToast = toast;

  // Server messages (TempData) are rendered as <div data-toast="success">…</div>. With JS they become toasts.
  // data-toast-action="#form-id" + data-toast-action-label="…" adds a button that submits that form.
  document.querySelectorAll("[data-toast]").forEach((el) => {
    let action = null;
    const target = el.dataset.toastAction ? document.querySelector(el.dataset.toastAction) : null;
    if (target instanceof HTMLFormElement && el.dataset.toastActionLabel) {
      action = { label: el.dataset.toastActionLabel, run: () => target.requestSubmit() };
    }
    toast(el.textContent.trim(), el.dataset.toast || "info", action);
    el.remove();
  });

  // ---- copy a link: <button data-copy="https://..." data-copied="Link copied."> ----
  document.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-copy]");
    if (!button) return;
    const text = button.dataset.copy;
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      // No clipboard permission (or an old browser): select the field next to the button so Ctrl+C works.
      const field = button.parentElement && button.parentElement.querySelector("input");
      if (field) { field.focus(); field.select(); }
    }
    toast(button.dataset.copied || text, "success");
  });

  // ---- confirm dialog: <form data-confirm="Are you sure?"> ----
  const dialog = document.getElementById("confirm-dialog");
  if (dialog && typeof dialog.showModal === "function") {
    let pending = null;
    document.addEventListener("submit", (event) => {
      const form = event.target;
      if (!(form instanceof HTMLFormElement) || !form.dataset.confirm || form.dataset.confirmed === "1") return;
      event.preventDefault();
      pending = form;
      dialog.querySelector("#confirm-message").textContent = form.dataset.confirm;
      dialog.querySelector("#confirm-title").textContent = form.dataset.confirmTitle || dialog.dataset.title || "Are you sure?";
      dialog.showModal();
    }, true);

    dialog.addEventListener("close", () => {
      if (dialog.returnValue === "ok" && pending) {
        pending.dataset.confirmed = "1";
        pending.requestSubmit ? pending.requestSubmit() : pending.submit();
      }
      pending = null;
      dialog.returnValue = "";
    });
  }

  // ---- busy buttons: no double-submits on slow saves ----
  document.addEventListener("submit", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form.method.toLowerCase() !== "post" || event.defaultPrevented) return;
    const button = event.submitter || form.querySelector('button[type="submit"]');
    // A form that opens a new tab (the WhatsApp buttons) leaves this page where it is, so it must not stay "busy".
    if (button && !form.classList.contains("lang-switch") && form.target !== "_blank") {
      // Disable on the next tick so the button's own name/value is still posted.
      setTimeout(() => button.classList.add("is-busy"), 0);
    }
  });

  // ---- activity chart (inline SVG, drawn from the data the server put in the page) ----
  const chartHost = document.querySelector("[data-chart]");
  if (chartHost) {
    initChart(chartHost);
  }

  function initChart(host) {
    const dataEl = host.querySelector('script[type="application/json"]');
    if (!dataEl) return;
    const data = JSON.parse(dataEl.textContent);
    const lang = root.lang || "en";
    const locale = `${lang}-u-nu-latn`;
    const monthFmt = new Intl.DateTimeFormat(locale, { month: "short" });
    const monthLong = new Intl.DateTimeFormat(locale, { month: "long", year: "numeric" });
    const numFmt = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
    const state = { metric: "revenue", range: 12, index: -1 };
    const canvas = host.querySelector(".chart-canvas");
    const tip = host.querySelector(".chart-tip");
    const W = 760, H = 270, PAD = { l: 22, r: 22, t: 24, b: 34 };

    const labelFor = {
      revenue: host.dataset.labelRevenue || "Revenue",
      jobs: host.dataset.labelJobs || "Job cards",
    };
    const currency = host.dataset.currency || "EGP";

    function series() {
      const months = data.months.slice(-state.range);
      return months.map((m) => {
        const [y, mo] = m.key.split("-").map(Number);
        const date = new Date(Date.UTC(y, mo - 1, 1));
        return { date, value: m[state.metric], label: monthFmt.format(date), long: monthLong.format(date) };
      });
    }

    // Smooth curve through the points (Catmull-Rom converted to cubic Béziers).
    function smoothPath(pts) {
      if (pts.length < 2) return pts.length ? `M${pts[0].x},${pts[0].y}` : "";
      let d = `M${pts[0].x},${pts[0].y}`;
      for (let i = 0; i < pts.length - 1; i++) {
        const p0 = pts[i - 1] || pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] || p2;
        // Keeping each control point between its two neighbours' heights stops the curve from overshooting
        // (a dip below zero between two low months, or a bump above a peak).
        const lo = Math.min(p1.y, p2.y), hi = Math.max(p1.y, p2.y);
        const clamp = (y) => Math.min(hi, Math.max(lo, y));
        const c1x = p1.x + (p2.x - p0.x) / 6, c1y = clamp(p1.y + (p2.y - p0.y) / 6);
        const c2x = p2.x - (p3.x - p1.x) / 6, c2y = clamp(p2.y - (p3.y - p1.y) / 6);
        d += ` C${c1x.toFixed(1)},${c1y.toFixed(1)} ${c2x.toFixed(1)},${c2y.toFixed(1)} ${p2.x.toFixed(1)},${p2.y.toFixed(1)}`;
      }
      return d;
    }

    function format(value) {
      return state.metric === "revenue" ? `${numFmt.format(value)} ${currency}` : numFmt.format(value);
    }

    let pts = [];
    let rows = [];

    function draw(animate) {
      rows = series();
      const max = Math.max(1, ...rows.map((r) => r.value)) * 1.18;
      const innerW = W - PAD.l - PAD.r, innerH = H - PAD.t - PAD.b;
      pts = rows.map((r, i) => ({
        x: PAD.l + (rows.length === 1 ? innerW / 2 : (innerW * i) / (rows.length - 1)),
        y: PAD.t + innerH - (r.value / max) * innerH,
      }));
      const line = smoothPath(pts);
      const area = pts.length ? `${line} L${pts[pts.length - 1].x},${H - PAD.b} L${pts[0].x},${H - PAD.b} Z` : "";

      let grid = "";
      for (let g = 0; g <= 3; g++) {
        const y = PAD.t + (innerH * g) / 3;
        grid += `<line x1="${PAD.l}" x2="${W - PAD.r}" y1="${y}" y2="${y}"/>`;
      }
      const every = rows.length > 8 ? 2 : 1;
      const labels = rows.map((r, i) => (i % every === 0 || i === rows.length - 1)
        ? `<text x="${pts[i].x}" y="${H - 8}" text-anchor="middle">${r.label}</text>` : "").join("");

      canvas.innerHTML = `
        <svg class="chart-svg" viewBox="0 0 ${W} ${H}" role="img" aria-label="${labelFor[state.metric]}">
          <defs>
            <linearGradient id="chart-fill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" style="stop-color: var(--accent); stop-opacity: .38"/>
              <stop offset="100%" style="stop-color: var(--accent); stop-opacity: 0"/>
            </linearGradient>
          </defs>
          <g class="chart-grid">${grid}</g>
          <path class="chart-area ${animate ? "chart-area-in" : ""}" d="${area}"/>
          <path class="chart-line ${animate ? "chart-draw" : ""}" d="${line}"/>
          <line class="chart-cursor" x1="0" x2="0" y1="${PAD.t}" y2="${H - PAD.b}" style="display:none"/>
          <circle class="chart-dot" r="6" cx="0" cy="0" style="display:none"/>
          <g class="chart-axis">${labels}</g>
        </svg>`;

      const linePath = canvas.querySelector(".chart-line");
      if (animate && linePath.getTotalLength) {
        linePath.style.setProperty("--len", Math.ceil(linePath.getTotalLength()) + 2);
      }
      show(state.index >= 0 && state.index < rows.length ? state.index : rows.length - 1);
    }

    function show(index) {
      if (!rows[index]) return;
      state.index = index;
      const svg = canvas.querySelector("svg");
      const cursor = svg.querySelector(".chart-cursor");
      const dot = svg.querySelector(".chart-dot");
      const p = pts[index];
      cursor.setAttribute("x1", p.x); cursor.setAttribute("x2", p.x); cursor.style.display = "";
      dot.setAttribute("cx", p.x); dot.setAttribute("cy", p.y); dot.style.display = "";

      const box = svg.getBoundingClientRect();
      const wrap = canvas.getBoundingClientRect();
      tip.style.left = `${(p.x / W) * box.width + (box.left - wrap.left)}px`;
      tip.style.top = `${(p.y / H) * box.height + (box.top - wrap.top)}px`;
      tip.innerHTML = `<span></span><strong></strong>`;
      tip.querySelector("span").textContent = `${rows[index].long} · ${labelFor[state.metric]}`;
      tip.querySelector("strong").textContent = format(rows[index].value);
      tip.classList.add("show");
    }

    function nearest(clientX) {
      const svg = canvas.querySelector("svg");
      const box = svg.getBoundingClientRect();
      const x = ((clientX - box.left) / box.width) * W;
      let best = 0, bestDist = Infinity;
      pts.forEach((p, i) => { const d = Math.abs(p.x - x); if (d < bestDist) { best = i; bestDist = d; } });
      return best;
    }

    canvas.addEventListener("pointermove", (e) => show(nearest(e.clientX)));
    canvas.addEventListener("pointerdown", (e) => show(nearest(e.clientX)));
    canvas.addEventListener("keydown", (e) => {
      if (e.key === "ArrowLeft" || e.key === "ArrowRight") {
        e.preventDefault();
        const step = e.key === "ArrowRight" ? 1 : -1;
        show(Math.min(rows.length - 1, Math.max(0, state.index + step)));
      }
    });

    host.querySelectorAll("[data-range]").forEach((b) => b.addEventListener("click", () => {
      state.range = Number(b.dataset.range);
      state.index = -1;
      host.querySelectorAll("[data-range]").forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
      draw(true);
    }));
    host.querySelectorAll("[data-metric]").forEach((b) => b.addEventListener("click", () => {
      state.metric = b.dataset.metric;
      host.querySelectorAll("[data-metric]").forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
      draw(true);
    }));

    window.addEventListener("resize", () => show(state.index));
    document.addEventListener("gd:themechange", () => show(state.index));
    draw(true);
    host.classList.add("is-ready");
  }
})();

// v1-4-stock-profit: the catalog part picker on the "add a line" forms (job card, estimate, technician page).
// Without JavaScript the select alone works: the server fills in the name and the price from the catalog.
(function () {
  document.querySelectorAll("[data-part-picker]").forEach((select) => {
    const form = select.closest("form");
    if (!form) { return; }
    const description = form.querySelector("[data-part-description]");
    const price = form.querySelector("[data-part-price]");
    const type = form.querySelector("[data-part-type]");

    select.addEventListener("change", () => {
      const option = select.selectedOptions[0];
      if (!option || !option.value) { return; }
      if (description) { description.value = option.dataset.name || ""; }
      if (price && option.dataset.price !== undefined) { price.value = option.dataset.price; }
      if (type) { type.value = "Part"; }
    });

    // Typing a catalog name exactly (the browser offers the names as suggestions) picks that part.
    if (description) {
      description.addEventListener("input", () => {
        const typed = description.value.trim().toLowerCase();
        if (!typed) { return; }
        const match = Array.from(select.options).find((o) => o.value && (o.dataset.name || "").toLowerCase() === typed);
        if (match && select.value !== match.value) {
          select.value = match.value;
          select.dispatchEvent(new Event("change"));
        }
      });
    }
  });
})();

// ---- growth-onboarding: installable app (docs/PWA.md) ----
// Registers the service worker (static files only, never pages) and offers "Install" once per device. Nothing here
// is needed for the site to work.
(() => {
  "use strict";
  const meta = document.querySelector('meta[name="gd-sw"]');
  if (!meta) return;

  if ("serviceWorker" in navigator) {
    window.addEventListener("load", () => {
      navigator.serviceWorker.register(meta.content, { scope: meta.dataset.scope || "/" }).catch(() => { /* not available here: fine */ });
    });
  }

  const standalone = window.matchMedia("(display-mode: standalone)").matches || navigator.standalone === true;
  const KEY = "gd-install-dismissed";
  const dismissed = () => { try { return localStorage.getItem(KEY) === "1"; } catch { return false; } };
  if (standalone || dismissed()) return;

  const show = (text, actionLabel, onAction) => {
    if (document.querySelector(".install-banner")) return;
    const banner = document.createElement("div");
    banner.className = "install-banner";
    banner.setAttribute("role", "region");
    banner.setAttribute("aria-label", meta.dataset.installTitle || "Install");
    const message = document.createElement("span");
    message.className = "install-text";
    message.textContent = text;
    banner.appendChild(message);
    if (onAction) {
      const action = document.createElement("button");
      action.type = "button";
      action.className = "btn btn-primary btn-sm";
      action.textContent = actionLabel;
      action.addEventListener("click", async () => { await onAction(); banner.remove(); });
      banner.appendChild(action);
    }
    const close = document.createElement("button");
    close.type = "button";
    close.className = "btn btn-outline-secondary btn-sm";
    close.textContent = meta.dataset.installDismiss || "Not now";
    close.addEventListener("click", () => {
      try { localStorage.setItem(KEY, "1"); } catch { /* storage blocked: it just asks again next visit */ }
      banner.remove();
    });
    banner.appendChild(close);
    document.body.appendChild(banner);
  };

  // Chrome, Edge, Android: the browser says it can install the app and hands over a prompt to open on a click.
  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    show(meta.dataset.installTitle, meta.dataset.installAction, async () => {
      event.prompt();
      await event.userChoice;
    });
  });
  window.addEventListener("appinstalled", () => document.querySelector(".install-banner")?.remove());

  // iPhone and iPad Safari never fire that event: show a one-line how-to instead.
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) && !/CriOS|FxiOS/.test(navigator.userAgent);
  if (ios) {
    show(meta.dataset.installIos, "", null);
  }
})();
