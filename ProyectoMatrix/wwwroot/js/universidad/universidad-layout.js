class UniversidadLayout {
    constructor() {
        this.userMenuToggle = null;
        this.userDropdown = null;
        this.panelOverlay = null;

        this.config = {
            usuarioId: null,
            rolId: null,
            empresaId: null
        };

        this.isInitialized = false;
    }

    init(config = {}) {
        this.config = { ...this.config, ...config };
        if (this.isInitialized) return;

        this.setupElements();
        this.setupEventListeners();
        this.setupKeyboardNavigation();
        this.setupTheme();

        this.isInitialized = true;
        console.log('🎓 Universidad Layout inicializado correctamente');
    }

    setupElements() {
        this.userMenuToggle = document.getElementById('userMenuToggle');
        this.userDropdown = document.getElementById('userDropdown');
        this.panelOverlay = document.getElementById('panelOverlay');

        if (!this.userMenuToggle || !this.userDropdown) {
            console.warn('⚠️ Elementos del menú de usuario no encontrados');
        }
    }

    setupEventListeners() {
        // Toggle menú usuario
        if (this.userMenuToggle) {
            this.userMenuToggle.addEventListener('click', (e) => {
                e.stopPropagation();
                this.toggleUserMenu();
            });
        }

        // Overlay (cierra paneles abiertos)
        if (this.panelOverlay) {
            this.panelOverlay.addEventListener('click', () => {
                this.closeAllPanels();
            });
        }

        // Click fuera → cerrar menú usuario
        document.addEventListener('click', (e) => {
            if (!this.userDropdown?.contains(e.target) &&
                !this.userMenuToggle?.contains(e.target)) {
                this.closeUserMenu();
            }
        });

        // Resaltar navegación
        this.setupNavigationHighlighting();

        // Preferencias
        this.setupPreferences();

        // Resize
        window.addEventListener('resize', () => this.handleResize());

        // Guardar estado al salir
        window.addEventListener('beforeunload', () => this.saveState());
    }

    setupKeyboardNavigation() {
        document.addEventListener('keydown', (e) => {
            // ESC cierra paneles
            if (e.key === 'Escape') this.closeAllPanels();

            // Alt + M → menú usuario
            if (e.altKey && e.key === 'm') {
                e.preventDefault();
                this.toggleUserMenu();
            }

            // Alt + H → dashboard
            if (e.altKey && e.key === 'h') {
                e.preventDefault();
                window.location.href = '/Universidad';
            }

            // Alt + C → cursos
            if (e.altKey && e.key === 'c') {
                e.preventDefault();
                window.location.href = '/Universidad/MisCursos';
            }
        });
    }

    setupTheme() {
        const empresaId = this.config.empresaId;
        if (empresaId) document.body.setAttribute('data-empresa', empresaId);

        const darkMode = localStorage.getItem('universidad_dark_mode') === 'true';
        if (darkMode) document.body.classList.add('dark-mode');
    }

    setupNavigationHighlighting() {
        const navLinks = document.querySelectorAll('.nav-link');
        const currentPath = window.location.pathname;

        navLinks.forEach(link => {
            const href = link.getAttribute('href');
            if (href && currentPath.startsWith(href) && href !== '/Universidad') {
                link.classList.add('active');
            } else if (href === '/Universidad' && currentPath === '/Universidad') {
                link.classList.add('active');
            }
        });
    }

    setupPreferences() {
        const preferences = this.loadPreferences();
        if (preferences.compactMode) document.body.classList.add('compact-mode');
        if (preferences.animationsEnabled === false) document.body.classList.add('no-animations');
    }

    // ===== USER MENU =====
    toggleUserMenu() {
        if (this.userDropdown?.classList.contains('show')) this.closeUserMenu();
        else this.openUserMenu();
    }

    openUserMenu() {
        if (this.userDropdown) {
            this.userDropdown.classList.add('show');
            this.userMenuToggle?.setAttribute('aria-expanded', 'true');

            const firstItem = this.userDropdown.querySelector('.dropdown-item');
            if (firstItem) setTimeout(() => firstItem.focus(), 100);
        }
        this.panelOverlay?.classList.add('show');
    }

    closeUserMenu() {
        if (this.userDropdown) {
            this.userDropdown.classList.remove('show');
            this.userMenuToggle?.setAttribute('aria-expanded', 'false');
        }
        this.panelOverlay?.classList.remove('show');
    }

    // Cierra cualquier “panel” abierto (actualmente sólo menú usuario)
    closeAllPanels() {
        this.closeUserMenu();
    }

    // ===== UTILIDADES =====
    showToast(message, type = 'info', duration = 3000) {
        const toastContainer = document.getElementById('toastContainer');
        if (!toastContainer) return;

        const toastId = 'toast_' + Date.now();
        const toast = document.createElement('div');
        toast.id = toastId;
        toast.className = `toast align-items-center text-white bg-${type} border-0`;
        toast.setAttribute('role', 'alert');
        toast.innerHTML = `
            <div class="d-flex">
                <div class="toast-body">${message}</div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
            </div>
        `;
        toastContainer.appendChild(toast);

        const bsToast = new bootstrap.Toast(toast, { delay: duration });
        bsToast.show();
        toast.addEventListener('hidden.bs.toast', () => toast.remove());
    }

    navigateTo(url, newTab = false) {
        if (newTab) window.open(url, '_blank');
        else {
            this.showPageLoading();
            window.location.href = url;
        }
    }

    showPageLoading() {
        const overlay = document.createElement('div');
        overlay.className = 'page-loading-overlay';
        overlay.innerHTML = `
            <div class="loading-content">
                <div class="loading-spinner"></div>
                <p>Cargando...</p>
            </div>
        `;
        document.body.appendChild(overlay);
    }

    handleResize() {
        const isMobile = window.innerWidth <= 768;
        if (isMobile) {
            this.closeAllPanels();
            document.body.classList.add('mobile-layout');
        } else {
            document.body.classList.remove('mobile-layout');
        }
    }

    saveState() {
        const state = {
            lastPage: window.location.pathname,
            timestamp: Date.now(),
            preferences: this.getPreferences()
        };
        try {
            sessionStorage.setItem('universidad_state', JSON.stringify(state));
        } catch (e) {
            console.warn('No se pudo guardar el estado:', e);
        }
    }

    loadPreferences() {
        try {
            const prefs = localStorage.getItem('universidad_preferences');
            return prefs ? JSON.parse(prefs) : {};
        } catch (e) {
            console.warn('Error al cargar preferencias:', e);
            return {};
        }
    }

    savePreferences(preferences) {
        try {
            localStorage.setItem('universidad_preferences', JSON.stringify(preferences));
        } catch (e) {
            console.warn('Error al guardar preferencias:', e);
        }
    }

    getPreferences() {
        return {
            compactMode: document.body.classList.contains('compact-mode'),
            darkMode: document.body.classList.contains('dark-mode'),
            animationsEnabled: !document.body.classList.contains('no-animations')
        };
    }

    // ===== PÚBLICO =====
    refresh() {
        this.setupNavigationHighlighting();
    }

    setCompactMode(enabled) {
        if (enabled) document.body.classList.add('compact-mode');
        else document.body.classList.remove('compact-mode');
        this.savePreferences(this.getPreferences());
    }

    setDarkMode(enabled) {
        if (enabled) document.body.classList.add('dark-mode');
        else document.body.classList.remove('dark-mode');
        this.savePreferences(this.getPreferences());
    }

    // ===== PERF =====
    logPerformance() {
        if ('performance' in window) {
            const loadTime = performance.now();
            console.log(`⚡ Layout Universidad cargado en ${loadTime.toFixed(2)}ms`);
            window.addEventListener('load', () => {
                const perfData = performance.getEntriesByType('navigation')[0];
                if (perfData) {
                    console.log('📊 Performance Universidad:', {
                        'DOM Load': Math.round(perfData.domContentLoadedEventEnd - perfData.navigationStart),
                        'Page Load': Math.round(perfData.loadEventEnd - perfData.navigationStart),
                        'DNS': Math.round(perfData.domainLookupEnd - perfData.domainLookupStart)
                    });
                }
            });
        }
    }
}

// ===== UTILIDADES GLOBALES =====
window.UniversidadUtils = {
    formatTime(seconds) {
        const hours = Math.floor(seconds / 3600);
        const minutes = Math.floor((seconds % 3600) / 60);
        const secs = seconds % 60;
        if (hours > 0) return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
        return `${minutes}:${secs.toString().padStart(2, '0')}`;
    },
    formatDate(date) {
        return new Intl.DateTimeFormat('es-ES', { year: 'numeric', month: 'long', day: 'numeric' }).format(new Date(date));
    },
    formatRelativeTime(date) {
        const now = new Date();
        const diff = now - new Date(date);
        const minutes = Math.floor(diff / 60000);
        const hours = Math.floor(minutes / 60);
        const days = Math.floor(hours / 24);
        if (days > 0) return `Hace ${days} día${days > 1 ? 's' : ''}`;
        if (hours > 0) return `Hace ${hours} hora${hours > 1 ? 's' : ''}`;
        if (minutes > 0) return `Hace ${minutes} minuto${minutes > 1 ? 's' : ''}`;
        return 'Ahora mismo';
    },
    debounce(func, wait) {
        let t; return function (...args) { clearTimeout(t); t = setTimeout(() => func.apply(this, args), wait); };
    },
    throttle(func, limit) {
        let inT; return function (...args) { if (!inT) { func.apply(this, args); inT = true; setTimeout(() => inT = false, limit); } };
    }
};

// ===== INIT =====
window.UniversidadLayout = new UniversidadLayout();

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => { /* se inicializa desde el layout */ });
} else {
    console.log('📚 Universidad Layout ready for initialization');
}

// Debug helpers (dev)
if (window.location.hostname === 'localhost' || window.location.hostname.includes('dev')) {
    window.debugUniversidad = {
        layout: window.UniversidadLayout,
        utils: window.UniversidadUtils,
        showTestToast() { window.UniversidadLayout.showToast('Toast de prueba', 'success'); },
        logState() {
            console.log('Universidad Layout State:', {
                config: window.UniversidadLayout.config,
                isInitialized: window.UniversidadLayout.isInitialized,
                preferences: window.UniversidadLayout.getPreferences()
            });
        }
    };
    console.log('🔧 Debug Universidad disponible en window.debugUniversidad');
}
