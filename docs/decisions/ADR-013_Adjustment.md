# ADR-013: General Stock Adjustment

**Status**: Accepted
**Date**: 2026-05-04
**Phase**: 1 (Required for B2B launch)

---

## Context

ระบบ WMS ต้อง handle stock discrepancies ที่ไม่ใช่จาก Cycle Count
- Damage in warehouse (handling, fall, water, etc.)
- Loss during operations (pick errors, theft)
- Found stock (wrong location, missing item appears)
- QC dispositions (accept/quarantine/scrap/RTV)
- Reclassification (Owner change, lot change)
- Manual corrections (data entry errors, system bugs)

แบบเดิมมี:
- counts.CountAdjustments (cycle count specific)
- StockMovements.MovementType = 'Adjust' (just a type)

ที่ขาด:
- General workflow for daily adjustments
- Reason codes master
- Approval workflow per reason
- Photo evidence
- Billing integration (3PL chargeable adjustments)

## Decision

### Schema (2 tables)

1. **master.AdjustmentReasons** (configurable reason codes)
   - Code: 'DAMAGE-WAREHOUSE', 'LOSS-PICK', 'FOUND-LOC', etc.
   - Category: Damage/Loss/Found/QC/Manual/Reclassify
   - Direction: Decrease/Increase/Both
   - RequireApproval (per reason)
   - RequirePhoto (per reason)
   - AuthorityLevel (Supervisor/Manager/GM)
   - AuthorityValueLimit (threshold)
   - IsChargeable (3PL: charge customer?)
   - ChargeAccount (billing)

2. **inventory.StockAdjustments** (the actual adjustment)
   - AdjustmentNumber: ADJ-YYYYMMDD-NNNN
   - Stock context (Stock/Product/Location/Lot/Pallet/Owner)
   - Quantities: Before / After / Delta (computed)
   - ReasonId FK
   - Notes + PhotoUrls (JSON)
   - SourceType + SourceReferenceId (where it came from)
   - 4-state workflow
   - Submission/Approval/Application timestamps
   - Billing integration (BillableActivityId)

### Distinct from CountAdjustments

| Aspect | CountAdjustments | StockAdjustments |
|--------|------------------|------------------|
| Source | Cycle count | Daily operations |
| Trigger | Count vs system | Operator observation |
| Reason | Always "Variance" | Multiple categories |
| Workflow | Recount-first | Direct adjustment |
| Photo | Optional | Often required |
| Billing | Usually not | Can be chargeable |

**Decision**: Keep separate. Different workflows, different audit needs.

### Workflow (4 states)

```
Pending → Approved → Applied
        ↓
        Rejected (terminal)
```

| From | To | Trigger | Authority |
|------|------|---------|-----------|
| (new) | Pending | User submits | Operator+ |
| Pending | Approved | Approver approves | Per reason |
| Pending | Rejected | Approver rejects | Per reason |
| Approved | Applied | System applies | Auto (immediate) |

### Authority Routing (similar to Cycle Count)

```
Per AdjustmentReasons.AuthorityLevel + AuthorityValueLimit:

Default routing by adjustment value:
  < ฿1,000:    Supervisor approval
  < ฿10,000:   Manager approval
  < ฿100,000:  GM approval
  ≥ ฿100,000:  GM + Audit log + email notification

Per-reason override:
  Reason "MANUAL-FIX": always GM (any value)
  Reason "FOUND-LOC": Supervisor (any value, no risk)
  Reason "DAMAGE-WAREHOUSE": Manager (insurance/billing)
```

### Use Cases

#### UC1: Damage in Warehouse
```
Operator finds 5 damaged units of SKU-001
1. Submit StockAdjustment:
   - Reason: DAMAGE-WAREHOUSE (chargeable, photo required)
   - QtyBefore: 100
   - QtyAfter: 95
   - Photo: damage_001.jpg, damage_002.jpg
2. Manager reviews + approves
3. System creates StockMovement (-5)
4. If 3PL customer: BillableActivity created
```

#### UC2: Loss During Pick
```
Picker can't find expected qty during picking
1. Picker triggers Adjustment from mobile
2. Reason: LOSS-PICK (require investigation)
3. Submit with notes
4. Supervisor investigates + approves/rejects
5. If approved: stock decreased + investigation flag
```

#### UC3: Found Stock
```
Putaway operator finds stock not in system
1. Submit StockAdjustment:
   - Reason: FOUND-LOC (low risk)
   - QtyBefore: 0 (or current)
   - QtyAfter: 5
2. Auto-approved (low risk reason)
3. Stock increased
```

#### UC4: QC Quarantine
```
Receiving inspector rejects item
1. Submit StockAdjustment:
   - Reason: QC-QUARANTINE (reclassify)
   - From location: Receiving area
   - To location: Quarantine zone
2. Auto-approved (just movement)
3. Stock physically moved
```

#### UC5: Owner Reclassification
```
VMI item converted to Self-owned (purchase)
1. Submit StockAdjustment:
   - Reason: RECLASSIFY-OWNER (manual, GM approval)
   - QtyBefore: 100 (Owner=VMI)
   - QtyAfter: 100 (Owner=Self)
2. GM approves
3. Stock OwnerId changed (audit logged)
```

## Consequences

### Positive

✅ Comprehensive adjustment support
✅ Real-world flexibility
✅ Reason codes drive workflow (configurable)
✅ Photo evidence for damage claims
✅ Billing hooks for 3PL revenue
✅ Audit trail complete
✅ Mobile + desktop submission

### Negative

⚠️ Workflow complexity per reason
   → Mitigation: Reason master is admin-configurable

⚠️ Photo storage cost
   → Mitigation: Required only for specific reasons; cleanup policy

⚠️ Approval bottleneck risk
   → Mitigation: Auto-approve thresholds; multi-approver fallback

### Neutral

- Functions added: ~18
- Build time: 1 week (Week 6)
- Storage: photos in cloud (S3/Azure Blob)
- New permissions: Adjustment.Create, .Approve, .Reject, .ManageReasons

## Default Reason Codes (Seed Data)

| Code | Category | Direction | RequireApproval | RequirePhoto | Chargeable |
|------|----------|-----------|-----------------|--------------|------------|
| DAMAGE-WAREHOUSE | Damage | Decrease | Yes | Yes | Yes |
| DAMAGE-RECEIVING | Damage | Decrease | Yes | Yes | No (vendor) |
| LOSS-PICK | Loss | Decrease | Yes | No | No |
| LOSS-UNKNOWN | Loss | Decrease | Yes | No | No |
| LOSS-THEFT | Loss | Decrease | Yes (GM) | Yes | No |
| FOUND-LOC | Found | Increase | No (auto) | No | No |
| FOUND-RETURN | Found | Increase | No (auto) | No | No |
| QC-REJECT | QC | Both (move) | Yes | Optional | No |
| QC-QUARANTINE | QC | Both (move) | No (auto) | No | No |
| QC-SCRAP | QC | Decrease | Yes | Yes | No |
| MANUAL-CORRECTION | Manual | Both | Yes (GM) | Yes | No |
| RECLASSIFY-OWNER | Reclassify | Both | Yes (GM) | No | No |
| RECLASSIFY-LOT | Reclassify | Both | Yes | No | No |
| TRANSFER-LOSS | Loss | Decrease | Yes | Optional | No |

## Alternatives Considered

### Alternative 1: Extend CountAdjustments
**Rejected**: Different workflows. Confusing semantics.

### Alternative 2: No formal adjustment, just StockMovements
**Rejected**: No approval. No audit. No billing. No reason tracking.

### Alternative 3: External adjustment system
**Rejected**: Over-engineering. Tight integration needed with stock + billing.

## Related ADRs

- ADR-006: Activity-based billing (chargeable adjustments hook here)
- ADR-007: Owner concept (RECLASSIFY-OWNER use case)
- ADR-010: Function-CRUD permission matrix (approval permissions)
- ADR-012: Inter-warehouse Transfer (loss in transit creates adjustment)

## Implementation Notes

### Phase 1 Build (Week 6)
- Day 1: Schema + migrations
- Day 2: AdjustmentReasons CRUD + seed data
- Day 3: StockAdjustments BLL + workflow
- Day 4: UI (submit, list, approve)
- Day 5: Mobile submission + photo upload + integration tests

### Future Enhancements
- Bulk adjustments (multiple stocks at once)
- Adjustment templates (common patterns)
- Insurance integration (damage claims)
- Vendor chargeback integration
- AI/ML for anomaly detection

---

**Last updated**: 2026-05-04
