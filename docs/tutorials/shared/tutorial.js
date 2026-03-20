// FalkForge Tutorial - Shared JavaScript
// Theme toggle, code copy buttons, smooth scroll

document.addEventListener('DOMContentLoaded', () => {
    // --- Theme toggle ---
    const themeToggle = document.getElementById('theme-toggle');
    const storageKey = 'falkforge-theme';

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        if (themeToggle) {
            themeToggle.textContent = theme === 'dark' ? '\u{1F319}' : '\u{2600}\u{FE0F}';
        }
    }

    const savedTheme = localStorage.getItem(storageKey) || 'dark';
    applyTheme(savedTheme);

    if (themeToggle) {
        themeToggle.addEventListener('click', () => {
            const current = document.documentElement.getAttribute('data-theme');
            const next = current === 'dark' ? 'light' : 'dark';
            localStorage.setItem(storageKey, next);
            applyTheme(next);
        });
    }

    // --- Code copy buttons ---
    document.querySelectorAll('pre > code').forEach((codeEl) => {
        const pre = codeEl.parentElement;
        const btn = document.createElement('button');
        btn.className = 'copy-btn';
        btn.textContent = 'Copy';
        btn.addEventListener('click', () => {
            navigator.clipboard.writeText(codeEl.textContent).then(() => {
                btn.textContent = 'Copied!';
                setTimeout(() => { btn.textContent = 'Copy'; }, 2000);
            });
        });
        pre.appendChild(btn);
    });

    // --- Smooth scroll ---
    document.querySelectorAll('a[href^="#"]').forEach((link) => {
        link.addEventListener('click', (e) => {
            const target = document.querySelector(link.getAttribute('href'));
            if (target) {
                e.preventDefault();
                target.scrollIntoView({ behavior: 'smooth' });
            }
        });
    });
});
