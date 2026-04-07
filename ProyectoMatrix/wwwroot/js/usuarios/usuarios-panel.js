document.addEventListener('DOMContentLoaded', function () {
    const panel = document.getElementById('slide-out-panel');
    const overlay = document.getElementById('panel-overlay');
    const panelContent = document.getElementById('panel-content');

    if (!panel || !overlay || !panelContent) {
        console.error('[usuarios-panel] Faltan elementos del panel.');
        return;
    }

    function showPanel() {
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

    function inicializarSelect2Jefe() {
        // Esperar a que Select2 esté disponible (cargado desde la partial)
        let intentos = 0;
        const intervalo = setInterval(function () {
            intentos++;

            if (window.jQuery && typeof window.jQuery.fn.select2 !== 'undefined') {
                clearInterval(intervalo);

                const $sel = window.jQuery('#selectJefe');
                if (!$sel.length) return;

                if ($sel.hasClass('select2-hidden-accessible')) {
                    $sel.select2('destroy');
                }

                $sel.select2({
                    theme: 'bootstrap-5',
                    width: '100%',
                    placeholder: '-- Escriba para buscar jefe... --',
                    minimumInputLength: 1,
                    allowClear: true,
                    dropdownParent: window.jQuery('body'),
                    ajax: {
                        url: '/Usuarios/BuscarPersonasJefes',
                        dataType: 'json',
                        delay: 300,
                        data: function (params) { return { term: params.term }; },
                        processResults: function (data) {
                            console.log('✅ Select2 resultados:', data);
                            return data;
                        },
                        error: function (xhr, status, err) {
                            console.error('❌ Select2 AJAX error:', status, err);
                        }
                    }
                }).on('select2:open', function () {
                    setTimeout(function () {
                        document.querySelector('.select2-container--open .select2-search__field')?.focus();
                    }, 50);
                });

                console.log('✅ Select2 inicializado');
            }

            if (intentos >= 30) {
                clearInterval(intervalo);
                console.error('❌ Select2 no cargó en 3 segundos');
            }
        }, 100);
    }

    async function openPanel(url) {
        if (!url) {
            console.error('[usuarios-panel] URL vacía');
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

            if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                panelContent.querySelectorAll('form').forEach(f => window.jQuery.validator.unobtrusive.parse(f));
            }

            inicializarSelect2Jefe();

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

            if (res.redirected) {
                window.location.href = res.url;
                return;
            }

            const html = await res.text();
            panelContent.innerHTML = html;

            if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                panelContent.querySelectorAll('form').forEach(f => window.jQuery.validator.unobtrusive.parse(f));
            }

            inicializarSelect2Jefe();

        } catch (err) {
            console.error('Error al enviar el formulario:', err);
            panelContent.innerHTML = `
            <div class="alert alert-danger m-3">
                No se pudo enviar el formulario.<br>
                <small>${(err && err.message) ? err.message.replace(/</g, '&lt;') : 'Error desconocido'}</small>
            </div>`;
        }
    }

    // Abrir panel
    document.body.addEventListener('click', function (e) {
        const el = e.target.closest('#btn-crear-usuario, .btn-editar-usuario');
        if (!el) return;

        e.preventDefault();
        const url = el.getAttribute('href') || el.getAttribute('data-url') || (el.dataset ? el.dataset.url : null);
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

    // Submit dentro del panel
    panel.addEventListener('submit', function (e) {
        const form = e.target.closest('form');
        if (!form) return;

        const submitter = e.submitter || document.activeElement;
        e.preventDefault();

        const isOverrides = submitter && submitter.classList && submitter.classList.contains('save-overrides');

        if (!isOverrides && window.jQuery && window.jQuery.fn && window.jQuery.fn.valid) {
            if (!window.jQuery(form).valid()) return;
        }

        handleFormSubmit(form);
    });
});