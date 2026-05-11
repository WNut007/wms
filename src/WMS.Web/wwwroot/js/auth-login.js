/*
  auth-login.js -- TD-116. AJAX login flow with 4 UI states.

  Hijacks submit on [data-av-login-form], posts via fetch with the
  X-Requested-With + Accept headers AuthController.Login looks for,
  reads the structured JSON response, and renders one of:

    State 1: Format invalid     -> red email + inline message
    State 2: Auth failed        -> danger banner +
                                   "N attempts remaining" chip
    State 3: Account locked     -> lock banner + live countdown
    State 4: Submitting         -> button spinner, inputs disabled

  Pure vanilla JS. Uses password-validator.css's `.av-*` namespace.

  Markup contract (Login.cshtml wires these data-av-* attrs):

    <form data-av-login-form asp-action="Login" method="post">
      <input type="hidden" name="__RequestVerificationToken" ... />
      <div data-av-login-banner></div>
      <input type="email" name="Email" data-av-email />
      <div class="av-field-msg" data-av-email-msg></div>
      <input type="password" name="Password" data-av-password />
      <button type="submit" data-av-submit>...</button>
    </form>

  The form's native `action` and `method` are used for the fetch URL.
*/

(function () {
    'use strict';

    // Loose-enough email check for client-side preview. Server-side
    // EmailAddress DataAnnotation is the source of truth on submit.
    var EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

    /**
     * Format milliseconds-remaining as "14 min 32 sec" / "32 sec".
     * Drops the minutes part when < 1 min.
     */
    function formatRemaining(ms) {
        if (ms <= 0) return '0 sec';
        var totalSec = Math.ceil(ms / 1000);
        var min = Math.floor(totalSec / 60);
        var sec = totalSec - min * 60;
        if (min === 0) return sec + ' sec';
        return min + ' min ' + sec + ' sec';
    }

    function buildBanner(variant, iconClass, title, subtitle) {
        // Build via createElement to avoid HTML-injection from the
        // server-provided message (defensive even though the
        // controller's messages are static literals today).
        var wrap = document.createElement('div');
        wrap.className = 'av-banner av-banner--' + variant;

        var icon = document.createElement('i');
        icon.className = 'ti ' + iconClass + ' av-banner-icon';
        wrap.appendChild(icon);

        var body = document.createElement('div');
        body.className = 'av-banner-body';
        var t = document.createElement('span');
        t.className = 'av-banner-title';
        t.textContent = title;
        body.appendChild(t);
        if (subtitle) {
            var s = document.createElement('span');
            s.className = 'av-banner-subtitle';
            s.appendChild(subtitle);   // already a node
            body.appendChild(s);
        }
        wrap.appendChild(body);
        return wrap;
    }

    function clearBanner(slot) {
        if (slot) slot.innerHTML = '';
    }

    function setLoading(button, on) {
        if (!button) return;
        button.classList.toggle('av-submit--loading', !!on);
        button.disabled = !!on;
    }

    function setFormLocked(form, locked) {
        if (!form) return;
        form.classList.toggle('av-form--locked', !!locked);
    }

    /**
     * Wire one login form.
     */
    function bindForm(form) {
        if (!form || form.__avLoginWired) return;
        form.__avLoginWired = true;

        var emailInput   = form.querySelector('[data-av-email]');
        var emailMsg     = form.querySelector('[data-av-email-msg]');
        var passwordInput= form.querySelector('[data-av-password]');
        var bannerSlot   = form.querySelector('[data-av-login-banner]');
        var submitBtn    = form.querySelector('[data-av-submit]');
        var countdownTimer = null;   // setInterval handle for lockout

        // ---------- live email format ----------
        function paintEmail() {
            if (!emailInput) return;
            var value = emailInput.value.trim();
            var wrap = emailInput.closest('.av-field') || emailInput.parentElement;
            if (!wrap) return;
            wrap.classList.remove('av-field--error', 'av-field--ok');
            if (emailMsg) { emailMsg.textContent = ''; emailMsg.className = 'av-field-msg'; }

            if (value.length === 0) return;   // neutral until typed
            if (EMAIL_REGEX.test(value)) {
                wrap.classList.add('av-field--ok');
            } else {
                wrap.classList.add('av-field--error');
                if (emailMsg) {
                    emailMsg.innerHTML =
                        '<i class="ti ti-alert-circle"></i>' +
                        'Please enter a valid email address (e.g., name@company.com)';
                    emailMsg.className = 'av-field-msg av-field-msg--error';
                }
            }
        }
        if (emailInput) emailInput.addEventListener('input', paintEmail);

        // ---------- countdown helper ----------
        function startCountdown(lockoutUntilIso) {
            stopCountdown();
            var until = new Date(lockoutUntilIso).getTime();
            if (isNaN(until)) return;

            function tick() {
                var remaining = until - Date.now();
                var span = bannerSlot.querySelector('[data-av-countdown]');
                if (remaining <= 0) {
                    stopCountdown();
                    setFormLocked(form, false);
                    clearBanner(bannerSlot);
                    bannerSlot.appendChild(buildBanner(
                        'success',
                        'ti-circle-check',
                        'You can try signing in again now.',
                        null));
                    return;
                }
                if (span) span.textContent = formatRemaining(remaining);
            }
            tick();
            countdownTimer = setInterval(tick, 1000);
        }

        function stopCountdown() {
            if (countdownTimer !== null) {
                clearInterval(countdownTimer);
                countdownTimer = null;
            }
        }

        // ---------- state renderers ----------
        function renderFormatInvalid(json) {
            // Server flagged a model-state error. If it points at a
            // specific field, paint that field; otherwise generic.
            clearBanner(bannerSlot);
            if (json.field && json.field.toLowerCase() === 'email') {
                paintEmail();
            } else if (emailInput && json.message) {
                if (emailMsg) {
                    emailMsg.innerHTML =
                        '<i class="ti ti-alert-circle"></i>' + json.message;
                    emailMsg.className = 'av-field-msg av-field-msg--error';
                }
            }
        }

        function renderAuthFailed(json) {
            clearBanner(bannerSlot);
            var sub = document.createElement('span');
            sub.textContent = 'Check your credentials and try again.';
            if (typeof json.attemptsRemaining === 'number') {
                var chip = document.createElement('span');
                chip.className = 'av-attempts';
                chip.textContent = json.attemptsRemaining +
                    (json.attemptsRemaining === 1 ? ' attempt left' : ' attempts left');
                sub.appendChild(document.createTextNode(' '));
                sub.appendChild(chip);
            }
            bannerSlot.appendChild(buildBanner(
                'danger',
                'ti-alert-triangle',
                json.message || 'Invalid email or password',
                sub));

            // Clear + refocus password.
            if (passwordInput) {
                passwordInput.value = '';
                passwordInput.focus();
            }
        }

        function renderLocked(json) {
            clearBanner(bannerSlot);
            var sub = document.createElement('span');
            sub.textContent = 'Too many failed attempts. Try again in ';
            var countdown = document.createElement('span');
            countdown.className = 'av-banner-countdown';
            countdown.setAttribute('data-av-countdown', '');
            countdown.textContent = '...';
            sub.appendChild(countdown);
            sub.appendChild(document.createTextNode('. '));
            var contact = document.createElement('a');
            contact.href = '#';
            contact.textContent = 'Contact administrator to unlock';
            sub.appendChild(contact);

            bannerSlot.appendChild(buildBanner(
                'danger',
                'ti-lock',
                json.message || 'Account temporarily locked',
                sub));

            setFormLocked(form, true);
            startCountdown(json.lockoutUntil);
        }

        function renderRateLimited(json) {
            clearBanner(bannerSlot);
            var sub = document.createElement('span');
            sub.textContent = 'Slow down — wait a minute before trying again.';
            bannerSlot.appendChild(buildBanner(
                'warning',
                'ti-clock-pause',
                json.message || 'Too many attempts',
                sub));
        }

        // ---------- submit handler ----------
        form.addEventListener('submit', async function (e) {
            // Don't intercept if the user submitted from a non-JS path
            // (shouldn't happen — listener only attaches when JS runs).
            e.preventDefault();
            setLoading(submitBtn, true);

            try {
                var formData = new FormData(form);
                var response = await fetch(form.action || window.location.href, {
                    method: form.method || 'POST',
                    headers: {
                        'Accept': 'application/json',
                        'X-Requested-With': 'XMLHttpRequest'
                    },
                    body: formData,
                    credentials: 'same-origin'
                });

                // Server-side antiforgery rejection / 5xx -> let the
                // browser surface it (rare; user can re-submit).
                if (!response.ok && response.status !== 400 && response.status !== 200) {
                    throw new Error('Server returned ' + response.status);
                }

                var json = await response.json();

                switch (json.status) {
                    case 'ok':
                    case 'must_change':
                        window.location.href = json.redirectUrl || '/';
                        return;   // keep button in loading state during navigation
                    case 'format_invalid':
                        renderFormatInvalid(json);
                        break;
                    case 'auth_failed':
                        renderAuthFailed(json);
                        break;
                    case 'locked':
                        renderLocked(json);
                        break;
                    case 'rate_limited':
                        renderRateLimited(json);
                        break;
                    default:
                        // Unknown — fall back to generic.
                        renderAuthFailed({ message: 'Sign-in failed. Please try again.' });
                }
            } catch (err) {
                // Network failure / JSON parse — fall back to a generic
                // banner. The full-page submit would have shown a stack
                // trace; we keep it operator-friendly.
                clearBanner(bannerSlot);
                var sub = document.createElement('span');
                sub.textContent = 'Network problem. Check your connection and try again.';
                bannerSlot.appendChild(buildBanner(
                    'danger', 'ti-wifi-off', 'Could not reach server', sub));
            } finally {
                setLoading(submitBtn, false);
            }
        });
    }

    function init(root) {
        root = root || document;
        var forms = root.querySelectorAll('[data-av-login-form]');
        for (var i = 0; i < forms.length; i++) bindForm(forms[i]);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(); });
    } else {
        init();
    }

    window.WmsAuthLogin = { bindForm: bindForm, init: init };
}());
