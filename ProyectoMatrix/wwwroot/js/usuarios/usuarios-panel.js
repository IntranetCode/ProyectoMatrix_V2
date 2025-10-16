document.addEventListener('DOMContentLoaded', function () {
    const panel = document.getElementById('slide-out-panel');
    const overlay = document.getElementById('panel-overlay');
    const panelContent = document.getElementById('panel-content');

    if (!panel || !overlay || !panelContent) {
        console.error('[usuarios-panel] Faltan elementos del panel (#slide-out-panel, #panel-overlay, #panel-content).');
        return;
    }

    function showPanel() {
        // Si tu CSS usa .open o .show, cambia aquí:
        panel.classList.add('is-open');
        overlay.classList.add('is-open');
    }
    function hidePanel() {
        panel.classList.remove('is-open');
        overlay.classList.remove('is-open');
    }

    function spinner() {
        return `
      <div class="d-flex justify-content-center align-items-center h-100 p-4">
        <div class="spinner-border" role="status" aria-label="Cargando"></div>
      </div>`;
    }

    async function openPanel(url) {
        if (!url) {
            console.error('[usuarios-panel] URL vacía para openPanel');
            return;
        }

        panelContent.innerHTML = spinner();
        showPanel();

        try {
            const res = await fetch(url, {
                method: 'GET',
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'include'
            });

            if (!res.ok) {
                const txt = await res.text().catch(() => '');
                throw new Error(`Error del servidor: ${res.status}\n${txt.substring(0, 500)}`);
            }

            const html = await res.text();
            panelContent.innerHTML = html;

            // Re-activar validación unobtrusive si está disponible
            if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                panelContent.querySelectorAll('form').forEach(f => window.jQuery.validator.unobtrusive.parse(f));
            }
        } catch (err) {
            console.error('Error al abrir el panel:', err);
            panelContent.innerHTML = `
        <div class="alert alert-danger m-3">
          No se pudo cargar el contenido.<br>
          <small>${(err && err.message) ? err.message.replace(/</g, '&lt;') : 'Error desconocido'}</small>
        </div>`;
        }
    }

    function closePanel() {
        hidePanel();
        setTimeout(() => { panelContent.innerHTML = ''; }, 300);
    }

    async function handleFormSubmit(form) {
        try {
            const res = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'include'
            });

            // Si el server redirige (guardado OK), seguimos esa redirección
            if (res.redirected) {
                window.location.href = res.url;
                return;
            }

            const html = await res.text();
            panelContent.innerHTML = html;

            // Re-activar validación (errores de modelo)
            if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                panelContent.querySelectorAll('form').forEach(f => window.jQuery.validator.unobtrusive.parse(f));
            }
        } catch (err) {
            console.error('Error al enviar el formulario:', err);
            panelContent.innerHTML = `
        <div class="alert alert-danger m-3">
          No se pudo enviar el formulario.<br>
          <small>${(err && err.message) ? err.message.replace(/</g, '&lt;') : 'Error desconocido'}</small>
        </div>`;
        }
    }

    // Delegación: abrir panel (acepta <a> y <button> con href o data-url)
    document.body.addEventListener('click', function (e) {
        const el = e.target.closest('#btn-crear-usuario, .btn-editar-usuario');
        if (!el) return;

        e.preventDefault();
        const href = el.getAttribute('href');
        const dataUrl = el.getAttribute('data-url') || (el.dataset ? el.dataset.url : null);
        const url = href || dataUrl;

        openPanel(url);
    });

    // Cerrar panel
    panel.addEventListener('click', function (e) {
        if (e.target.matches('#close-panel-btn') || e.target.matches('#cancel-btn')) {
            e.preventDefault();
            closePanel();
        }
    });
    overlay.addEventListener('click', closePanel);
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') closePanel();
    });

    // Interceptar cualquier submit dentro del panel (no dependas de un id concreto)
    panel.addEventListener('submit', function (e) {
        const form = e.target.closest('form');
        if (!form) return;

        // ¿Quién disparó el submit?
        const submitter = e.submitter || document.activeElement;

        e.preventDefault();

        // Si fue el botón de overrides, saltamos validación de jQuery
        const isOverrides = submitter && submitter.classList && submitter.classList.contains('save-overrides');

        if (!isOverrides && window.jQuery && window.jQuery.fn && window.jQuery.fn.valid) {
            if (!window.jQuery(form).valid()) return;
        }

        handleFormSubmit(form); // tu función fetch POST (incluye X-Requested-With y credentials)
    });

});
