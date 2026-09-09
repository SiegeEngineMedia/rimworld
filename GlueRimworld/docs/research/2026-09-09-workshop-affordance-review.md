# RimWorld Workshop affordance review

## Scope and status

This review selects Workshop projects that add observable mechanics, AI/job behavior,
policy systems, or extensibility useful to GlueRimworld. It treats each mod as a
runtime surface to project, not as code to copy. The companion declarative catalog is
`Seeds/rimworld-workshop-affordance-catalog.json`; it records Workshop IDs, source
revisions, candidate families, authority boundaries, and acceptance fixtures.

The SteamCMD download plan is declared but not yet executed: SteamCMD is not currently
installed on this machine. Seven public source repositories were downloaded into the
isolated review root `C:\Users\joshu\Projects\glue\workshop-review-20260909` and
reviewed at the revisions recorded in the catalog. No third-party source or asset is
being redistributed by GlueRimworld.

## What the community implementations teach us

The strongest common pattern is a four-layer boundary:

1. RimWorld owns facts and legality: Needs, map objects, reservations, reachability,
   WorkGivers, JobGivers, Pawn job tracking, and save serialization.
2. A mod adds a bounded affordance or policy: a candidate such as “haul on the way,”
   a policy bundle such as “emergency,” or a new need such as hygiene.
3. The mod asks the native engine to validate and execute the action. It does not
   reconstruct pathing, storage acceptance, or target legality in a second simulator.
4. The user-facing layer reports the selected intent, native admission, lifecycle,
   and failure outcome.

That is a close fit for Glue: declarative ranking and policy selection, a typed intent,
native admission, and a durable receipt/readback projection. The important design
distinction is between a *proposal* and an *admission*. Glue may rank a candidate or
activate a policy, but RimWorld must remain authoritative over whether the action is
legal now.

## Review matrix

| Project | Mechanics worth projecting | Native seam to preserve | Glue target | Priority |
|---|---|---|---|---|
| [Common Sense](https://steamcommunity.com/sharedfiles/filedetails/?id=1561769193) | Need-aware recreation/drugs, clean-before-task, spoilage-aware food and ingredients, inventory recovery | Think trees, JobDefs/JobDrivers, FoodUtility, WorkGivers, pawn inventory | Behavior-candidate pack with `clean-before-task`, `choose-spoilage-pressure`, and `recover-interrupted-inventory` | P2 |
| [Pick Up And Haul](https://steamcommunity.com/sharedfiles/filedetails/?id=1279012058) | Multi-item inventory hauling, capacity allocation, queued unloads | `WorkGiver_HaulToInventory`, `JobDriver`, reservation, reachability, storage and mass utilities | Workflow candidate with constraint trace and queued native targets | P0 |
| [While You're Up](https://steamcommunity.com/workshop/filedetails/?id=2034960453) | Opportunistic hauling, closer-to-job supply detours, path-cost budgets, safety exclusions | WorkGiver scanner, pathfinder, `TraverseParms`, storage lookup, opportunistic prefix | Conditional detour affordance preserving the original job | P0 |
| [Better Pawn Control](https://steamcommunity.com/workshop/filedetails/?id=1541460369) | Named work/schedule/assign policies, emergency switch, arrival defaults, per-map state | Work/Schedule/Assign managers, map identity, `Scribe` references and deep state | Policy bundle and retained-session command | P1 |
| [Achtung!](https://github.com/pardeike/Achtung2) / [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=730936602) | Force work, multi-pawn commands, formations, auto-combat, optional multiplayer synchronization | Float-menu options, forced jobs, native target resolution, MultiplayerAPI | Explicit command contract with native eligibility and readback | P1 |
| [Dubs Bad Hygiene](https://steamcommunity.com/sharedfiles/filedetails/?id=836308268) | Hygiene/bladder needs, water/sewage, urgent and ordinary need satisfaction, work systems | NeedDefs, ThinkTreeDefs, JobGivers, WorkGiverDefs, ThingDefs, map/resource state | Need-system and resource-network candidate pack | P0 |
| [Vanilla Expanded Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013) | Shared gizmos, mode switches, hediffs, custom UI, comps, animal/event/quest utilities | Def extensions, ThingComps, gizmos, Harmony, framework dependency and save lifecycle | Extension-capability catalog; reference architecture, not copied implementation | P1 |

### Pick Up And Haul and While You're Up: the closest immediate fit

Pick Up And Haul's source makes the native boundary concrete. Its haul WorkGiver
filters spawned things through reservation, forbidden-state, automatic-haul, storage,
and carry-capacity checks. It then constructs a native job with item and storage
queues. Glue should therefore emit a semantic batch-haul intent plus the observed
constraint trace; it should never decide which cells are reservable or whether a
storage target accepts a Thing.

While You're Up adds a second useful shape: a detour is valid only when it remains
within configurable distance/path budgets and does not violate bleeding, caravan,
reservation, or reachability constraints. Its source also exposes a modder-facing
extension point through `JobDef.allowOpportunisticPrefix`. This is a good model for a
declarative Glue affordance: `originalJob`, `detourCandidate`, `constraints`, and
`resumeOriginalJob`, with native execution and a rejection receipt.

Together they should become the first richer example build target after the current
`eat-meal` lane. The acceptance fixture is already declared: batch two compatible
stacks, preserve native storage legality, then prove queue, native admission, and
receipt/readback. The detour fixture must prove that a rejected route leaves the
original job unchanged.

### Dubs Bad Hygiene: the need-system stress target

Dubs Bad Hygiene is the best test of whether GlueRimworld can enrich simulation rather
than only rename existing behaviors. Its 1.6 patch tree inserts hygiene, toilet, and
water JobGivers into ordinary, basic-need, very-urgent, and Hospitality relaxation
trees. Its definitions add WorkGivers for plumbing, fluids, washing, and related work.
The Workshop page describes a large system of hygiene needs, water, sewage, irrigation,
heating, and waste, and explicitly warns that removal from saves is not easy.

The Glue representation should add typed need facts and resource-network facts to the
same actor observation envelope used for Food and Rest. The candidate pack may rank
`wash`, `use-toilet`, or `drink-water`; the native JobGiver must still decide whether a
fixture, path, resource, capacity, and current job make it legal. Persistence must
record only stable semantic references and revisions, never native Thing handles.

### Better Pawn Control: policy and mount persistence

Better Pawn Control is less about autonomous AI and more about reusable policy state.
Its Workshop description identifies presets across Work, Schedule, Assign, and Animal
tabs, emergency switching, arrival defaults, and improved handling of multiple maps.
The source review shows `Scribe`-backed policy links, map identity, and nested policy
state.

This is exactly the retained-mount problem in a different vocabulary. A Glue policy
command should carry a stable `mapKey`, `policyKey`, `expectedRevision`, and desired
semantic policy. Native RimWorld applies the Work/Schedule/Assign changes. Glue persists
the command outcome and can remount the projection by map key after save/load, while
rejecting stale revisions and unknown pawn identity.

### Achtung!: explicit command contracts and test harnesses

Achtung's current repository is particularly valuable as testing guidance. Its README
describes a paired mod/bridge workflow using RimBridgeServer, GABS for game lifecycle,
and DecompilerServer for managed-assembly inspection. Its testing guide recommends
using the least expensive layer that answers the question: source/assembly inspection,
then deterministic no-tick contracts, then asynchronous tick-driven scenarios, and
visual interaction only where UI state requires it. It also requires the game to be
stopped before deployment and uses named bridge contracts for real native behavior.

GlueRimworld should adopt the same ordering: declarative manifest and headless host
first; native no-tick eligibility/readback second; a bounded real-tick scenario third;
visual Glue Editor validation last. The existing C# runtime should grow no new general
gameplay simulator. Any additional code should be a narrow adapter for a declared
native contract, with a hook-attempt/not-fired receipt when the game never reaches the
relevant lifecycle boundary.

### Common Sense and VEF: compatibility and extensibility references

Common Sense demonstrates how quickly a seemingly simple AI improvement becomes a
large patch surface: food packing thresholds, cleaning, social recreation, drug
choices, spoilage, inventory, and compatibility fallbacks. It is a useful P2 target
for validating candidate composition and conflict reporting, but not the first native
execution target.

Vanilla Expanded Framework is a reference for extensible capability vocabulary. Its
Workshop description explicitly positions it as a shared library for custom gizmos,
hediffs, UI, ThingComps, animal behavior, genes, events, and quests. Glue should model
these as capability references and observed native state, not copy framework classes.
The framework's license and dependency model make this distinction important: use
public contracts and examples as integration guidance, retain attribution and
dependency metadata, and do not bundle its implementation into GlueRimworld.

## Declarative synthesis for GlueRimworld

Every reviewed mechanic can be normalized to this record shape:

```json
{
  "affordanceId": "haul-on-way",
  "kind": "conditional-detour",
  "inputs": ["currentJobRoute", "haulableFacts", "storageFacts", "healthSafetyFacts"],
  "constraints": ["native-reservation", "native-reachability", "max-total-trip-ratio"],
  "nativeAdmission": "WorkGiver-or-JobGiver",
  "glueResult": "intent-and-constraint-trace",
  "receipt": "native-admission-or-typed-rejection",
  "persistence": "stable-semantic-refs-and-revision-only"
}
```

The next catalog additions should be declarative fixtures, not new C# behavior:

- `batch-haul` and `haul-on-way`, using the existing candidate pack and native mapping
  boundary;
- `wash`, `use-toilet`, and `drink-water`, with typed hygiene/resource observations;
- `policy-activate` and `emergency-policy`, with map revision and save/load proof;
- `force-work`, with explicit command authority and a named native readback contract.

Only after those fixtures fail for a demonstrated adapter gap should a new source seam
be considered. The source seam must be selected by the manifest/catalog, fail closed
when absent, and emit evidence even when its hook never fires.

## Download and validation sequence

1. Install SteamCMD outside the live RimWorld Mods directory.
2. Execute the declared `steamcmd-workshop-command-plan` for app `294100` into an
   isolated staging root and verify each Workshop ID, package ID, version directory,
   dependency, and checksum.
3. Run the declared `workshop-profile-plan` to normalize the selected set without
   enabling it in the live game.
4. Compare staged About/Defs/source metadata with the catalog; mark version drift,
   removed items, and save-impact warnings as typed review results.
5. Generate Glue candidate/policy fixtures from the reviewed behavior descriptions.
6. Run the bounded headless harness, then native no-tick and real-tick fixtures,
   then save/load remount and editor lifecycle validation.
7. Package only the GlueRimworld artifacts and dependency metadata. Do not package
   Workshop payloads or third-party source.

## Sources

- [Common Sense Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=1561769193) and [Common Sense source](https://github.com/catgirlfighter/RimWorld_CommonSense)
- [Pick Up And Haul Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=1279012058) and [source](https://github.com/Mehni/PickUpAndHaul)
- [While You're Up Workshop page](https://steamcommunity.com/workshop/filedetails/?id=2034960453) and [source](https://github.com/CodeOptimist/rimworld-jobs-of-opportunity)
- [Better Pawn Control Workshop page](https://steamcommunity.com/workshop/filedetails/?id=1541460369) and [source](https://github.com/voult2/BetterPawnControl)
- [Achtung! source README](https://github.com/pardeike/Achtung2/blob/master/README.md), [Achtung testing guide](https://github.com/pardeike/Achtung2/blob/master/TESTING.md), and [Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=730936602)
- [Dubs Bad Hygiene Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=836308268) and [source/issues](https://github.com/Dubwise56/Dubs-Bad-Hygiene)
- [Vanilla Expanded Framework Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013) and [source](https://github.com/Vanilla-Expanded/VanillaExpandedFramework)
