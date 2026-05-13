/**
 * Super Admin sensitive actions: open modal → email PIN → submit original form.
 * Relies on Bootstrap 5 modal and session-backed verification (server).
 */
(function () {
    'use strict';

    var PURPOSE_INVITE = 'CreateSuperAdmin';
    var PURPOSE_TOGGLE = 'ToggleUserStatus';

    function getSendPinUrl() {
        var u = document.body && document.body.getAttribute('data-fm-superadmin-send-pin-url');
        return u || '';
    }

    function getAntiForgeryToken() {
        var modal = document.getElementById('fmSuperAdminConfirmModal');
        var input = modal ? modal.querySelector('input[name="__RequestVerificationToken"]') : null;
        if (!input)
            input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function setFeedback(el, msg, isError) {
        if (!el) return;
        el.textContent = msg || '';
        el.classList.toggle('text-danger', !!isError);
        el.classList.toggle('text-success', !!msg && !isError);
    }

    function digitsOnly(s) {
        return (s || '').replace(/\D/g, '');
    }

    function openModal(bsModal, ctx) {
        var titleEl = document.getElementById('fmSuperAdminConfirmModalLabel');
        var copyEl = document.getElementById('fmSuperAdminConfirmCopy');
        var pinInput = document.getElementById('fmSuperAdminPinInput');
        var feedback = document.getElementById('fmSuperAdminPinFeedback');
        var confirmBtn = document.getElementById('fmSuperAdminConfirmDoBtn');

        if (titleEl) titleEl.textContent = ctx.title || 'Confirm action';
        if (copyEl) copyEl.textContent = ctx.bodyText || '';
        if (pinInput) {
            pinInput.value = '';
            pinInput.focus();
        }
        setFeedback(feedback, '', false);
        if (confirmBtn) confirmBtn.disabled = true;

        window.__fmSuperAdminConfirmCtx = ctx;
        bsModal.show();
    }

    function wireOnce() {
        var modalEl = document.getElementById('fmSuperAdminConfirmModal');
        if (!modalEl || typeof bootstrap === 'undefined') return;

        var bsModal = bootstrap.Modal.getOrCreateInstance(modalEl);
        var pinInput = document.getElementById('fmSuperAdminPinInput');
        var sendBtn = document.getElementById('fmSuperAdminSendPinBtn');
        var confirmBtn = document.getElementById('fmSuperAdminConfirmDoBtn');
        var feedback = document.getElementById('fmSuperAdminPinFeedback');

        if (pinInput) {
            pinInput.addEventListener('input', function () {
                pinInput.value = digitsOnly(pinInput.value).slice(0, 6);
                var len = pinInput.value.length;
                if (confirmBtn) confirmBtn.disabled = len < 6;
            });
        }

        if (sendBtn) {
            sendBtn.addEventListener('click', function () {
                var ctx = window.__fmSuperAdminConfirmCtx;
                if (!ctx) return;

                var url = getSendPinUrl();
                if (!url) {
                    setFeedback(feedback, 'Missing send-PIN URL. Reload the page.', true);
                    return;
                }

                var token = getAntiForgeryToken();
                if (!token) {
                    setFeedback(feedback, 'Security token missing. Reload the page.', true);
                    return;
                }

                var fd = new FormData();
                fd.append('__RequestVerificationToken', token);
                fd.append('purpose', ctx.purpose);
                if (ctx.purpose === PURPOSE_TOGGLE && ctx.targetUserId != null)
                    fd.append('targetUserId', String(ctx.targetUserId));
                if (ctx.purpose === PURPOSE_INVITE && ctx.inviteEmail)
                    fd.append('inviteEmail', ctx.inviteEmail);

                sendBtn.disabled = true;
                setFeedback(feedback, 'Sending…', false);

                fetch(url, { method: 'POST', body: fd, credentials: 'same-origin' })
                    .then(function (r) { return r.json().catch(function () { return { success: false, message: 'Invalid response.' }; }); })
                    .then(function (data) {
                        if (data && data.success) {
                            setFeedback(feedback, data.message || 'Code sent.', false);
                            if (pinInput) pinInput.focus();
                        } else {
                            setFeedback(feedback, (data && data.message) || 'Could not send code.', true);
                        }
                    })
                    .catch(function () {
                        setFeedback(feedback, 'Network error. Try again.', true);
                    })
                    .finally(function () {
                        sendBtn.disabled = false;
                    });
            });
        }

        if (confirmBtn) {
            confirmBtn.addEventListener('click', function () {
                var ctx = window.__fmSuperAdminConfirmCtx;
                if (!ctx || !ctx.form) return;

                var pin = digitsOnly(pinInput ? pinInput.value : '');
                if (pin.length < 6) {
                    setFeedback(feedback, 'Enter the 6-digit code from your email.', true);
                    return;
                }

                var hidden = ctx.form.querySelector('input[name="ActionConfirmationPin"], input[name="actionConfirmationPin"]');
                if (!hidden) {
                    setFeedback(feedback, 'Form is missing the PIN field. Reload the page.', true);
                    return;
                }
                hidden.value = pin;
                bsModal.hide();
                if (typeof ctx.form.requestSubmit === 'function')
                    ctx.form.requestSubmit();
                else
                    ctx.form.submit();
            });
        }

        document.addEventListener('click', function (ev) {
            var t = ev.target.closest('[data-fm-superadmin-confirm-invite]');
            if (t) {
                ev.preventDefault();
                var form = document.querySelector(t.getAttribute('data-fm-superadmin-form') || '#fmInviteSuperAdminForm');
                if (!form || !form.checkValidity || !form.checkValidity()) {
                    if (form && typeof form.reportValidity === 'function') form.reportValidity();
                    return;
                }
                var emailInput = form.querySelector('#Email, [name="Email"]');
                var inviteEmail = emailInput ? emailInput.value.trim() : '';
                if (!inviteEmail) {
                    if (emailInput && typeof form.reportValidity === 'function') form.reportValidity();
                    return;
                }
                openModal(bsModal, {
                    purpose: PURPOSE_INVITE,
                    inviteEmail: inviteEmail,
                    form: form,
                    title: 'Confirm Super Admin invite',
                    bodyText: 'We will email a short verification code to your sign-in address. Enter it below to send the invitation.'
                });
                return;
            }

            t = ev.target.closest('[data-fm-superadmin-confirm-toggle]');
            if (t) {
                ev.preventDefault();
                var rowForm = t.closest('form');
                if (!rowForm) return;
                var uid = parseInt(t.getAttribute('data-fm-superadmin-target-user-id') || '0', 10);
                if (!uid) return;
                var active = t.getAttribute('data-fm-superadmin-target-active') === 'true';
                openModal(bsModal, {
                    purpose: PURPOSE_TOGGLE,
                    targetUserId: uid,
                    form: rowForm,
                    title: active ? 'Confirm suspend user' : 'Confirm activate user',
                    bodyText: 'We will email a verification code to your sign-in address. Enter it below to apply this change.'
                });
            }
        });
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', wireOnce);
    else
        wireOnce();
})();
