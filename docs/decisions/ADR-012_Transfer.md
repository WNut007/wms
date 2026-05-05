# ADR-012: Inter-warehouse Transfer

**Status**: Accepted
**Date**: 2026-05-04
**Phase**: 1 (Required for B2B launch)

---

## Context

ระบบ WMS ใหม่ต้อง support multi-warehouse setup
- B2B ปัจจุบัน: บางลูกค้าใหญ่มีหลาย locations
- 3PL อนาคต: lookup โดน multiple warehouses บ่อย
- ATP design มี multi-location หรือ network inventory
- Receiving → Putaway → Pick → Pack workflow ต้องไหลข้าม warehouse ได้

ที่มีในแบบเดิม:
- inventory.TransferOrders (header เท่านั้น)
- ขาด: Lines, Status History, Workflow detail, In-transit handling

## Decision

### Schema (3 tables)

1. **inventory.TransferOrders** (header)
   - TransferNumber unique
   - From + To warehouse
   - 9-state Status field
   - Timestamps for each milestone
   - People (requested/approved/dispatched/received by)
   - Logistics (carrier, tracking number, transit days)

2. **inventory.TransferOrderLines** (item details)
   - TransferId FK
   - Product, Owner, Lot (preserved)
   - From location (specific or any)
   - To location (specific or putaway)
   - Quantities: Requested / Dispatched / Received
   - Computed: QtyLossInTransit
   - Per-line status
   - Linked PickTask + Adjustment (loss)

3. **inventory.TransferStatusHistory** (audit)
   - From → To status
   - Reason
   - Performer + timestamp

### Workflow (9 states + 2 side paths)

```
Main Flow:
Draft → Submitted → Approved → Picking → 
Dispatched → InTransit → Receiving → Received → Closed

Side Paths:
- Cancelled: any state before InTransit
- Lost: InTransit but never Received (timeout or admin)
```

### State Machine Details

| From State | To State | Trigger | Permission |
|-----------|----------|---------|------------|
| Draft | Submitted | User submits | Manager+ |
| Submitted | Approved | Approver approves | Approver role |
| Submitted | Cancelled | User cancels | Submitter |
| Approved | Picking | Pick task created | System auto |
| Picking | Dispatched | All lines picked | Picker completes |
| Dispatched | InTransit | Driver pickup | Dispatch role |
| InTransit | Receiving | Arrived at To wh | Receiver scans |
| Receiving | Received | All lines received | Receiver completes |
| Received | Closed | Final reconciliation | System auto |
| InTransit | Lost | Timeout or admin | Admin only |

### Key Behaviors

**Owner Preservation**:
- TransferOrderLines.OwnerId required
- Stock at destination = same OwnerId
- VMI/3PL stock cannot lose owner identity

**Lot Preservation**:
- TransferOrderLines.LotId optional but preserved if specified
- Source lot → destination lot (same Id)
- No commingling unless explicitly allowed

**Loss in Transit Handling**:
- If QtyDispatched > QtyReceived
- Auto-create StockAdjustment with reason "TRANSFER-LOSS"
- Link via TransferOrderLines.AdjustmentId
- Investigate reason (damage during transport, theft, etc.)

**In-transit Stock Representation**:
- Option A: Pseudo-location "IN-TRANSIT" per warehouse
- Option B: Track via TransferOrderLines (no stock record)
- **Decision**: Option B (cleaner, no extra location complexity)
- Stock decreased at From wh on Dispatch
- Stock increased at To wh on Receive

**Pick Task Generation**:
- On Approved → Picking transition
- Create PickTasks for all lines
- Use normal picker workflow (4-tier scan)
- Linked back via TransferOrderLines.PickTaskId

**Receiving Workflow**:
- On arrival, use receiver mobile workflow
- Scan transfer number (replaces ASN)
- Verify QtyDispatched
- Discrepancy → mark line "Discrepancy"
- Auto Adjustment for shortfall

## Consequences

### Positive

✅ Real multi-warehouse support from day 1
✅ Audit trail full (header + lines + status history)
✅ Owner integrity preserved
✅ Loss tracking automatic
✅ Reuses existing pick + receiving workflows
✅ B2B can ship between warehouses

### Negative

⚠️ Schema complexity (3 new tables)
   → Mitigation: Standard pattern, documented

⚠️ Workflow learning curve (9 states)
   → Mitigation: UI shows current state + next valid actions

⚠️ Manual approval bottleneck
   → Mitigation: Auto-approve for low-value (configurable threshold)

### Neutral

- Functions added: ~15
- Build time: 1 week (Week 6)
- Permissions added: Transfer.Create, .Approve, .Dispatch, .Receive

## Alternatives Considered

### Alternative 1: Use existing Order Management
**Rejected**: Orders are customer-facing. Internal transfers different domain.

### Alternative 2: Direct stock move (no document)
**Rejected**: No audit trail. No approval. No logistics tracking.

### Alternative 3: External logistics module
**Rejected**: Over-engineering. Internal transfers don't need full TMS.

## Related ADRs

- ADR-007: Owner concept (Transfer must preserve owner)
- ADR-013: Stock Adjustment (loss in transit creates adjustment)

## Implementation Notes

### Phase 1 Build (Week 6)
- Day 1: Schema + migrations
- Day 2: BLL services
- Day 3: Controller + UI (header)
- Day 4: Lines + workflow
- Day 5: Mobile receiving + integration tests

### Future Enhancements (Post-Phase 1)
- Multi-stop transfers (warehouse A → B → C)
- Transfer templates (recurring rebalancing)
- Auto-trigger on low stock threshold
- Carrier API integration for tracking
- BOL (Bill of Lading) document generation

---

**Last updated**: 2026-05-04
