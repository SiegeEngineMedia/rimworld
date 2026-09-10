# RimWorld Workshop affordance review

## Scope and status

This review selects Workshop projects that add observable mechanics, AI/job behavior,
policy systems, or extensibility useful to GlueRimworld. It treats each mod as a
runtime surface to project, not as code to copy. The companion declarative catalog is
`Seeds/rimworld-workshop-affordance-catalog.json`; it records Workshop IDs, source
revisions, candidate families, authority boundaries, and acceptance fixtures.

The SteamCMD download plan remains declared for reproducible acquisition, but it was
not needed for this local preflight because RimWorld is already installed through
Steam. Steam's `appworkshop_294100.acf` records six of the seven selected Workshop IDs
as installed and the corresponding payloads are present under
`steamapps/workshop/content/294100`: five have an explicit `1.6` payload directory;
Better Pawn Control's original ID is absent, and While You're Up's cached payload has
no `1.6` directory. The game install's `Mods` directory currently contains only
`brrainz.harmony` and `glue.gluerimworld`; this is expected because Steam-managed
Workshop content is loaded from the separate Workshop content root. The review keeps
that live directory untouched and stages copies separately. Seven public source repositories were also
downloaded into the isolated review root
`C:\Users\joshu\Projects\glue\workshop-review-20260909` and reviewed at the
revisions recorded in the catalog. No third-party source or asset is being redistributed
by GlueRimworld.

### Local Steam preflight

| Check | Result | Review meaning |
|---|---|---|
| RimWorld install | Present locally | Native 1.6 assemblies and the game-side test surface are available |
| Steam Workshop manifest | Present; no update/download required | Steam has a durable installed-item record for the local app |
| Selected payloads | 6 present, 1 missing | Six can be reviewed from the local cache; Better Pawn Control still needs acquisition or source-only review |
| Explicit 1.6 payload | 5 present | Common Sense, Pick Up And Haul, Achtung!, Dubs Bad Hygiene, and Vanilla Expanded Framework have 1.6 directories |
| While You're Up | Cached, supported versions stop at 1.5 | Keep it source-reviewed and fail closed until a 1.6 payload is confirmed |
| Live `Mods` directory | Harmony + GlueRimworld only | Do not infer Workshop absence from this directory; Steam content is separate |

This gives us a clean build/test baseline: payload presence is an acquisition fact,
version-directory presence is a compatibility hint, and enabled test-profile membership
is a separate gate. No review payload is silently enabled in the live game.

The repeatable structural pass is `Tools/scan-workshop-structural-review.ps1` driven by
`Seeds/rimworld-workshop-structural-review.json`. It scans only the catalog's known
Workshop IDs and emits a bounded receipt with package metadata, supported-version and
directory evidence, assembly names, declared XML capability vocabulary, and explicit
missing/review-gap facts. This is the right pre-build lens: it can tell the Glue actor
surface that a mechanic has `NeedDef`, `JobGiver`, `WorkGiverDef`, `ThingComp`, or
`PatchOperation` evidence without pretending that the mechanic is legal or executable
in the current native world.

The current local receipt classifies Common Sense, Pick Up And Haul, Achtung!, Dubs
Bad Hygiene, and Vanilla Expanded Framework as `ready-for-1.6-structural-review`;
While You're Up is `source-or-payload-review-only` because both its cached payload and
metadata lack 1.6 evidence; Better Pawn Control is `missing-payload`. The receipt also
confirms `thirdPartyFilesCopied=false`, so this review remains metadata-only and does
not turn the live Steam installation into a mod test profile.

### Lens-bypass audit

The first pass used three sanctioned-but-non-equivalent fallbacks: direct inspection
of Steam's cache instead of executing the Glue SteamCMD command-plan, a local
structural scanner instead of a Glue receipt, and direct bounded-host HTTP execution
instead of the full editor lifecycle. Those routes were useful under the unavailable
host hook, but they were too easy to mistake for primary-lens proof. The companion
`Seeds/rimworld-help-tooling-lens-policy.json` now declares the primary routes,
fallback evidence, forbidden substitutions, and required equivalence/deferred-gate
fields. Future receipts must say which route ran; a cache hit can no longer imply a
subscription, and a host assertion can no longer imply editor validation.

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

## Online and headless harness options

There is a credible external harness pattern to pair with Glue's existing bounded
host. [RimBridgeServer](https://github.com/pardeike/RimBridgeServer) runs inside
RimWorld and exposes semantic inspection, native actions, settings, saves,
screenshots, and small JSON/Lua automation surfaces. It is designed to stay close
to RimWorld's logical seams rather than simulate gameplay outside the process.

[GABS](https://github.com/pardeike/GABS) supervises the game process and mirrors the
in-game bridge into a stable MCP surface. Its configuration-first model declares a
game once, supports named launch profiles, and re-reads configuration on the next
call. The bridge contract is deliberately environment-driven: the game-side bridge
reads `GABP_SERVER_PORT`, `GABP_TOKEN`, `GABS_GAME_ID`, and optional profile context
from the launched process environment; `bridge.json` is diagnostic state, not a
discovery fallback. This matches Glue's retained-session and stale-credential
discipline.

The recommended GlueRimworld harness stack is layered:

| Layer | Tooling | Evidence |
|---|---|---|
| Content acquisition | Glue `steamcmd-workshop-command-plan` and `workshop-profile-plan` | isolated Workshop staging, package/dependency/version checks |
| Declarative runtime | Glue C# bounded host and `rimworld-headless-harness.json` | template results, zero host errors, deterministic receipts |
| Native no-tick | RimBridgeServer named bridge contract | eligibility, native target resolution, typed rejection |
| Real simulation | GABS + RimBridgeServer | real ticks, pathing, jobs, settings, saves, UI state |
| Source/assembly inspection | DecompilerServer or source checkout | exact patched/native call path, no guessed API |
| Visual/UI verification | semantic bridge/UI screenshot tools | only for behavior that cannot be proven structurally |

This is an online option in the useful engineering sense: the MCP/bridge can expose
the live game to an AI client, while all gameplay authority stays in the game process.
It is not a cloud-hosted RimWorld server and should remain loopback-scoped unless a
separate authenticated deployment boundary is deliberately designed.

The [GABS configuration guide](https://github.com/pardeike/GABS/blob/main/docs/CONFIGURATION.md)
provides an important rule for GlueRimworld: use `games_status`/`games_connect` to
observe an existing process, use the declared lifecycle to start and stop it, and
never recover a stale live endpoint from a cache file. The [RimWorld debugging stack
guide](https://github.com/pardeike/RimBridgeServer/blob/main/docs/rimworld-mod-debugging-stack.md)
recommends the same order: install the in-game bridge, configure GABS, start the game
through the supervisor, then inspect the live session.

## Download and validation sequence

1. Prefer the installed Steam Workshop cache when the app manifest and payload are
   present; otherwise install SteamCMD outside the live RimWorld Mods directory.
2. Execute the declared `steamcmd-workshop-command-plan` for app `294100` only for
   missing or stale IDs, into an isolated staging root, and verify each Workshop ID,
   package ID, version directory, dependency, and checksum.
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
