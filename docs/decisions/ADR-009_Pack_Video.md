# ADR-009: Pack Video

**Status**: Accepted
**Date**: 2026-05-10
**Phase**: 17 (Pack Video MVP)

---

## Context

The Pack workflow (Phase 14D) records *what* went into a carton —
PackTaskLines.PackedQuantity, BoxTypeId, WeightKg, ShortPackReason.
What it does NOT record is *visual evidence* of what was actually
sealed in the box.

Visual evidence is operationally valuable for three things:

1. **Dispute resolution** — customer claims "you sent me 4 items, I
   only got 3" → operator pulls the pack video and can see the
   carton's contents at the moment of sealing
2. **Picker/packer accountability** — chronic discrepancies on a
   specific operator's tasks can be reviewed
3. **Process auditing** — pack supervisor spot-checks N% of tasks
   for procedure compliance

The original design doc (`docs/01_WMS_Master_Design.md` §7) sketched
this as: "Pack video: 2-level control (per-station + per-channel),
10-day retention, MediaRecorder-based, no native app". This ADR turns
that sketch into a buildable MVP.

Privacy is a real concern. Even without microphone capture, a video
recorded at a pack station can incidentally capture operator faces,
hands, badges, customer order details visible on screen, etc.
Thailand's PDPA + most retention frameworks require:
- Documented retention policy (we ship 10-day default)
- Access audit log (deferred — TD)
- Purpose limitation (we use it for dispute/audit only)
- Data minimisation (we don't capture audio in MVP)

## Decision

### MVP scope (this ADR — Phase 17)

**Recording**:
- Trigger: operator clicks "Record video" toggle on the Pack Detail
  submit form. NOT auto-start (no surprise recording).
- Format: WebM (VP9 codec), MediaRecorder API browser-native.
- Microphone: muted by default. Captures video stream only. Reduces
  bandwidth + sidesteps audio-recording privacy questions.
- Soft cap: 60 seconds. Operator UI shows a warning banner past 60s;
  no hard server-side cap on duration (just on file size, see below).
- Browser support: Chromium-family (Chrome / Edge / Brave). Safari
  users see a "video not supported in this browser" notice — full
  Safari support requires server-side transcoding (TD).

**Upload**:
- Separate POST endpoint (`POST /PackTasks/UploadVideo/{id}`),
  not coupled to the Submit POST. Reasons:
  - Submit's TX should stay small + fast (no blob upload inside
    the pack-task TransactionScope)
  - Video upload failure shouldn't block the pack from being
    submitted (the carton is already sealed regardless)
  - Operator can submit first, then upload after — natural UX
- Multipart/form-data; the file part is the WebM blob.

**Storage**:
- Reuse the existing `IDocumentStorageService` (Phase 5
  `LocalFileStorageService`). Same `documents.Files` table, same
  on-disk layout (`{root}/{tenantId}/{entityType}/{entityId}/...`).
- `entityType="PackTask"`, `category="PackVideo"`. The category
  field is what distinguishes a pack video from a pack-related
  document (e.g. operator notes PDF) in the same row family.
- File size cap raised from 25 MB → 50 MB to fit a 60-second 720p
  WebM (~30 MB typical). `.webm` + `.mp4` added to the extension
  allowlist.
- Per-pack-task metadata: a parallel `outbound.PackVideos` row
  keyed by `PackTaskId` + `DocumentFileId` FK back to the storage
  table. Carries pack-specific fields (`DurationSec`,
  `RecordedAt`, `RecordedBy`) the generic documents.Files schema
  doesn't capture.

**Playback**:
- Terminal Pack Detail (Packed status) page surfaces a "Watch video"
  button when a video exists.
- HTML5 `<video>` element streams from
  `GET /PackTasks/Video/{id}` (currently full-file response;
  range-request streaming is a TD).

**Retention**:
- **10-day automatic cleanup via Hangfire job** (Phase 17 T1
  infrastructure). Daily run at 03:00 UTC.
- Job logic: select `PackVideos` with `RecordedAt < NOW - 10 days`,
  delete the underlying `documents.Files` row + its on-disk bytes
  via `IDocumentStorageService.DeleteAsync`, then delete the
  `PackVideos` row. Idempotent — re-running on the same day is a
  no-op.
- `RetentionDays` configurable via `appsettings.json` (default 10).
- Per-tenant retention override deferred (TD — needs a tenant-level
  setting table).

**Permissions**:
- `OUTBOUND.ORDERS` covers Upload + Get + Delete for MVP. Same
  permission gate as the rest of the pack workflow. Tightening to a
  separate `OUTBOUND.VIDEO` perm with read-vs-write split is a TD
  alongside the access log.

### Multi-step state diagram

```
Pack task submitted → operator clicks "Record"
                      → MediaRecorder starts (camera + screen blob)
                      → operator clicks "Stop" (or 60s soft warning)
                      → blob → POST /PackTasks/UploadVideo/{id}
                      → IDocumentStorageService writes file to disk
                      → outbound.PackVideos row inserted
                      → UI shows "Video uploaded"

Daily 03:00 UTC      → Hangfire fires PackVideoRetentionCleanupJob
                      → finds rows with RecordedAt < NOW - RetentionDays
                      → DELETE storage bytes + PackVideos row
```

## Alternatives considered

### Coupling video upload to Submit POST

Reject. Two concerns:
1. The Submit TransactionScope wraps multiple repo writes (line
   updates + carton create + task flip + SO flip). Adding a multi-MB
   blob upload inside the TX would extend TX duration into the
   seconds and risk MSDTC promotion timeouts.
2. If the upload fails, the operator's options are bad: either roll
   back the submit (carton is already sealed; can't un-pack) or
   commit and lose the video. Decoupling lets the submit succeed and
   the video upload retry independently.

### Continuous recording (start at first carton entry, stop at submit)

Reject for MVP. Continuous recording captures a lot of dead time
(operator scratching head, walking to scale, etc.) and inflates
storage costs significantly. On-demand recording is the smaller
default; operators who want continuous can hit Record then Stop on
their own cadence.

### Server-side transcoding to MP4

Reject for MVP. MP4 transcoding requires ffmpeg in the deployment +
a queue + per-file CPU cost. Browser-native WebM works in 80%+ of
operator browsers (Chromium-family is dominant in warehouse TPCs).
Safari users get a "not supported" notice + can't record. Transcoding
is a Phase 17B+ TD when Safari coverage matters.

### Audio capture

Reject. Audio recording is a much higher-friction privacy decision
(operator conversations, ambient phone calls, customer voice if
intercom). Visual evidence answers the dispute use case without it.

### Per-station / per-channel recording policy

Reject for MVP scope but **schema-ready**:
- `master.PackStations.VideoEnabled` exists as a column from Phase
  pre-MVP. MVP ignores it (always-recordable when the operator
  clicks). Future TD: respect the flag + add admin UI to toggle.
- Per-channel policy (B2C requires, B2B opt-in) needs a SO-channel
  link first. TD until SO-channel lands.

## Tech-Debt items spawned

| TD | Description |
|---|---|
| TD-039 | Pack video — Safari support via server-side transcoding |
| TD-039 | Pack video — PDPA access audit log (`documents.VideoAccessLog`) |
| TD-039 | Pack video — per-station policy (honor `PackStations.VideoEnabled`) |
| TD-039 | Pack video — per-channel policy (B2C requires) |
| TD-039 | Pack video — per-tenant retention override |
| TD-039 | Pack video — admin role check on /hangfire dashboard |
| TD-039 | Pack video — range-request streaming for `GET /PackTasks/Video/{id}` |
| TD-039 | Pack video — thumbnail extraction (preview frame on the carton tile) |
| TD-039 | Pack video — continuous recording mode |
| TD-039 | Pack video — mobile pack PWA with video |
| TD-039 | Pack video — finer perm split (`OUTBOUND.VIDEO` read/write) |

## Rollout

1. Hangfire infrastructure (Phase 17 T1) — done.
2. Schema (Phase 17 T2) — `outbound.PackVideos` table + storage
   options widening.
3. Service + controller endpoints (T3-T4).
4. Pack Detail UI integration (T5).
5. Retention job (T6).
6. Tests + tag (T7).

## References

- `docs/01_WMS_Master_Design.md` §7 (original sketch)
- `docs/03_WMS_Implementation_Roadmap.md` Week 9 (planned scope)
- ADR-014 (storage / immutability pattern reused for the
  documents.Files row)
- Phase 14D commit `dfd560b` (Pack SubmitAsync TX shape — why we
  decouple)
