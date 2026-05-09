/* ==================================================================
   wmsf-form-state.js — Alpine helper for V1 Section-tab forms (Phase 8)

   Used by Products / Customers / Warehouses Create + Edit views.
   Owns: tab navigation, per-section status-dot state, progress
   counter, and "X required" badge state.

   Wire at the form root:

     <form x-data="wmsfFormState({
         totalSteps: 4,
         initialStep: 1,
         serverErrors: { 1: false, 2: true, ... },
         required:     { 1: ['Code', 'Name'], 2: [...], ... }
     })">

   Then bind:
     - x-text="step", x-show="step === N"
     - :class="dotClass(N)" on .wmsf-dot
     - x-text="sectionRequiredHint(N)" + :class="{ 'is-empty': sectionComplete(N) }"
     - @click="goTo(N)" on tabs
     - @click="next()" / @click="prev()" on action bar
     - @input="onTouch('FieldName')" on each input

   Alpine reactivity: dotClass / sectionComplete / sectionRequiredHint
   read this.touched (proxied → reactive) AND DOM values (not reactive).
   Touching a field flips touched[name] which triggers re-evaluation;
   the DOM read inside _fieldValue then sees the current value. So
   the chain works as long as every input has @input="onTouch(...)".
   ================================================================== */

function wmsfFormState(opts) {
    const required = opts.required || {};
    const serverErrors = Object.assign({}, opts.serverErrors || {});
    // mode: 'create' (default) or 'edit'.
    //   create dot precedence: error > complete > touched > untouched
    //   edit   dot precedence: error > touched > complete (touched =
    //                          "user changed something — unsaved")
    const isEdit = opts.mode === 'edit';

    return {
        step: opts.initialStep || 1,
        totalSteps: opts.totalSteps || 1,
        touched: {},          // { 'FieldName': true } once user types
        serverErrors,         // { stepNum: bool } — sticky from POST until user touches a field
        required,             // { stepNum: ['fieldName', ...] } — exposed so per-form
                              //   overrides (e.g. Customer Create's B2B requiredFor)
                              //   can fall back to it via this.required[sec].
        mode: isEdit ? 'edit' : 'create',

        goTo(n) {
            if (n < 1 || n > this.totalSteps) return;
            this.step = n;
            // Scroll to top of pane on tab change so long forms don't
            // start halfway down the previous pane's last field.
            this.$root.scrollIntoView({ behavior: 'smooth', block: 'start' });
        },

        next() { this.goTo(Math.min(this.step + 1, this.totalSteps)); },
        prev() { this.goTo(Math.max(this.step - 1, 1)); },

        onTouch(field) {
            this.touched[field] = true;
            // First touch on a section clears its server-error flag —
            // the user is actively fixing it.
            const sec = this._sectionOf(field);
            if (sec != null) this.serverErrors[sec] = false;
        },

        // Internal: which section does this field belong to?
        _sectionOf(field) {
            for (const sec of Object.keys(this.required)) {
                if (this.required[sec].indexOf(field) !== -1) return sec;
            }
            return null;
        },

        // Internal: read a field's current value from the DOM.
        // Handles input / select / textarea / checkbox uniformly.
        _fieldValue(name) {
            const root = this.$root;
            if (!root) return '';
            const el = root.querySelector('[name="' + name + '"]');
            if (!el) return '';
            if (el.type === 'checkbox' || el.type === 'radio') {
                // For required radios with multiple inputs, querySelector
                // returns the first; check for ANY checked sibling.
                if (el.type === 'radio') {
                    const checked = root.querySelector('[name="' + name + '"]:checked');
                    return checked ? 'on' : '';
                }
                return el.checked ? 'on' : '';
            }
            return (el.value || '').trim();
        },

        // Computed: does this section have all required fields filled?
        sectionComplete(sec) {
            const fields = this.required[sec] || [];
            if (fields.length === 0) return true;
            return fields.every(f => this._fieldValue(f).length > 0);
        },

        // Computed: has the user touched any required field of this section?
        sectionTouched(sec) {
            const fields = this.required[sec] || [];
            return fields.some(f => this.touched[f]);
        },

        // Computed: badge text — "X required" or "All set".
        sectionRequiredHint(sec) {
            const fields = this.required[sec] || [];
            if (fields.length === 0) return '';
            const filled = fields.filter(f => this._fieldValue(f).length > 0).length;
            const missing = fields.length - filled;
            if (missing === 0) return 'All set';
            return missing + (missing === 1 ? ' required' : ' required');
        },

        // Computed: which CSS modifier class does this section's dot wear?
        // Edit mode flips touched-vs-complete precedence: a "touched"
        // section means the user CHANGED something — amber stays until
        // save. Create mode prefers "complete" so the dot turns green
        // and stays green once the section is done.
        dotClass(sec) {
            if (this.serverErrors[sec]) return 'is-error';
            if (isEdit) {
                if (this.sectionTouched(sec)) return 'is-progress';
                if (this.sectionComplete(sec)) return 'is-complete';
                return '';
            }
            if (this.sectionComplete(sec)) return 'is-complete';
            if (this.sectionTouched(sec)) return 'is-progress';
            return '';
        }
    };
}

window.wmsfFormState = wmsfFormState;
