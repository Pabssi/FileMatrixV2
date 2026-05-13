/**
 * FileMatrix Authentication Orchestrator (auth-modal.js)
 * 
 * RESPONSIBILITY: Implements a "Seamless Auth" experience by handling 
 * Identity login/register forms via AJAX with reCAPTCHA v3 support.
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

                // Show loading state
                var btn = form.querySelector('button[type="submit"]');
                if (btn) {
                    btn.disabled = true;
                    var spinner = btn.querySelector('.btn-spinner');
                    if (!spinner) {
                        spinner = document.createElement('div');
                        spinner.className = 'btn-spinner';
                        btn.insertBefore(spinner, btn.firstChild);
                    }
                    spinner.style.display = 'inline-block';
                }

                var siteKey = form.getAttribute('data-recaptcha-sitekey');
                if (siteKey && window.grecaptcha) {
                    grecaptcha.ready(function() {
                        grecaptcha.execute(siteKey, {action: form.id === 'loginForm' ? 'login' : 'register'}).then(function(token) {
                            var data = new URLSearchParams();
                            for (var pair of new FormData(form)) {
                                data.append(pair[0], pair[1]);
                            }
                            data.append('g-recaptcha-response', token);
                            performSubmit(form, data, btn);
                        }).catch(function(err) {
                            console.error('reCAPTCHA execution failed', err);
                            var data = new URLSearchParams();
                            for (var pair of new FormData(form)) {
                                data.append(pair[0], pair[1]);
                            }
                            performSubmit(form, data, btn);
                        });
                    });
                } else {
                    var data = new URLSearchParams();
                    for (var pair of new FormData(form)) {
                        data.append(pair[0], pair[1]);
                    }
                    performSubmit(form, data, btn);
                }
            } catch (outer) {
                console.error(outer);
                var btn = e.target.querySelector('button[type="submit"]');
                if (btn) {
                    btn.disabled = false;
                    if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none';
                }
            }
        }, true);

        function performSubmit(form, data, btn) {
            fetch(form.action, {
                method: 'POST',
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'same-origin',
                body: data
            }).then(function (r) {
                if (!r.ok) {
                    return r.text().then(function (text) { return { __serverError: true, status: r.status, html: text }; });
                }
                var ct = r.headers.get('content-type') || '';
                if (ct.indexOf('application/json') !== -1) return r.json();
                return r.text().then(function (text) { return { html: text }; });
            }).then(function (js) {
                if (js && js.__serverError) {
                    console.error('Server error', js.status, js.html);
                    if (btn) { btn.disabled = false; if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none'; }
                    try {
                        var summaryEl = form.querySelector('[asp-validation-summary]') || form.querySelector('.text-danger');
                        if (summaryEl) {
                            var errorMsg = 'Server error: ' + js.status;
                            // If it's a 400, it's often a validation or anti-forgery issue
                            if (js.status === 400) {
                                errorMsg = 'Security validation failed (400). Please refresh the page and try again.';
                            }
                            summaryEl.innerHTML = '<div class="text-danger">' + errorMsg + '</div>';
                        } else {
                            alert('Server error: ' + js.status);
                        }
                    } catch (e) { console.error(e); }
                    return;
                }
                var summary = form.querySelector('[asp-validation-summary]') || form.querySelector('.text-danger');
                if (js && js.success) {
                    if (js.message) {
                        try {
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

                    if (form.id === 'registerForm') {
                        if (btn) { btn.disabled = false; if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none'; }
                        if (js.next === 'confirmation') {
                            window.location.href = '/Account/RegisterConfirmation';
                            return;
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

                    if (form.id === 'loginForm') {
                        if (js.redirectUrl) {
                            window.location.href = js.redirectUrl;
                            return;
                        }
                        window.location.href = (window.location.origin + '/Organizations');
                        return;
                    }
                }

                if (btn) { btn.disabled = false; if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none'; }

                if (js && js.errors) {
                    try {
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
                            var valmsg = form.querySelector('[data-valmsg-for="' + key + '"]');
                            if (!valmsg) {
                                var shortKey = key.split('.').pop();
                                valmsg = form.querySelector('[data-valmsg-for="' + shortKey + '"]');
                            }
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

                if (js && js.html) {
                    try {
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
                                } catch (ex) { }
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
                                } catch (ex) { }
                            }
                        }
                    } catch (ex) { console.error(ex); }
                }
            }).catch(function (err) {
                console.error(err);
                if (btn) { btn.disabled = false; if (btn.querySelector('.btn-spinner')) btn.querySelector('.btn-spinner').style.display = 'none'; }
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
