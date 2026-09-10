# RimWorld actor corpus efficacy review

Date: 2026-09-09
Scope: `GlueRimworld/Seeds/rimworld-actors-tenant-manifest.json`, the shared Glue actor component tree, and retained RimWorld Quicktest receipts.

## Result

Yes, we can test the actors corpus manifest in RimWorld, but the current result is a bounded native foothold rather than a full-corpus efficacy claim.

The manifest is valid and editor-visible. It declares the same shared actor domains and observation/decision/execution/persistence tree used by GlueActors, and it keeps RimWorld-specific authority at the adapter boundary. The retained runtime evidence proves one real end-to-end action family (`eat-meal`) plus mount persistence and reconnect behavior.

The important limitation is structural: `RimworldSeedCatalog` currently reads four local seed files directly. It does not consume `rimworld-actors-tenant-manifest.json` or the canonical shared actor-domain manifest. That means the tenant composition is presently a declarative contract and review surface, not yet the runtime's source of truth. Adding more C# before closing this distinction would make the architecture less honest.

## Visibility into simulation history

| Evidence layer | Visibility | What is knowable |
|---|---|---|
| Corpus and manifest history | High | Checked-in tenant composition, shared domain declaration, candidate seeds, authority boundaries, and commit chronology. |
| Adapter path | High | Source shows native observation, candidate filtering, fail-closed gates, JobGiver/WorkGiver admission, and receipt persistence. |
| Historical native behavior | Medium-high | Retained logs expose pawn labels, selected candidates, native admission statuses, receipt keys, mount revisions, and outage/reconnect events. |
| Current live behavior | Low | The current host ports are unavailable and the latest generic Quicktest log does not load GlueRimworld, so there is no fresh live observation to claim. |
| Full simulation efficacy | Low | We have too few positive scenarios, no repeated outcome metrics, and no observed coverage for the other declared candidate families. |

The history is therefore sufficient to audit what was implemented and what actually fired in prior runs. It is not a live colony telemetry stream, and it cannot turn a prior receipt into a current-health claim.

## Actual behavior observed

The strongest retained run, `work/deterministic-native-quicktest.log`, records:

- GlueRimworld loading with `needMap=6`, `scheduleMap=4`, `skills=12`, `candidates=4`, and `nativeMappings=2`.
- The shared engine selecting `eat-meal` for two pawns with `receiptVerified=True`.
- RimWorld's native JobGiver admitting `eat-meal` for both pawns.
- Independently persisted native receipts read back as verified.
- A retained `rimworld-colony-0` mount heartbeat with persistence and readback, rendered with `definition://lens-renderer-projection`.

The reconnect run adds useful negative/positive evidence: engine calls and a mount heartbeat fail while the host is unavailable; later hour-9 calls succeed with the same colony session and verified native receipts. That is evidence of bounded failure and remount behavior, not proof that every in-flight gameplay action reconciles correctly.

The candidate pack also declares `rest-in-bed`, `haul-designated`, and `construct-designated`. The retained native logs do not show those candidates being admitted. Construction has a declared WorkGiver mapping, but a declared mapping is not an observed blueprint/material/reservation transition.

One separate `-nographics` run contains shader errors and repeated `NullReferenceException` noise. It should remain a rejected diagnostic artifact, not be mixed into a green efficacy score. The current `Player.log` quicktest likewise contains no GlueRimworld load marker and is not a Glue test.

## Efficacy judgment

Current efficacy is best described as:

> The declarative actor path can select a known candidate, cross the RimWorld native admission boundary, and produce auditable receipts and mount readback for a narrow fixture. It has not yet demonstrated broad colony behavior, tenant-manifest runtime consumption, or repeated outcome efficacy.

This is a meaningful proof of the mount seam. It is not yet evidence that the actor simulation improves a colony, coordinates a community, handles health/logistics/social domains, or stays correct across long-running save/reload cycles.

## Declarative finish line

The reusable acceptance profile is `Seeds/rimworld-actor-corpus-efficacy-profile.json`. It separates five evidence classes: declared, engine, native, lifecycle, and efficacy. A future run should produce one bounded receipt per scenario and must distinguish:

1. selected by the shared declarative engine;
2. admitted or rejected by native RimWorld authority;
3. actually executed/read back by RimWorld;
4. persisted and reconciled across lifecycle events.

The next fixtures should cover observation of every universal domain, rest/recovery, hauling with reservation awareness, construction with material legality, social/community state, and health/treatment. Each needs one positive case and one typed rejection before it counts toward publication. Unsupported candidates remain declared and fail closed.

## Source gate

Do not add new behavior source until a declarative probe demonstrates which tenant-manifest fields the runtime must consume and emits a missing-consumer receipt for the rest. The first implementation change, if still necessary after that probe, should be the smallest manifest loader/registration seam; it should not duplicate the domain model or move native authority into Glue.
