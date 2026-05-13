/* ==================================================================
   grid-common.js — shared helpers for grid factories (Block 4.5.2)

   Designed for reuse across PO Lines (this block) and future Block 5
   Customer multi-address picker, plus GR / SO / Transfer Lines grids
   when they migrate to the same pattern at rule-of-three refactor time.

   Exposes two helpers under the `WmsGrid` global namespace:

     WmsGrid.setupTomSelect(input, opts)
       Wraps Tom Select 2.x with our server-side load shape (flat JSON
       array per /Products/SearchAsync) and a few sensible defaults.
       Caller owns the option/item HTML via render functions — no
       hardcoded product-shape, so an address picker can reuse the same
       wrapper with its own renderOption.

     WmsGrid.setupSortable(tbody, opts)
       Wraps SortableJS 1.15.x with our drag-handle convention. Caller
       supplies onEnd({oldIndex, newIndex}) and the wrapper handles
       options translation + animation defaults.

   Both helpers return the underlying instance so callers can call
   .destroy() on cleanup or .setValue() etc. on the Tom Select.

   Loaded per-view (NOT in _OfficeLayout) since the underlying libs are
   ~110 KB combined. Add to layout when 3+ pages need it.
   ================================================================== */

(function (window) {
    'use strict';

    // ----------------------------------------------------------------
    // setupTomSelect
    // ----------------------------------------------------------------
    //
    // opts:
    //   valueField     — string, e.g. 'id'                    (required)
    //   labelField     — string, e.g. 'code'                  (required)
    //   searchField    — string[], e.g. ['code','name']       (required)
    //   searchUrl      — string, e.g. '/Products/SearchAsync' (required)
    //   extraParams    — () => Record<string, string>         (optional)
    //                    Called per request. Lets the warehouse id
    //                    track the form's Warehouse <select> live.
    //   take           — number, default 50                   (optional)
    //                    Appended as ?take=N. Cap honored server-side.
    //   loadThrottleMs — number, default 300                  (optional)
    //   placeholder    — string                               (optional)
    //   maxOptions     — number, default 50                   (optional)
    //   renderOption   — (item) => string                     (required)
    //                    HTML for one row in the dropdown.
    //   renderItem     — (item) => string                     (optional)
    //                    HTML for the selected row in the closed combo.
    //                    Defaults to <span>{labelField value}</span>.
    //   onChange       — (value) => void                      (optional)
    //   onClear        — () => void                           (optional)
    //
    // Returns the TomSelect instance.
    function setupTomSelect(input, opts) {
        if (typeof window.TomSelect !== 'function') {
            throw new Error('Tom Select not loaded — vendor it in via /lib/tom-select/');
        }
        if (!opts || !opts.searchUrl || !opts.renderOption) {
            throw new Error('setupTomSelect requires opts.searchUrl and opts.renderOption');
        }

        const labelField = opts.labelField || 'code';
        const take       = opts.take || 50;

        // Build query string per request so extraParams() can be
        // re-evaluated each time (e.g., warehouse changes mid-edit).
        function buildQuery(query) {
            const params = new URLSearchParams();
            if (query) params.set('q', query);
            params.set('take', String(take));
            const extras = (typeof opts.extraParams === 'function')
                ? (opts.extraParams() || {})
                : {};
            for (const [k, v] of Object.entries(extras)) {
                if (v != null && v !== '') params.set(k, String(v));
            }
            return params.toString();
        }

        const config = {
            valueField:       opts.valueField || 'id',
            labelField:       labelField,
            searchField:      opts.searchField || [labelField],
            placeholder:      opts.placeholder || 'Search…',
            maxOptions:       opts.maxOptions || 50,
            loadThrottle:     opts.loadThrottleMs != null ? opts.loadThrottleMs : 300,
            // Disable client-side scoring — server already filtered +
            // sorted. Re-sorting locally would shuffle the server's
            // intended order (e.g., code ASC).
            sortField:        { field: '$order' },
            // Open dropdown on focus so operators see "what's here"
            // without typing first.
            openOnFocus:      true,
            // Tom Select default would clear the input on blur if no
            // option is selected; preserve operator's typed value so
            // they can correct a typo.
            create:           false,
            preload:          false,
            load: function (query, callback) {
                fetch(opts.searchUrl + '?' + buildQuery(query), {
                    headers: { 'Accept': 'application/json' },
                    credentials: 'same-origin'
                })
                    .then(function (r) {
                        if (!r.ok) {
                            // 400 (no warehouse context) etc. — surface
                            // empty list; consumer's renderEmpty owns
                            // the operator-facing message.
                            callback();
                            return null;
                        }
                        return r.json();
                    })
                    .then(function (data) {
                        if (data) callback(data);
                    })
                    .catch(function () {
                        // Network error — empty list, Tom Select shows
                        // "No results found".
                        callback();
                    });
            },
            render: {
                option: function (item, escape) {
                    return opts.renderOption(item, escape);
                },
                item: function (item, escape) {
                    return opts.renderItem
                        ? opts.renderItem(item, escape)
                        : '<span>' + escape(item[labelField] || '') + '</span>';
                }
            },
            onChange: function (value) {
                if (typeof opts.onChange === 'function') opts.onChange(value);
                if (!value && typeof opts.onClear === 'function') opts.onClear();
            }
        };

        return new window.TomSelect(input, config);
    }

    // ----------------------------------------------------------------
    // setupSortable
    // ----------------------------------------------------------------
    //
    // opts:
    //   handleSelector — string, default '.drag-handle'       (optional)
    //   animation      — number ms, default 150               (optional)
    //   onEnd          — ({oldIndex, newIndex}) => void       (required)
    //                    Fires only when the row actually moved. No-op
    //                    drops (drop on same row) are filtered.
    //   ghostClass     — string, default 'sortable-ghost'     (optional)
    //   chosenClass    — string, default 'sortable-chosen'    (optional)
    //   dragClass      — string, default 'sortable-drag'      (optional)
    //   disabled       — boolean, default false               (optional)
    //                    Edit-when-locked path — skip drag-drop wiring
    //                    entirely (PO Edit when ReceivedQuantity > 0).
    //   swapThreshold  — number 0.0..1.0, default 0.5         (optional)
    //                    d.2.3.c — lower threshold = swap triggers on
    //                    partial overlap (easier to land for tables).
    //                    Sortable's library default is 1.0 which
    //                    requires full overlap; 0.5 is the documented
    //                    sweet spot for vertical row reorder.
    //
    // Returns the Sortable instance.
    function setupSortable(tbody, opts) {
        if (typeof window.Sortable !== 'function') {
            throw new Error('SortableJS not loaded — vendor it in via /lib/sortablejs/');
        }
        if (!opts || typeof opts.onEnd !== 'function') {
            throw new Error('setupSortable requires opts.onEnd callback');
        }

        return new window.Sortable(tbody, {
            handle:        opts.handleSelector || '.drag-handle',
            animation:     opts.animation != null ? opts.animation : 150,
            ghostClass:    opts.ghostClass  || 'sortable-ghost',
            chosenClass:   opts.chosenClass || 'sortable-chosen',
            dragClass:     opts.dragClass   || 'sortable-drag',
            disabled:      !!opts.disabled,
            swapThreshold: opts.swapThreshold != null ? opts.swapThreshold : 0.5,
            onEnd: function (evt) {
                // Filter no-op drops (e.g., drag and release on the same
                // row) — saves a pointless reorder callback that would
                // otherwise fire AJAX in the Edit flow for nothing.
                if (evt.oldIndex === evt.newIndex) return;

                // d.2.3.c.1 — revert Sortable's DOM mutation BEFORE
                // calling opts.onEnd. With Alpine x-for `<template>`
                // bindings, Sortable's DOM mutation + a subsequent
                // array splice both try to be the source of truth for
                // DOM order, and Alpine's keyed diffing has to
                // reconcile them. Result is non-deterministic across
                // Alpine versions + Sortable timing — bug surfaced as
                // "drag visual works but POST carries stale DisplayOrder"
                // in d.2.3.c smoke.
                //
                // Revert makes Alpine the sole source of truth: it
                // sees the array change + re-renders DOM cleanly.
                // Move-up / Move-down menu actions already worked
                // because they only mutate the array — no DOM
                // mutation to fight.
                //
                // Algorithm: if item moved DOWN (oldIndex < newIndex),
                // re-insert before the child currently at oldIndex
                // (which is the original next-sibling after the move).
                // If moved UP, re-insert before children[oldIndex+1]
                // or append if oldIndex was last; appendChild handles
                // the null reference correctly.
                var parent = evt.from;
                if (evt.oldIndex < evt.newIndex) {
                    parent.insertBefore(evt.item, parent.children[evt.oldIndex]);
                } else {
                    var ref = parent.children[evt.oldIndex + 1] || null;
                    parent.insertBefore(evt.item, ref);
                }

                opts.onEnd({ oldIndex: evt.oldIndex, newIndex: evt.newIndex });
            }
        });
    }

    // ----------------------------------------------------------------
    // mountSelectsInRow — Block 4.5.2 chunk c.1.1 hotfix
    // ----------------------------------------------------------------
    //
    // Canonical entry point for wiring Tom Select to every combo cell
    // in a single grid row. Designed for reuse across:
    //   - po-line-grid.js addLine / duplicateRow / initGrid (chunk c.1.1)
    //   - po-line-grid.js UoM cell (chunk c.3 will add 'uom' kind)
    //   - Edit.cshtml's server-hydrated row walk (chunk d)
    //   - Block 5 Customer multi-address picker
    //
    // rowEl: the row element (typically a <tr>) containing the selects.
    // cells: an object mapping kind → { selector, config } where:
    //   selector — CSS selector to find the <select> within rowEl
    //   config   — opts for setupTomSelect (valueField, searchUrl, etc.)
    //
    // Skips selects that already have a Tom Select instance attached
    // (sel.tomselect truthy) — idempotent under x-init double-fire +
    // belt-and-suspenders explicit init in factory methods.
    //
    // Returns: { kind: tomSelectInstance } for each cell that mounted.
    // Caller is responsible for stashing instances in its own Map keyed
    // by row.key (Alpine factory state — outside this helper's scope).
    function mountSelectsInRow(rowEl, cells) {
        if (!rowEl || !cells) return {};
        const out = {};
        for (const [kind, spec] of Object.entries(cells)) {
            if (!spec || !spec.selector) continue;
            const sel = rowEl.querySelector(spec.selector);
            // Skip if already mounted (Tom Select sets `.tomselect` on
            // the element it wraps).
            if (sel && !sel.tomselect) {
                out[kind] = setupTomSelect(sel, spec.config);
            }
        }
        return out;
    }

    window.WmsGrid = {
        setupTomSelect:    setupTomSelect,
        setupSortable:     setupSortable,
        mountSelectsInRow: mountSelectsInRow
    };
})(window);
