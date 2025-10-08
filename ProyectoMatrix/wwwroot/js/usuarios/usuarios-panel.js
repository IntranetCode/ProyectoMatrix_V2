document.addEventListener('DOMContentLoaded', function () {

    const panel = document.getElementById('slide-out-panel');
    const overlay = document.getElementById('panel-overlay');
    const panelContent = document.getElementById('panel-content');

    async function openPanel(url) {
        panelContent.innerHTML = '<div class="d-flex justify-content-center align-items-center h-100"><div class="spinner-border text-primary" role="status"><span class="visually-hidden">Loading...</span></div></div>';
        panel.classList.add('is-open');
        overlay.classList.add('is-open');

        try {
            const response = await fetch(url);
            if (!response.ok) throw new Error(`Error del servidor: ${response.status}`);

            const html = await response.text();
            panelContent.innerHTML = html;

            // ¡CLAVE! Vuelve a parsear el formulario para activar la validación de ASP.NET
            if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                const form = panelContent.querySelector('form');
                if (form) {
                    window.jQuery.validator.unobtrusive.parse(form);
                }
            }
        } catch (error) {
            console.error('Error al abrir el panel:', error);
            panelContent.innerHTML = `<div class="alert alert-danger m-3">Error al cargar el contenido.</div>`;
        }
    }

    function closePanel() {
        panel.classList.remove('is-open');
        overlay.classList.remove('is-open');
        setTimeout(() => { panelContent.innerHTML = ''; }, 300);
    }

    async function handleFormSubmit(form) {
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form)
                // El token anti-falsificación se incluye automáticamente por FormData si está en el form
            });

            if (response.redirected) {
                // Si el guardado fue exitoso, el servidor redirige. Recargamos la página principal.
                window.location.href = response.url;
            } else {
                // Si hubo un error de validación, el servidor devuelve el formulario con errores.
                const html = await response.text();
                panelContent.innerHTML = html;

                // Vuelve a activar la validación en el formulario recibido con errores.
                if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                    const newForm = panelContent.querySelector('form');
                    if (newForm) {
                        window.jQuery.validator.unobtrusive.parse(newForm);
                    }
                }
            }
        } catch (error) {
            console.error('Error al enviar el formulario:', error);
        }
    }

    // Usamos delegación en 'document.body' para que funcione siempre
    document.body.addEventListener('click', function (e) {
        const link = e.target.closest('#btn-crear-usuario, .btn-editar-usuario');
        if (link) {
            e.preventDefault();
            openPanel(link.href);
        }
    });

    panel.addEventListener('click', function (e) {
        if (e.target.matches('#close-panel-btn') || e.target.matches('#cancel-btn')) {
            closePanel();
        }
    });

    panel.addEventListener('submit', function (e) {
        if (e.target.matches('#user-form')) {
            e.preventDefault();
            const form = e.target;
            // Solo envía si la validación del lado del cliente pasa
            if (window.jQuery(form).valid()) {
                handleFormSubmit(form);
            }
        }
    });

    overlay.addEventListener('click', closePanel);
});