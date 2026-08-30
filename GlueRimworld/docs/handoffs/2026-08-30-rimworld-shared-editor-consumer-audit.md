# GlueRimworld shared Glue Editor consumer audit

Date: 2026-08-30  
Scope: `rimworld/GlueRimworld`

## Current posture

GlueRimworld is a genuine, deliberately thin host adapter: RimWorld's
netstandard2.1/Unity process calls the shared `glue-runtime-host` over the
sanctioned loopback HTTP `/api/execute` boundary. It does not embed a second
GLUE engine. `PawnBehaviorBridge` observes real pawn Need, timetable, map, and
tick state and forwards a typed actor payload to the shared actor runtime.

The current foothold is not yet a first-class retained editor consumer. It has
no manifest-owned editor route, no retained lens/session identity, no Rings or
Time Stream projection, and no explicit next-lens transition receipt. Its
runtime call also uses the Zomboid-specific template key
`actor/runtime/zomboid-day-tick`; that is an honest cross-host wiring proof,
but it is not a generic RimWorld automation contract and must not become the
RimWorld product surface.

## Existing evidence

- `Source/GlueBridgeClient.cs` uses the external runtime-host HTTP boundary and
  reports failures through `LastError`.
- `Source/PawnBehaviorBridge.cs` runs once per in-game hour, reads real pawn
  facts, and remains observational (it does not replace RimWorld JobGiver
  behavior).
- `Seeds/rimworld-need-map.json`, `rimworld-schedule-map.json`, and
  `rimworld-pawn-skill-taxonomy.json` are real host adapter tables grounded in
  the installed RimWorld definitions.
- `NEXT_STEPS.md` explicitly records the missing RimWorld-native behavior
  candidate seed, asynchronous long-event dispatch, and runtime-host build
  prerequisites.

## Shared lens contract to add

The correct next seam is a generic actor-observation receipt produced by the
shared editor runtime and consumed by this adapter. It should carry:

- `lensRef`, retained `sessionKey`, and host/actor identity;
- observed actor values, rates, schedule/world facts, and selected behavior;
- mounted policy/surface keys and active projection (`rings` or `timestream`);
- selected semantic path and optional `nextLensRef` transition;
- revision, timestamp/tick, verification, and diagnostics.

The receipt must be generic to actor hosts. RimWorld should contribute only its
NeedDef/TimeAssignmentDef mappings and native observation facts, never a
Zomboid-named runtime template or bespoke editor UI.

## Safe work decision

No product JSON, HTML, manifest, or seed was edited in this packet. Adding a
new receipt datasource without first landing its shared manifest contract
would create another host-specific parallel implementation, so the safe
source work is deferred until the shared Glue runtime defines that receipt.

## Blockers

1. The shared mounted-session dispatcher still fails to publish dynamic routed
   template results, blocking reliable lens-mounted query/authoring proofs.
2. `dotnet` is not available in the current environment, so the RimWorld mod
   source has no compiler replay here.
3. The runtime-host executable and a live RimWorld save were not launched;
   therefore no native session, Rings, Time Stream, or next-lens continuity is
   claimed.
4. `GlueBridgeClient.Execute` is synchronous on the game tick. Moving it to a
   long-event/background boundary is required before increasing observation
   breadth or cadence.

## Next safe sequence

1. Define and test the generic actor-observation receipt in the shared Glue
   manifest/runtime.
2. Repair dynamic mounted result publication and prove query/aggregate output.
3. Add a manifest-backed RimWorld observation lens that mounts the generic
   receipt, with retained session continuity and one declared next lens.
4. Replace the Zomboid-specific day-tick call with a generic actor-day or
   host-declared runtime template only after that shared contract is real.
