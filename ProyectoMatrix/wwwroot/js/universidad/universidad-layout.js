/* =====================================================
   ARCHIVO: wwwroot/js/universidad/universidad-layout.js
   PROPÓSITO: Funcionalidades del layout Universidad NS
   ===================================================== */

class UniversidadLayout {
    constructor() {
        this.userMenuToggle = null;
        this.userDropdown = null;
        this.notificationsBtn = null;
        this.notificationsPanel = null;
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
        this.checkNotifications();

        this.isInitialized = true;
        console.log('🎓 Universidad Layout inicializado correctamente');
    }

    setupElements() {
        this.userMenuToggle = document.getElementById('userMenuToggle');
        this.userDropdown = document.getElementById('userDropdown');
        this.notificationsBtn = document.getElementById('notificationsBtn');
        this.notificationsPanel = document.getElementById('notificationsPanel');
        this.panelOverlay = document.getElementById('panelOverlay');
        this.closeNotifications = document.getElementById('closeNotifications');

        if (!this.userMenuToggle || !this.userDropdown) {
            console.warn('⚠️ Elementos del menú de usuario no encontrados');
        }
    }

    setupEventListeners() {
        // User Menu Toggle
        if (this.userMenuToggle) {
            this.userMenuToggle.addEventListener('click', (e) => {
                e.stopPropagation();
                this.toggleUserMenu();
            });
        }

        // Notifications Toggle
        if (this.notificationsBtn) {
            this.notificationsBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.toggleNotifications();
            });
        }

        // Close Notifications
        if (this.closeNotifications) {
            this.closeNotifications.addEventListener('click', () => {
                this.closeNotificationsPanel();
            });
        }

        // Panel Overlay
        if (this.panelOverlay) {
            this.panelOverlay.addEventListener('click', () => {
                this.closeAllPanels();
            });
        }

        // Click outside to close
        document.addEventListener('click', (e) => {
            if (!this.userDropdown?.contains(e.target) &&
                !this.userMenuToggle?.contains(e.target)) {
                this.closeUserMenu();
            }

            if (!this.notificationsPanel?.contains(e.target) &&
                !this.notificationsBtn?.contains(e.target)) {
                this.closeNotificationsPanel();
            }
        });

        // Navigation link highlighting
        this.setupNavigationHighlighting();

        // Auto-save preferences
        this.setupPreferences();

        // Window resize handler
        window.addEventListener('resize', () => {
            this.handleResize();
        });

        // Before unload - save state
        window.addEventListener('beforeunload', () => {
            this.saveState();
        });
    }

    setupKeyboardNavigation() {
        document.addEventListener('keydown', (e) => {
            // ESC key closes all panels
            if (e.key === 'Escape') {
                this.closeAllPanels();
            }

            // Alt + N for notifications
            if (e.altKey && e.key === 'n') {
                e.preventDefault();
                this.toggleNotifications();
            }

            // Alt + M for user menu
            if (e.altKey && e.key === 'm') {
                e.preventDefault();
                this.toggleUserMenu();
            }

            // Alt + H for home/dashboard
            if (e.altKey && e.key === 'h') {
                e.preventDefault();
                window.location.href = '/Universidad';
            }

            // Alt + C for courses
            if (e.altKey && e.key === 'c') {
                e.preventDefault();
                window.location.href = '/Universidad/MisCursos';
            }
        });
    }

    setupTheme() {
        // Aplicar tema según empresa
        const empresaId = this.config.empresaId;
        if (empresaId) {
            document.body.setAttribute('data-empresa', empresaId);
        }

        // Verificar preferencia de tema oscuro
        const darkMode = localStorage.getItem('universidad_dark_mode') === 'true';
        if (darkMode) {
            document.body.classList.add('dark-mode');
        }
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
        // Cargar preferencias guardadas
        const preferences = this.loadPreferences();

        // Aplicar preferencias
        if (preferences.compactMode) {
            document.body.classList.add('compact-mode');
        }

        if (preferences.animationsEnabled === false) {
            document.body.classList.add('no-animations');
        }
    }

    // ===== USER MENU METHODS =====

    toggleUserMenu() {
        if (this.userDropdown?.classList.contains('show')) {
            this.closeUserMenu();
        } else {
            this.openUserMenu();
        }
    }

    openUserMenu() {
        this.closeNotificationsPanel(); // Close other panels

        if (this.userDropdown) {
            this.userDropdown.classList.add('show');
            this.userMenuToggle?.setAttribute('aria-expanded', 'true');

            // Focus first item
            const firstItem = this.userDropdown.querySelector('.dropdown-item');
            if (firstItem) {
                setTimeout(() => firstItem.focus(), 100);
            }
        }
    }

    closeUserMenu() {
        if (this.userDropdown) {
            this.userDropdown.classList.remove('show');
            this.userMenuToggle?.setAttribute('aria-expanded', 'false');
        }
    }

    // ===== NOTIFICATIONS METHODS =====

    toggleNotifications() {
        if (this.notificationsPanel?.classList.contains('show')) {
            this.closeNotificationsPanel();
        } else {
            this.openNotificationsPanel();
        }
    }

    openNotificationsPanel() {
        this.closeUserMenu(); // Close other panels

        if (this.notificationsPanel) {
            this.notificationsPanel.classList.add('show');
            this.panelOverlay?.classList.add('show');

            // Mark notifications as read
            this.markNotificationsAsRead();
        }
    }

    closeNotificationsPanel() {
        if (this.notificationsPanel) {
            this.notificationsPanel.classList.remove('show');
            this.panelOverlay?.classList.remove('show');
        }
    }

    closeAllPanels() {
        this.closeUserMenu();
        this.closeNotificationsPanel();
    }

    async checkNotifications() {
        try {
            // Simulated API call - replace with real endpoint
            const response = await fetch('/Universidad/Api/Notifications', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            if (response.ok) {
                const notifications = await response.json();
                this.updateNotificationBadge(notifications.unreadCount || 0);
                this.updateNotificationsPanel(notifications.items || []);
            }
        } catch (error) {
            console.warn('Error al cargar notificaciones:', error);
            // Fallback con notificaciones mock
            this.updateNotificationBadge(3);
        }
    }

    updateNotificationBadge(count) {
        const badge = document.querySelector('.notification-badge');
        if (badge) {
            if (count > 0) {
                badge.textContent = count > 99 ? '99+' : count.toString();
                badge.style.display = 'block';
            } else {
                badge.style.display = 'none';
            }
        }
    }

    updateNotificationsPanel(notifications) {
        const content = document.querySelector('.notifications-content');
        if (!content) return;

        if (notifications.length === 0) {
            content.innerHTML = `
                <div class="text-center py-4">
                    <i class="fas fa-bell-slash fa-2x text-muted mb-3"></i>
                    <h6 class="text-muted">No hay notificaciones</h6>
                    <p class="text-muted small">Las nuevas notificaciones aparecerán aquí</p>
                </div>
            `;
            return;
        }

        content.innerHTML = notifications.map(notification => `
            <div class="notification-item ${notification.unread ? 'unread' : ''}">
                <div class="notification-icon bg-${notification.type || 'primary'}">
                    <i class="fas fa-${notification.icon || 'bell'}"></i>
                </div>
                <div class="notification-content">
                    <h6>${notification.title}</h6>
                    <p>${notification.message}</p>
                    <small class="text-muted">${notification.timeAgo}</small>
                </div>
            </div>
        `).join('');
    }

    markNotificationsAsRead() {
        const unreadItems = document.querySelectorAll('.notification-item.unread');
        unreadItems.forEach(item => {
            setTimeout(() => {
                item.classList.remove('unread');
            }, 500);
        });

        // Update badge
        setTimeout(() => {
            this.updateNotificationBadge(0);
        }, 1000);
    }

    // ===== UTILITY METHODS =====

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
                <div class="toast-body">
                    ${message}
                </div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" 
                        data-bs-dismiss="toast"></button>
            </div>
        `;

        toastContainer.appendChild(toast);

        const bsToast = new bootstrap.Toast(toast, { delay: duration });
        bsToast.show();

        // Remove after hiding
        toast.addEventListener('hidden.bs.toast', () => {
            toast.remove();
        });
    }

    navigateTo(url, newTab = false) {
        if (newTab) {
            window.open(url, '_blank');
        } else {
            // Show loading state
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
        } catch (error) {
            console.warn('No se pudo guardar el estado:', error);
        }
    }

    loadPreferences() {
        try {
            const prefs = localStorage.getItem('universidad_preferences');
            return prefs ? JSON.parse(prefs) : {};
        } catch (error) {
            console.warn('Error al cargar preferencias:', error);
            return {};
        }
    }

    savePreferences(preferences) {
        try {
            localStorage.setItem('universidad_preferences', JSON.stringify(preferences));
        } catch (error) {
            console.warn('Error al guardar preferencias:', error);
        }
    }

    getPreferences() {
        return {
            compactMode: document.body.classList.contains('compact-mode'),
            darkMode: document.body.classList.contains('dark-mode'),
            animationsEnabled: !document.body.classList.contains('no-animations')
        };
    }

    // ===== API METHODS =====

    async apiCall(endpoint, options = {}) {
        const defaultOptions = {
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            }
        };

        try {
            const response = await fetch(endpoint, { ...defaultOptions, ...options });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            return await response.json();
        } catch (error) {
            console.error('Error en API call:', error);
            this.showToast('Error de conexión. Intente nuevamente.', 'danger');
            throw error;
        }
    }

    // ===== PUBLIC METHODS =====

    refresh() {
        this.checkNotifications();
        this.setupNavigationHighlighting();
    }

    setCompactMode(enabled) {
        if (enabled) {
            document.body.classList.add('compact-mode');
        } else {
            document.body.classList.remove('compact-mode');
        }
        this.savePreferences(this.getPreferences());
    }

    setDarkMode(enabled) {
        if (enabled) {
            document.body.classList.add('dark-mode');
        } else {
            document.body.classList.remove('dark-mode');
        }
        this.savePreferences(this.getPreferences());
    }

    // ===== PERFORMANCE MONITORING =====

    logPerformance() {
        if ('performance' in window) {
            const loadTime = performance.now();
            console.log(`⚡ Layout Universidad cargado en ${loadTime.toFixed(2)}ms`);

            // Log navigation timing
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

// ===== GLOBAL UTILITIES =====

window.UniversidadUtils = {
    formatTime(seconds) {
        const hours = Math.floor(seconds / 3600);
        const minutes = Math.floor((seconds % 3600) / 60);
        const secs = seconds % 60;

        if (hours > 0) {
            return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
        }
        return `${minutes}:${secs.toString().padStart(2, '0')}`;
    },

    formatDate(date) {
        return new Intl.DateTimeFormat('es-ES', {
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        }).format(new Date(date));
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
        let timeout;
        return function executedFunction(...args) {
            const later = () => {
                clearTimeout(timeout);
                func(...args);
            };
            clearTimeout(timeout);
            timeout = setTimeout(later, wait);
        };
    },

    throttle(func, limit) {
        let inThrottle;
        return function () {
            const args = arguments;
            const context = this;
            if (!inThrottle) {
                func.apply(context, args);
                inThrottle = true;
                setTimeout(() => inThrottle = false, limit);
            }
        }
    }
};

// ===== INITIALIZATION =====

// Create global instance
window.UniversidadLayout = new UniversidadLayout();

// Auto-initialize on DOM ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        // Will be initialized from the layout with config
    });
} else {
    // Document already loaded
    console.log('📚 Universidad Layout ready for initialization');
}

// Debug helpers (only in development)
if (window.location.hostname === 'localhost' || window.location.hostname.includes('dev')) {
    window.debugUniversidad = {
        layout: window.UniversidadLayout,
        utils: window.UniversidadUtils,

        showTestToast() {
            window.UniversidadLayout.showToast('Toast de prueba', 'success');
        },

        toggleNotifications() {
            window.UniversidadLayout.toggleNotifications();
        },

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