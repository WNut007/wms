/*
  password-validator.js -- TD-117 shared live-validation behaviour.

  Vanilla JS, no framework dep. Exposes window.WmsPasswordValidator
  with two layers:

    1. Pure rule helpers (no DOM): used by tests / callers that just
       want a result without painting UI.
    2. DOM binding (.bindForm + .bindShowToggle): attaches listeners
       and updates the classes / text that auth-validator.css styles.

  Convention: views opt-in via `data-av-*` attributes. The init pass
  on DOMContentLoaded scans for [data-av-form] elements and wires
  them automatically; explicit calls also work for late-rendered
  forms (htmx, partial views, etc.).

  Markup contract for a form (all data-av-* on opt-in):

    <form data-av-form
          data-av-rules="length,upper,lower,digit"
          data-av-min-length="8">
      <input type="password" data-av-new />
      <ul data-av-checklist>
        <li data-av-rule="length">...<span data-av-count></span></li>
        <li data-av-rule="upper">...</li>
        ...
      </ul>
      <div data-av-strength>
        <div class="av-strength-bar">
          <div class="av-strength-segment"></div> x4
        </div>
        <div data-av-strength-label></div>
      </div>
      <input type="password" data-av-confirm />
      <div data-av-match-msg></div>
      <button type="submit" data-av-submit>Set password</button>
    </form>

  Each rule li (.av-rule) gets `.av-rule--ok` or `.av-rule--fail`
  toggled based on the live result. Strength wrapper gets one of
  .av-strength--weak / --fair / --good / --strong. Submit button is
  disabled until all required rules pass + confirm matches.
*/

(function () {
    'use strict';

    // ---------- Pure rule helpers (no DOM) ----------

    var rules = {
        length: function (value, min) {
            min = min || 8;
            return {
                met: value.length >= min,
                count: value.length,
                target: min
            };
        },
        upper: function (value) {
            return { met: /[A-Z]/.test(value) };
        },
        lower: function (value) {
            return { met: /[a-z]/.test(value) };
        },
        digit: function (value) {
            return { met: /\d/.test(value) };
        }
    };

    /**
     * Run all rules in `ruleNames` against `value`. Returns
     * { results: { length: {...}, upper: {...}, ... }, metCount }.
     */
    function evaluate(value, ruleNames, minLength) {
        var results = {};
        var metCount = 0;
        for (var i = 0; i < ruleNames.length; i++) {
            var name = ruleNames[i];
            var checker = rules[name];
            if (!checker) continue;
            var result = name === 'length'
                ? checker(value, minLength)
                : checker(value);
            results[name] = result;
            if (result.met) metCount++;
        }
        return { results: results, metCount: metCount };
    }

    /**
     * Map (metCount, totalRules) -> strength level string.
     *   0 rules met        -> null (no bar shown)
     *   25% of rules met   -> "weak"
     *   50% of rules met   -> "fair"
     *   75% of rules met   -> "good"
     *   100% of rules met  -> "strong"
     */
    function calculateStrength(metCount, totalRules) {
        if (metCount <= 0) return null;
        var ratio = metCount / totalRules;
        if (ratio <= 0.25) return 'weak';
        if (ratio <= 0.50) return 'fair';
        if (ratio <= 0.75) return 'good';
        return 'strong';
    }

    var STRENGTH_LABELS = {
        weak: 'Weak',
        fair: 'Fair',
        good: 'Good',
        strong: 'Strong'
    };

    /**
     * Match check.
     *   { ok: true }                -> new == confirm and not empty
     *   { ok: false, empty: true }  -> confirm field is blank
     *   { ok: false, empty: false } -> mismatch
     */
    function validateMatch(newPw, confirmPw) {
        if (!confirmPw || confirmPw.length === 0) {
            return { ok: false, empty: true };
        }
        return { ok: newPw === confirmPw, empty: false };
    }

    // ---------- DOM binding ----------

    /**
     * Wire a single form element. Idempotent — re-binding the same
     * element no-ops (the wired flag is stored on the element).
     */
    function bindForm(form) {
        if (!form || form.__avWired) return;
        form.__avWired = true;

        var ruleNames = (form.getAttribute('data-av-rules') || 'length,upper,lower,digit')
            .split(',').map(function (s) { return s.trim(); }).filter(Boolean);
        var minLength = parseInt(form.getAttribute('data-av-min-length') || '8', 10);

        var newInput = form.querySelector('[data-av-new]');
        var confirmInput = form.querySelector('[data-av-confirm]');
        var strength = form.querySelector('[data-av-strength]');
        var strengthLabel = form.querySelector('[data-av-strength-label]');
        var submit = form.querySelector('[data-av-submit]');
        var matchMsg = form.querySelector('[data-av-match-msg]');

        // Map rule name -> {li, countSpan} for fast updates.
        var ruleNodes = {};
        var liList = form.querySelectorAll('[data-av-rule]');
        for (var i = 0; i < liList.length; i++) {
            var li = liList[i];
            ruleNodes[li.getAttribute('data-av-rule')] = {
                li: li,
                count: li.querySelector('[data-av-count]')
            };
        }

        // Each rule starts unmarked (pending grey). Once the user
        // types anything and the rule fails, mark it red. Met rules
        // always show green. Tracking 'touched' prevents the "all
        // red on first render" feel.
        var touched = false;

        function paint() {
            var value = newInput ? newInput.value : '';
            if (value.length > 0) touched = true;

            var ev = evaluate(value, ruleNames, minLength);

            // Update each rule row.
            for (var name in ruleNodes) {
                if (!Object.prototype.hasOwnProperty.call(ruleNodes, name)) continue;
                var node = ruleNodes[name];
                var r = ev.results[name];
                node.li.classList.remove('av-rule--ok', 'av-rule--fail');
                if (r && r.met) {
                    node.li.classList.add('av-rule--ok');
                } else if (touched) {
                    node.li.classList.add('av-rule--fail');
                }
                // Live count for the length rule (e.g. "6/8").
                if (node.count && r && typeof r.count !== 'undefined') {
                    node.count.textContent =
                        r.met ? '' : '(' + r.count + '/' + r.target + ')';
                }
            }

            // Update strength bar.
            if (strength) {
                var level = calculateStrength(ev.metCount, ruleNames.length);
                strength.classList.remove(
                    'av-strength--weak', 'av-strength--fair',
                    'av-strength--good', 'av-strength--strong');
                if (level) strength.classList.add('av-strength--' + level);
                if (strengthLabel) {
                    strengthLabel.textContent = level ? STRENGTH_LABELS[level] : '';
                }
            }

            // Update confirm match state.
            var matchOk = true;
            if (confirmInput) {
                var match = validateMatch(value, confirmInput.value);
                var wrap = confirmInput.closest('.av-field') || confirmInput.parentElement;
                if (wrap) {
                    wrap.classList.remove('av-field--error', 'av-field--ok');
                    if (!match.empty && match.ok) wrap.classList.add('av-field--ok');
                    else if (!match.empty && !match.ok) wrap.classList.add('av-field--error');
                }
                if (matchMsg) {
                    if (match.empty) {
                        matchMsg.textContent = '';
                        matchMsg.className = 'av-field-msg';
                    } else if (match.ok) {
                        matchMsg.innerHTML =
                            '<i class="ti ti-check"></i>Passwords match';
                        matchMsg.className = 'av-field-msg av-field-msg--ok';
                    } else {
                        matchMsg.innerHTML =
                            '<i class="ti ti-alert-circle"></i>Passwords do not match';
                        matchMsg.className = 'av-field-msg av-field-msg--error';
                    }
                }
                matchOk = match.ok && !match.empty;
            }

            // Submit gating — all rules pass AND confirm matches (when
            // confirm field is present). When no confirm field, just
            // rules.
            if (submit) {
                var allRulesOk = ev.metCount === ruleNames.length;
                var ready = allRulesOk && (confirmInput ? matchOk : true);
                submit.disabled = !ready;
            }
        }

        if (newInput) newInput.addEventListener('input', paint);
        if (confirmInput) confirmInput.addEventListener('input', paint);

        // Initial paint (handles browser-prefilled values).
        paint();
    }

    /**
     * Wire a show-password toggle button.
     * Markup:
     *   <button type="button" data-av-toggle data-av-toggle-target="#newpw">
     *     <i class="ti ti-eye"></i>
     *   </button>
     */
    function bindShowToggle(button) {
        if (!button || button.__avToggleWired) return;
        button.__avToggleWired = true;

        var selector = button.getAttribute('data-av-toggle-target');
        var target = selector
            ? document.querySelector(selector)
            : button.parentElement && button.parentElement.querySelector('input');
        if (!target) return;

        button.addEventListener('click', function (e) {
            e.preventDefault();
            var icon = button.querySelector('i');
            if (target.type === 'password') {
                target.type = 'text';
                if (icon) {
                    icon.classList.remove('ti-eye');
                    icon.classList.add('ti-eye-off');
                }
                button.setAttribute('aria-label', 'Hide password');
            } else {
                target.type = 'password';
                if (icon) {
                    icon.classList.remove('ti-eye-off');
                    icon.classList.add('ti-eye');
                }
                button.setAttribute('aria-label', 'Show password');
            }
        });
    }

    /**
     * Initial scan — wire every [data-av-form] + [data-av-toggle]
     * present at DOMContentLoaded. Late-rendered surfaces (htmx,
     * Alpine x-html, etc.) can call init() again to rescan; the
     * idempotency flags prevent double-wiring.
     */
    function init(root) {
        root = root || document;
        var forms = root.querySelectorAll('[data-av-form]');
        for (var i = 0; i < forms.length; i++) bindForm(forms[i]);
        var toggles = root.querySelectorAll('[data-av-toggle]');
        for (var j = 0; j < toggles.length; j++) bindShowToggle(toggles[j]);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(); });
    } else {
        init();
    }

    // Public API.
    window.WmsPasswordValidator = {
        rules: rules,
        evaluate: evaluate,
        calculateStrength: calculateStrength,
        validateMatch: validateMatch,
        bindForm: bindForm,
        bindShowToggle: bindShowToggle,
        init: init
    };
}());
