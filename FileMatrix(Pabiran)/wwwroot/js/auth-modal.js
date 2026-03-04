/**
 * FileMatrix Authentication Orchestrator (auth-modal.js)
 * 
 * RESPONSIBILITY: Implements a "Seamless Auth" experience by handling 
 * Identity login/register forms via AJAX, supporting partial DOM updates 
 * and inline validation without full page reloads.
 * 
 * DESIGN: 
 * 1. Success Handling: Triggers client-side redirects or tab switching.
 * 2. Error Handling: Maps server-side ModelState errors back to UI fields.
 * 3. Fallback: Supports full HTML response parsing for complex validation scenarios.
 */
(function () {
    function init() {
        document.addEventListener('submit', function (e) {
            try {
                var target = e.target || e.srcElement;
                if (!target) return;

                // Only handle our modal forms
                var form = target;
                if (form.id !== 'registerForm' && form.id !== 'loginForm') return;

                e.preventDefault();

                // run client-side validation if available
                if (window.jQuery && $(form).valid && !$(form).valid()) {
                    return; // unobtrusive will show messages
                }

                // Show loading state
                var btn = form.querySelector('button[type="submit"]');
                var originalBtnText = '';
                if (btn) {
                    btn.disabled = true;
                    // Try to find spinner or create one
                    var spinner = btn.querySelector('.btn-spinner');
                    if (!spinner) {
                        spinner = document.createElement('div');
                        spinner.className = 'btn-spinner';
                        btn.insertBefore(spinner, btn.firstChild);
                    }
                    spinner.style.display = 'inline-block';
                }

                var data = new FormData(form);

                fetch(form.action, {
                    method: 'POST',
                    headers: { 'X-Requested-With': 'XMLHttpRequest' },
                    credentials: 'same-origin',
                    body: data
                }).then(function (r) {
                    // If server returned a non-OK status, capture the body so we can
                    // show the server error inside the modal for easier debugging.
                    if (!r.ok) {
                        return r.text().then(function (text) { return { __serverError: true, status: r.status, html: text }; });
                    }
                    var ct = r.headers.get('content-type') || '';
                    if (ct.indexOf('application/json') !== -1) return r.json();
                    return r.text().then(function (text) { return { html: text }; });
                }).then(function (js) {
                    // Re-enable button if we are not redirecting success
                    // (If success redirect happens, page unloads anyway, but good practice to reset if logic continues)

                    if (js && js.__serverError) {
                        console.error('Server error', js.status, js.html);
                        if (btn) { btn.disabled = false; if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none'; }
                        // show server error text in the modal summary if present
                        try {
                            var summaryEl = form.querySelector('[asp-validation-summary]') || form.querySelector('.text-danger');
                            if (summaryEl) {
                                summaryEl.innerHTML = '<div class="text-danger">Server error: ' + js.status + '</div>' + '<div class="text-muted small">' + (js.html ? js.html : '') + '</div>';
                            } else {
                                alert('Server error: ' + js.status);
                            }
                        } catch (e) { console.error(e); }
                        return;
                    }
                    var summary = form.querySelector('[asp-validation-summary]') || form.querySelector('.text-danger');
                    if (js && js.success) {
                        // If server provided a message, show it inside the login pane of the modal
                        if (js.message) {
                            try {
                                // remove any existing auth modal alerts
                                var modalBody = document.querySelector('#authModal .modal-body');
                                if (modalBody) {
                                    var existing = modalBody.querySelectorAll('.auth-modal-alert');
                                    existing.forEach(function (el) { el.remove(); });
                                    var alert = document.createElement('div');
                                    alert.className = 'auth-modal-alert alert alert-success alert-dismissible fade show';
                                    alert.setAttribute('role', 'alert');
                                    alert.innerHTML = js.message +
                                        ' <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>';
                                    modalBody.insertBefore(alert, modalBody.firstChild);
                                }
                            } catch (e) { console.error(e); }
                        }

                        // For register success: show message and switch to login tab
                        if (form.id === 'registerForm') {
                            if (btn) { btn.disabled = false; if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none'; }

                            // If instructed to go to confirmation page
                            if (js.next === 'confirmation') {
                                window.location.href = '/Account/RegisterConfirmation';
                                return;
                            }

                            if (js.message) {
                                try {
                                    // remove any existing auth modal alerts
                                    var modalBody = document.querySelector('#authModal .modal-body');
                                    if (modalBody) {
                                        var existing = modalBody.querySelectorAll('.auth-modal-alert');
                                        existing.forEach(function (el) { el.remove(); });
                                        var alert = document.createElement('div');
                                        alert.className = 'auth-modal-alert alert alert-success alert-dismissible fade show';
                                        alert.setAttribute('role', 'alert');
                                        alert.innerHTML = js.message +
                                            ' <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>';
                                        modalBody.insertBefore(alert, modalBody.firstChild);
                                    }
                                } catch (e) { console.error(e); }
                            }

                            var loginTabEl = document.querySelector('#login-tab');
                            if (loginTabEl && window.bootstrap && bootstrap.Tab) {
                                var t = new bootstrap.Tab(loginTabEl);
                                t.show();
                            }
                            form.reset();
                            if (summary) summary.innerHTML = '';
                            return;
                        }

                        // For login success: navigate to redirectUrl if provided
                        if (form.id === 'loginForm') {
                            if (js.redirectUrl) {
                                window.location.href = js.redirectUrl;
                                return;
                            }
                            // fallback: switch to organizations
                            window.location.href = (window.location.origin + '/Organizations');
                            return;
                        }
                    }

                    // If we got here, it's either an error or partial update. Re-enable button.
                    if (btn) { btn.disabled = false; if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none'; }

                    if (js && js.errors) {
                        // Support two shapes: an array of error strings, or an object mapping
                        // field names to arrays of messages (returned by server ModelState).
                        try {
                            // clear previous field errors
                            var prevMsgs = form.querySelectorAll('[data-valmsg-for]');
                            prevMsgs.forEach(function (el) { el.innerHTML = ''; });
                        } catch (e) { }

                        if (Array.isArray(js.errors)) {
                            if (summary) {
                                summary.innerHTML = js.errors.map(function (s) { return '<div>' + s + '</div>'; }).join('');
                            } else {
                                alert(js.errors.join('\n'));
                            }
                            return;
                        }

                        if (js.errors && typeof js.errors === 'object') {
                            var nonField = [];
                            Object.keys(js.errors).forEach(function (key) {
                                var msgs = js.errors[key] || [];
                                if (!key || key === '' || key.toLowerCase() === 'model') {
                                    nonField = nonField.concat(msgs);
                                    return;
                                }

                                // Try to find a validation message element for this field
                                var valmsg = form.querySelector('[data-valmsg-for="' + key + '"]');
                                if (!valmsg) {
                                    // try without prefix (sometimes keys include model prefix)
                                    var shortKey = key.split('.').pop();
                                    valmsg = form.querySelector('[data-valmsg-for="' + shortKey + '"]');
                                }

                                // Handle email specific mapping if needed
                                if (!valmsg && key.toLowerCase().indexOf('email') !== -1) {
                                    valmsg = form.querySelector('[data-valmsg-for="Email"]');
                                }

                                if (valmsg) {
                                    valmsg.innerHTML = msgs.map(function (m) { return '<div>' + m + '</div>'; }).join('');
                                } else {
                                    nonField = nonField.concat(msgs);
                                }
                            });

                            if (nonField.length && summary) {
                                summary.innerHTML = nonField.map(function (s) { return '<div>' + s + '</div>'; }).join('');
                            } else if (nonField.length && !summary) {
                                alert(nonField.join('\n'));
                            }
                        }

                        return;
                    }

                    // STRATEGY: 'Fragment Patching'. 
                    // If the server returns HTML instead of JSON (common for validation errors), 
                    // we parse the fragment and replace only the relevant form in the modal.
                    if (js && js.html) {
                        try {
                            // Replace the modal pane that corresponds to the submitted form
                            var parser = new DOMParser();
                            var doc = parser.parseFromString(js.html, 'text/html');
                            var newRegisterForm = doc.querySelector('#registerForm');
                            var newLoginForm = doc.querySelector('#loginForm');

                            if (form.id === 'registerForm' && newRegisterForm) {
                                var modalPane = document.querySelector('#authModal .tab-pane#register');
                                if (modalPane) {
                                    modalPane.innerHTML = newRegisterForm.outerHTML;
                                    try {
                                        if (window.jQuery && $.validator && $.validator.unobtrusive) {
                                            $.validator.unobtrusive.parse(modalPane);
                                        }
                                    } catch (ex) { /* ignore */ }
                                }
                            }

                            if (form.id === 'loginForm' && newLoginForm) {
                                var modalPaneLogin = document.querySelector('#authModal .tab-pane#login');
                                if (modalPaneLogin) {
                                    modalPaneLogin.innerHTML = newLoginForm.outerHTML;
                                    try {
                                        if (window.jQuery && $.validator && $.validator.unobtrusive) {
                                            $.validator.unobtrusive.parse(modalPaneLogin);
                                        }
                                    } catch (ex) { /* ignore */ }
                                }
                            }
                        } catch (ex) { console.error(ex); }
                    }
                }).catch(function (err) {
                    console.error(err);
                    if (btn) { btn.disabled = false; if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none'; }
                });
            } catch (outer) {
                console.error(outer);
                var btn = e.target.querySelector('button[type="submit"]');
                if (btn) {
                    btn.disabled = false;
                    if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none';
                }
            }
        }, true);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
