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

   Exposes:
     state:
       lines[]              — array of line rows
       selected (Set)       — bulk selection (chunk 4.5.4 will read it)
       openMenuKey          — which row's ⋯ menu is open (or null)
     actions:
       initGrid()           — wire SortableJS to the tbody (call from x-init)
       initProductCell(input, key)
                            — wire Tom Select to a Product cell
       addLine()            — append empty row + open Tom Select on it
       removeLine(key)      — remove + clean up Tom Select instance
       duplicateRow(key)    — in-place row copy (no drawer; chunk 4.5.4 = drawer)
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
     readOnly              — boolean, default false (Edit-locked when receipts exist)
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
       unitPrice:     number?
     }
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
        readOnly:     !!opts.readOnly,
        mode:         opts.mode || 'create',
        sortable:     null,

        // ------- init -------
        initGrid() {
            this.$nextTick(() => {
                if (!this.$refs.linesTbody) return;
                this.sortable = window.WmsGrid.setupSortable(this.$refs.linesTbody, {
                    handleSelector: '.wmsg-handle',
                    disabled: this.readOnly,
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

        initProductCell(input, key) {
            // Skip if already wired (Alpine may re-run x-init on certain
            // re-renders; the WmsGrid.setupTomSelect call would double-init).
            if (tsInstances.has(key)) return;

            const row = this.lines.find(l => l.key === key);
            const ts = window.WmsGrid.setupTomSelect(input, {
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
                    if (!row) return;
                    row.productId = value;
                    // Stash code/name from the selected item so we can
                    // re-render the cell after a reorder without a
                    // round-trip back to the server.
                    const opt = ts.options[value];
                    if (opt) {
                        row.productCode = opt.code;
                        row.productName = opt.name;
                    }
                }
            });

            // If row has a pre-selected product (Edit flow), seed Tom Select
            if (row && row.productId && row.productCode) {
                ts.addOption({
                    id: row.productId,
                    code: row.productCode,
                    name: row.productName || '',
                    brand: ''
                });
                ts.setValue(row.productId, true /* silent */);
            }

            tsInstances.set(key, ts);
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
            // Focus the new row's Product Tom Select on next tick.
            this.$nextTick(() => {
                const newInput = this.$root.querySelector(
                    `[data-row-key="${row.key}"] .wmsg-product-input`
                );
                const ts = tsInstances.get(row.key);
                if (ts) ts.focus();
                else if (newInput) newInput.focus();
            });
        },

        removeLine(key) {
            if (this.lines.length <= 1) return; // always keep at least one
            const idx = this.lines.findIndex(l => l.key === key);
            if (idx === -1) return;
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
            const dup = Object.assign({}, src, {
                key:          newKey(),
                lineNumber:   nextLineNumber,
                id:           null,             // unsaved copy
                displayOrder: src.displayOrder  // recomputed by _renumberDisplayOrder
            });
            this.lines.splice(idx + 1, 0, dup);
            this.closeMenus();
            this._renumberDisplayOrder();
            // Seed Tom Select for the duplicated row on next tick.
            this.$nextTick(() => {
                const newInput = this.$root.querySelector(
                    `[data-row-key="${dup.key}"] .wmsg-product-input`
                );
                if (newInput) this.initProductCell(newInput, dup.key);
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
            // SortableJS already mutated the DOM. Sync our model so
            // Alpine's next x-for re-render lands in the same order.
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

// Internal: a fresh blank row.
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
        unitPrice:        null
    };
}

window.poLineGrid = poLineGrid;
