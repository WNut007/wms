# ADR-011: 3D Warehouse Monitor — Schema Now, Implementation in Phase 4

**Status**: Accepted
**Date**: 2026-05-04
**Decision makers**: Project owner

---

## Context

ระบบ WMS ต้องการ visual representation ของ warehouse layout
- Manager ต้องการเห็น stock distribution เชิงพื้นที่
- Customer demos (3PL pitching) ต้องการ "wow factor"
- Slotting optimization ต้องการเห็น activity heatmap

Reference: SAP EWM 3D Warehouse Monitor (Eric Schulz, SAP Tokyo, 2023)
- Web-based 3D visualization using Three.js
- Real-time bin contents visualization
- Multiple view modes (occupancy, velocity, heat)

## Decision

### What we WILL do (Phase 1)

1. **Add coordinate fields to Locations table**:
   - `PositionX, PositionY, PositionZ` (DECIMAL(10,3) in meters)
   - `Rotation` (DECIMAL(5,2) in degrees)
   - `Aisle, Bay, Level` for grouping

2. **Add visualization metadata to Locations**:
   - `Show3D` (BIT, default 1)
   - `DisplayColor` (VARCHAR for color override)
   - `IsPickface` (BIT for visual marker)

3. **Add WarehouseLayouts master table** (empty until Phase 4)
   - Overall warehouse dimensions
   - Static elements (walls, columns) as JSON
   - Default view mode

4. **Add Pattern-based generator support**:
   - When generating bins like "A-{rack}-{level}-{bin}"
   - Auto-fill X/Y/Z coordinates based on pattern math
   - Saves manual data entry later

### What we will NOT do (until Phase 4)

❌ Install Three.js or any 3D library
❌ Build 3D viewer pages
❌ Build SVG floor plan visualization
❌ Implement picker position tracking
❌ Implement activity heatmap aggregation

## Rationale

### Why add schema now?

**Cost of adding later is high**:
- Backfilling X/Y/Z for 5,000+ existing bins = manual work
- Pattern-based generator should fill these from day 1
- Schema migration on production data = risky
- Effort to add now: ~5 minutes (just add columns)

**Cost of adding now is low**:
- Just nullable columns
- No impact on Phase 1-3 functionality
- Pattern generator already iterates through positions
- Easy to fill during location creation

### Why defer implementation to Phase 4?

**Not critical for launch**:
- B2B works without 3D
- B2C works without 3D
- 3PL works without 3D
- Reports + dashboards already provide insights

**Implementation is significant**:
- Three.js learning curve
- Performance tuning (5,000+ bins at 60fps)
- Real-time SignalR coordination
- Coordinate system setup workflow
- Mobile fallback considerations
- Total: 5-6 weeks dedicated work

**Better as Phase 2 (post-launch)**:
- Use real customer feedback to design
- Differentiator for sales pitches
- Adds visible value during 3PL expansion
- Doesn't compete with critical operational features

## Consequences

### Positive

✅ Phase 1-3 timeline unaffected (4-5 months)
✅ Schema ready when Phase 4 begins (no migration needed)
✅ Pattern-based location generator includes X/Y/Z from day 1
✅ Customer demos can show "3D coming soon" with confidence
✅ Activity logging from Phase 1 = data ready for heatmap when built

### Negative

⚠️ X/Y/Z fields visible in admin UI (might confuse users)
   → Mitigation: Hide in basic view, show in "advanced" mode

⚠️ Bulk import templates need to handle empty coordinates gracefully
   → Mitigation: NULL allowed, fill in via pattern generator if missing

⚠️ Risk of feature creep: someone might "just add 2D map"
   → Mitigation: This ADR explicitly forbids it
   → Mitigation: CLAUDE.md "Things NOT to Do" lists it

### Neutral

- Schema slightly larger (3 DECIMAL columns × 5,000 bins = ~120KB)
- Documentation must explain feature is deferred
- Phase 4 planning needs dedicated time

## Implementation Notes

### Phase 1 Tasks (already in roadmap)

- [x] Schema: Add X/Y/Z to master.Locations
- [x] Schema: Add WarehouseLayouts table
- [x] Schema: Add LocationActivity table (for future heatmap data)
- [x] Schema: Add PickerPositions table (for future live view)
- [ ] Pattern generator: auto-fill X/Y/Z from pattern math
- [ ] Bulk import: accept X/Y/Z columns (optional)
- [ ] Admin UI: show coordinates in "advanced" mode
- [ ] Documentation: note coordinates are for Phase 4

### Phase 4 Tasks (when activated)

#### 4b.1: 2D SVG Floor Plan (5 days)
- Read coordinates → render SVG rects
- Color by mode (5 modes)
- Click handlers
- SignalR live updates

#### 4b.2: Three.js 3D Renderer (2 weeks)
- Install Three.js + OrbitControls
- Build scene from coordinates
- InstancedMesh optimization
- View mode toggles

#### 4b.3: Live Operations Twin (3+ weeks)
- Picker position tracking (mobile updates)
- Pick path animation
- Activity heatmap aggregation jobs
- Time-lapse replay

## Alternatives Considered

### Alternative 1: Build 2D map in Phase 1

**Rejected because**:
- Adds 5 days to already tight Phase 1 timeline
- Pattern generator + coordinates = same value as visualization itself
- Better as polish feature in Phase 4

### Alternative 2: Skip 3D entirely

**Rejected because**:
- Strong differentiator for 3PL sales
- SAP/Symphony/Swisslog all have 3D — table stakes for enterprise
- Schema fields cost almost nothing to add now

### Alternative 3: Use external 3D tool (e.g., Magellan SAIL)

**Rejected because**:
- Adds dependency on 3rd party
- Licensing costs
- Less integration flexibility
- We have full control of our data, can build native

## Related ADRs

- ADR-004: Hybrid putaway (uses bin coordinates)
- ADR-005: Strategy pattern (slotting strategies will inform 3D)

---

**Last updated**: 2026-05-04
