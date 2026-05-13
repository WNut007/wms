/* ==================================================================
   po-line-grid.js — Alpine factory for the PO Lines grid (Block 4.5.2)

   Composes with wmsfFormState via Object.assign:

     <form x-data="Object.assign(wmsfFormState({...}), poLineGrid(window.__poCreateConfig))">

   The merged object preserves wmsfFormState's step navigation +
   section dots while adding the grid's lines[] state + handlers.

   Config (opts) MUST be hoisted to a <script> block above the form
   per memory feedback_json_in_attribute_script_hoist.md — never
   inject JSON via @Html.Raw into x-data="..." (the literal " in JSON
   closes the attribute prematurely; bug bit twice — hotfix 745900a
   then chunk c c7fdd43, reverted as 215b389).

   ROW MOUNTING (Block 4.5.2 chunk c.1.1 hotfix):
     Tom Select wiring is centralized through _mountRow(rowEl, key)
     which calls WmsGrid.mountSelectsInRow with the configs for all
     combo cells in the row. Three call sites converge on it:
       1. initGrid() walks initial rows on mount (defensive against
          x-init not firing in some Alpine versions; idempotent via
          tsInstances.has(key) guard)
       2. addLine() explicitly mounts the new row's selects after
          $nextTick (was missing in the original c — caused the row-2
          dropdown not rendering bug, reported as smoke phase B.3 fail)
       3. duplicateRow() same pattern as addLine
     plus the x-init="initProductCell($el, line.key)" template binding
     remains as belt-and-suspenders. All paths are idempotent.

     Chunk c.3 will extend _mountRow to also wire the UoM cell. Chunk d
     will use the same helper to hydrate Edit.cshtml's N server-rendered
     rows on mount.

   Exposes:
     state:
       lines[]              — array of line rows
       selected (Set)       — bulk selection (chunk 4.5.4 will read it)
       openMenuKey          — which row's ⋯ menu is open (or null)
     actions:
       initGrid()           — wire SortableJS to the tbody + mount initial rows
       initProductCell(input, key)
                            — backward-compat wrapper for x-init template binding
       addLine()            — append empty row + mount + open Tom Select on it
       removeLine(key)      — remove + clean up Tom Select instance
       duplicateRow(key)    — in-place row copy + mount (no drawer; chunk 4.5.4 = drawer)
       moveTop(key)         — reorder helper
       moveBottom(key)      — reorder helper
       toggleMenu(key)      — open/close per-row ⋯ menu
       closeMenus()         — close all ⋯ menus (used on doc-click)
     computed (functions, called from view):
       totalQty()           — sum of qty * 1
       grandTotal()         — sum of qty * unitPrice
       allLinesComplete()   — true when every row has product+uom+qty>0
       linesDotClass()      — wmsfFormState dot for Section 02 (overrides default)

   opts:
     initialLines          — array of seed rows (server-rendered, may be empty)
     mode                  — 'create' | 'edit' (default 'create')
     onReorder             — (Array<{lineId, displayOrder}>) => void
                             Edit flow's eager-AJAX path (chunk d will pass it).
                             Create flow leaves it null — drag-drop is in-memory only.

   Row shape:
     {
       key:           string,   // stable identity for x-for keying + Tom Select map
       lineNumber:    int,      // legacy column (preserved per Option X)
       displayOrder:  int,      // gap-10 sequence (chunk 4.5.1)
       id:            string?,  // GUID for Edit-flow rows; null for unsaved Create rows
       productId:     string,
       productCode:   string,   // captured from Tom Select selection for re-render
       productName:   string,
       lotNumber:     string,
       expectedQuantity: number,
       uomId:         string,
       uomCode:       string,   // d.2.3.b — displayed on locked rows (readonly text)
       receivedQuantity: number,// d.2.3.b — drives isLocked flag
       isLocked:      boolean,  // d.2.3.b — true when receivedQuantity > 0
       unitPrice:     number?
     }

   d.2.3.b — grid-wide `readOnly` config flag retired (TD-026 closure).
   Lock state is per-row via `line.isLocked` (derived from receivedQuantity
   server-side in the Razor seed). The factory + Razor read line.isLocked
   directly; Sortable is always enabled (reorder of locked rows changes
   DisplayOrder only — no math impact).
   ================================================================== */

function poLineGrid(opts) {
    opts = opts || {};

    // Per-row Tom Select instances keyed by row.key — survives reorders
    // (Map keys are stable; Array indices are not).
    const tsInstances = new Map();
    let nextKey = 1;
    const newKey = () => 'k' + (nextKey++);

    // Seed lines: stamp each with a stable key + ensure displayOrder set
    const seed = (opts.initialLines || []).map((l, i) => Object.assign(
        { key: newKey() },
        l,
        { displayOrder: l.displayOrder || (i + 1) * 10 }
    ));

    return {
        // ------- state -------
        lines:        seed.length > 0 ? seed : [_blankRow(newKey, 1)],
        selected:     new Set(),
        openMenuKey:  null,
        mode:         opts.mode || 'create',
        sortable:     null,

        // ------- init -------
        initGrid() {
            this.$nextTick(() => {
                if (!this.$refs.linesTbody) return;

                // Walk initial rows + mount selects on each. Defensive
                // against x-init not firing reliably for initially-rendered
                // template instances (Alpine 3.x lifecycle quirks). Also
                // covers chunk d's Edit.cshtml hydration of N server-
                // rendered rows.
                this.$refs.linesTbody.querySelectorAll('[data-row-key]').forEach((rowEl) => {
                    const key = rowEl.dataset.rowKey;
                    this._mountRow(rowEl, key);
                });

                // d.2.3.b — Sortable always enabled. Locked rows can still
                // be reordered (DisplayOrder is presentation-only; LineNumber
                // + receipt math unaffected).
                // d.2.3.c.3 — reverted to d.2.3.c.1's index-splice path.
                // newKeys experiment in d.2.3.c.2 introduced a worse
                // failure mode (full reversal on fast drag); see TD-120.
                this.sortable = window.WmsGrid.setupSortable(this.$refs.linesTbody, {
                    handleSelector: '.wmsg-handle',
                    onEnd: (evt) => this._handleReorder(evt.oldIndex, evt.newIndex)
                });
            });

            // Close ⋯ menus when clicking outside any row's actions cell.
            // Listener attached at document level; cleaned up on x-destroy.
            this._docClickHandler = (e) => {
                if (!e.target.closest('.wmsg-actions')) this.closeMenus();
            };
            document.addEventListener('click', this._docClickHandler);
        },

        destroyGrid() {
            if (this._docClickHandler) {
                document.removeEventListener('click', this._docClickHandler);
            }
            if (this.sortable) this.sortable.destroy();
            tsInstances.forEach(ts => ts.destroy());
            tsInstances.clear();
        },

        // Backward-compat wrapper for the template's x-init binding.
        // x-init="initProductCell($el, line.key)" remains in the Razor
        // markup as belt-and-suspenders. If x-init fires first, this
        // mounts the row. If addLine/duplicateRow's explicit call fires
        // first, the tsInstances.has(key) guard inside _mountRow makes
        // this a no-op.
        initProductCell(input, key) {
            if (tsInstances.has(key)) return;
            const rowEl = input.closest('tr');
            if (rowEl) this._mountRow(rowEl, key);
        },

        // ------- row mount (canonical) -------
        // Calls WmsGrid.mountSelectsInRow with cell configs for every
        // combo in the row. Stashes resulting Tom Select instance in
        // tsInstances so destroyGrid + removeLine can clean up.
        // Seeds pre-selected option for Edit hydration paths.
        //
        // Chunk c.3 will add a 'uom' entry to the cells map.
        _mountRow(rowEl, key) {
            if (tsInstances.has(key)) return;
            const row = this.lines.find(l => l.key === key);
            if (!row) return;
            // d.2.3.b: per-row lock. Locked rows render readonly text
            // (Razor x-if branch), no <select> exists — mountSelectsInRow
            // would find nothing, but short-circuit here so we don't even
            // call it (cheaper + clearer intent).
            if (row.isLocked) return;

            const instances = window.WmsGrid.mountSelectsInRow(rowEl, {
                product: {
                    selector: '.wmsg-product-input',
                    config:   this._productCellConfig(key)
                }
                // c.3: uom: { selector: '.wmsg-uom-input', config: this._uomCellConfig(key) }
            });

            if (instances.product) {
                tsInstances.set(key, instances.product);
                // Seed Tom Select for pre-selected product (Edit hydration
                // path — server-rendered row already has productId).
                if (row.productId && row.productCode) {
                    instances.product.addOption({
                        id: row.productId,
                        code: row.productCode,
                        name: row.productName || '',
                        brand: ''
                    });
                    instances.product.setValue(row.productId, true /* silent */);
                }
            }
        },

        // Per-cell config builder for the Product Tom Select. Lives on
        // the factory because onChange needs to mutate this.lines[i] +
        // read the Tom Select instance from tsInstances. Captures the
        // row.key in closure so reorders don't break the lookup.
        _productCellConfig(rowKey) {
            const self = this;
            return {
                valueField:  'id',
                labelField:  'code',
                searchField: ['code', 'name', 'brand'],
                searchUrl:   '/Products/SearchAsync',
                placeholder: 'Search SKU…',
                extraParams: () => {
                    const wh = document.querySelector('[name=WarehouseId]');
                    return { warehouseId: wh ? wh.value : '' };
                },
                renderOption: (item, escape) => `
                    <div class="wmsg-option">
                        <div class="wmsg-option-left">
                            <div class="wmsg-option-code">${escape(item.code)}</div>
                            <div class="wmsg-option-name">${escape(item.name)}</div>
                            <div class="wmsg-option-brand">${escape(item.brand || '—')}</div>
                        </div>
                        <div class="wmsg-option-right">
                            <span class="wmsg-option-stock${(item.onHand > 0 ? '' : ' is-zero')}">
                                ${escape(String(item.onHand))} in stock
                            </span>
                        </div>
                    </div>
                `,
                renderItem: (item, escape) =>
                    `<span class="wmsg-item-code">${escape(item.code)}</span>`,
                onChange: (value) => {
                    const row = self.lines.find(l => l.key === rowKey);
                    if (!row) return;
                    row.productId = value;
                    const ts = tsInstances.get(rowKey);
                    const opt = ts && ts.options[value];
                    if (opt) {
                        row.productCode = opt.code;
                        row.productName = opt.name;
                    }
                }
            };
        },

        // ------- row mutations -------
        addLine() {
            const nextLineNumber = this.lines.length === 0
                ? 1
                : Math.max.apply(null, this.lines.map(l => l.lineNumber || 0)) + 1;
            const nextDisplayOrder = (this.lines.length + 1) * 10;
            const row = _blankRow(newKey, nextLineNumber);
            row.displayOrder = nextDisplayOrder;
            this.lines.push(row);
            // Belt-and-suspenders: explicitly mount the new row's selects.
            // Was missing in the original chunk c — caused row 2 dropdown
            // not rendering (smoke phase B.3 fail). x-init in template
            // remains as belt; this is the suspenders.
            this.$nextTick(() => {
                const rowEl = this.$root.querySelector(`[data-row-key="${row.key}"]`);
                if (rowEl) this._mountRow(rowEl, row.key);
                const ts = tsInstances.get(row.key);
                if (ts) ts.focus();
            });
        },

        removeLine(key) {
            if (this.lines.length <= 1) return; // always keep at least one
            const idx = this.lines.findIndex(l => l.key === key);
            if (idx === -1) return;
            // d.2.3.b — defence: UI disables the menu Delete on locked
            // rows, but a stray @click from elsewhere shouldn't bypass.
            if (this.lines[idx].isLocked) return;
            const ts = tsInstances.get(key);
            if (ts) {
                ts.destroy();
                tsInstances.delete(key);
            }
            this.lines.splice(idx, 1);
            this.selected.delete(key);
            this.closeMenus();
            this._renumberDisplayOrder();
        },

        duplicateRow(key) {
            const idx = this.lines.findIndex(l => l.key === key);
            if (idx === -1) return;
            const src = this.lines[idx];
            const nextLineNumber = Math.max.apply(null, this.lines.map(l => l.lineNumber || 0)) + 1;
            // d.2.3.b — duplicate of a locked row creates a NEW unlocked
            // line (new lines have no receipts by definition). This is
            // the documented workaround for "edit a locked line" — operator
            // clones, edits the clone, asks warehouse to receive against
            // the new line going forward.
            const dup = Object.assign({}, src, {
                key:              newKey(),
                lineNumber:       nextLineNumber,
                id:               null,             // unsaved copy
                displayOrder:     src.displayOrder, // recomputed by _renumberDisplayOrder
                receivedQuantity: 0,
                isLocked:         false
            });
            this.lines.splice(idx + 1, 0, dup);
            this.closeMenus();
            this._renumberDisplayOrder();
            // Same explicit-mount pattern as addLine.
            this.$nextTick(() => {
                const rowEl = this.$root.querySelector(`[data-row-key="${dup.key}"]`);
                if (rowEl) this._mountRow(rowEl, dup.key);
            });
        },

        moveTop(key) {
            const idx = this.lines.findIndex(l => l.key === key);
            if (idx <= 0) return;
            const [moved] = this.lines.splice(idx, 1);
            this.lines.unshift(moved);
            this.closeMenus();
            this._renumberDisplayOrder();
            this._notifyReorder();
        },

        moveBottom(key) {
            const idx = this.lines.findIndex(l => l.key === key);
            if (idx === -1 || idx === this.lines.length - 1) return;
            const [moved] = this.lines.splice(idx, 1);
            this.lines.push(moved);
            this.closeMenus();
            this._renumberDisplayOrder();
            this._notifyReorder();
        },

        // d.2.3.c.1 — Move Up / Move Down (single-step neighbor swap).
        // Mirror of moveTop/moveBottom; useful in lists with 5-10 lines
        // where Top/Bottom is too coarse. Same DisplayOrder semantics
        // (allowed on locked rows — presentation-only).
        moveUp(key) {
            const idx = this.lines.findIndex(l => l.key === key);
            if (idx <= 0) return;
            // Swap with the row above (splice out + splice in at idx-1).
            const [moved] = this.lines.splice(idx, 1);
            this.lines.splice(idx - 1, 0, moved);
            this.closeMenus();
            this._renumberDisplayOrder();
            this._notifyReorder();
        },

        moveDown(key) {
            const idx = this.lines.findIndex(l => l.key === key);
            if (idx === -1 || idx === this.lines.length - 1) return;
            const [moved] = this.lines.splice(idx, 1);
            this.lines.splice(idx + 1, 0, moved);
            this.closeMenus();
            this._renumberDisplayOrder();
            this._notifyReorder();
        },

        // ------- ⋯ menu -------
        toggleMenu(key) {
            this.openMenuKey = (this.openMenuKey === key) ? null : key;
        },

        closeMenus() {
            this.openMenuKey = null;
        },

        // ------- selection (chunk 4.5.4 will use) -------
        toggleSelect(key) {
            if (this.selected.has(key)) this.selected.delete(key);
            else this.selected.add(key);
        },

        isSelected(key) {
            return this.selected.has(key);
        },

        // ------- computed (called from view) -------
        totalQty() {
            return this.lines.reduce((s, l) => s + (Number(l.expectedQuantity) || 0), 0);
        },

        rowTotal(line) {
            const qty   = Number(line.expectedQuantity) || 0;
            const price = Number(line.unitPrice) || 0;
            return qty * price;
        },

        grandTotal() {
            return this.lines.reduce((s, l) => s + this.rowTotal(l), 0);
        },

        allLinesComplete() {
            return this.lines.every(l =>
                l.productId && l.uomId && Number(l.expectedQuantity) > 0
            );
        },

        // wmsfFormState dot override for Section 02 (Lines).
        // Read by the view's <span class="wmsf-dot" :class="linesDotClass()">.
        linesDotClass() {
            if (this.serverErrors && this.serverErrors[2]) return 'is-error';
            if (this.lines.length > 0 && this.allLinesComplete()) return 'is-complete';
            if (this.lines.some(l => l.productId)) return 'is-progress';
            return '';
        },

        // ------- internals -------
        _handleReorder(oldIdx, newIdx) {
            // d.2.3.c.3 — reverted to index splice. d.2.3.c.2's newKeys
            // experiment shipped a worse failure mode for fast drag
            // (full reversal); reverted in favor of partial fix.
            // Fast-drag race tracked as TD-120.
            const [moved] = this.lines.splice(oldIdx, 1);
            this.lines.splice(newIdx, 0, moved);
            this._renumberDisplayOrder();
            this._notifyReorder();
        },

        _renumberDisplayOrder() {
            this.lines.forEach((l, i) => {
                l.displayOrder = (i + 1) * 10;
                // Create flow: keep lineNumber in sync with array
                // position so submission order = LineNumber sequence.
                // Edit flow: lineNumber is server-assigned + immutable.
                if (this.mode === 'create') l.lineNumber = i + 1;
            });
        },

        _notifyReorder() {
            if (typeof opts.onReorder !== 'function') return;
            // Edit flow only — Create flow leaves onReorder null.
            opts.onReorder(this.lines
                .filter(l => l.id) // saved rows only
                .map(l => ({ lineId: l.id, displayOrder: l.displayOrder })));
        }
    };
}

// Internal: a fresh blank row. d.2.3.b — new rows have no receipts by
// definition (isLocked=false, receivedQuantity=0).
function _blankRow(newKeyFn, lineNumber) {
    return {
        key:              newKeyFn(),
        lineNumber:       lineNumber,
        displayOrder:     lineNumber * 10,
        id:               null,
        productId:        '',
        productCode:      '',
        productName:      '',
        lotNumber:        '',
        expectedQuantity: 1,
        uomId:            '',
        uomCode:          '',
        receivedQuantity: 0,
        isLocked:         false,
        unitPrice:        null
    };
}

window.poLineGrid = poLineGrid;
