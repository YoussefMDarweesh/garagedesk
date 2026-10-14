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

  function toast(message, kind = "info") {
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
    toasts.appendChild(card);
    setTimeout(close, kind === "danger" ? 7000 : 4500);
  }
  window.gdToast = toast;

  // Server messages (TempData) are rendered as <div data-toast="success">…</div>. With JS they become toasts.
  document.querySelectorAll("[data-toast]").forEach((el) => {
    toast(el.textContent.trim(), el.dataset.toast || "info");
    el.remove();
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
    if (button && !form.classList.contains("lang-switch")) {
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
