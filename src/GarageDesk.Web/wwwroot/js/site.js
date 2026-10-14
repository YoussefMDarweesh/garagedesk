// Light / dark theme toggle. The small script in <head> applies the saved (or OS) theme before first paint;
// this handles the navbar button and remembers the choice.
(() => {
  const root = document.documentElement;
  const button = document.getElementById("theme-toggle");
  if (!button) {
    return;
  }

  const isDark = () =>
    (root.getAttribute("data-bs-theme") ||
      (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")) === "dark";

  const sync = () => button.setAttribute("aria-pressed", String(isDark()));
  sync();

  button.addEventListener("click", () => {
    const next = isDark() ? "light" : "dark";
    root.setAttribute("data-bs-theme", next);
    try {
      localStorage.setItem("gd-theme", next);
    } catch {
      // Storage can be blocked (private mode); the theme still switches for this visit.
    }
    sync();
  });
})();
