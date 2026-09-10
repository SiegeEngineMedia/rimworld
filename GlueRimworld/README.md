# Glue Rimworld

A foothold RimWorld mod proving GLUE's existing declarative actor engine can drive
real pawn behavior selection, mirroring the shape of `vintage-actors` (Vintage Story)
and `glue-tabletop` (Tabletop Simulator) in the glue monorepo's family of game
integrations -- see `NEXT_STEPS.md` for what a full build-out still needs and for the
honest, disclosed limits of this pass.

## What this is

- A real, loadable RimWorld 1.6 mod skeleton (`About/About.xml`, `Assemblies/`,
  `Source/GlueRimworld.csproj` targeting `netstandard2.1` against the real installed
  game's `Assembly-CSharp.dll`/`UnityEngine.CoreModule.dll`).
- A Harmony-injected `GameComponent` (`Source/PawnBehaviorBridge.cs`) that, once per
  in-game hour, reads every free colonist's real `Need.CurLevel` values and real
  `TimeAssignmentDef` schedule slot, and POSTs them to a locally running
  `glue-runtime-host` process over loopback HTTP (`127.0.0.1:8765`, `POST /api/execute`)
  targeting `actor/runtime/zomboid-day-tick` -- the exact actor-engine template chain
  (`actor/status/status-tick` -> `derive-deficits` -> `actor/runtime/band-pressure-merge`
  -> `actor/behavior/arbiter` -> `actor/status/go-use-affordance`) already landed and
  mechanism-verified by the glue monorepo's Zomboid "one actor's day" lane (Z-1, commit
  `f40f3d710`), using `colony/pawn-need-taxonomy` / `colony/pawn-schedule-templates` /
  `colony/pawn-work-priorities` seed data that originated as RimWorld's own
  `NeedDef`/`TimeAssignmentDef`/`WorkTypeDef` vocabulary.
- The engine's selected behavior candidate is surfaced back onto the pawn visibly
  (a floating `MoteMaker.ThrowText` mote) and in the log, so the wiring is observable
  in-game, not just inert seed data.
- `Seeds/rimworld-work-candidates.json` is a declarative RimWorld content pack for
  the shared route. The adapter projects native meal/bed facts and filters the pack
  before Glue ranks it; native reservation, reachability, designation, and JobGiver
  legality remain RimWorld-owned. Declarative `nativeJobMappings` cover meal
  ingestion and construction through RimWorld's `ConstructFinishFrames` WorkGiver;
  native Job admission is opt-in and fail-closed when a pawn already has a `CurJob`
  or no legal native target exists.
- The adapter also exposes bounded relational facts for the shared candidate
  engine: nearby free-colonist availability as `social`, and RimWorld's active
  hostile-threat result as `safety`. These are disclosed affordances, not
  fabricated RimWorld `NeedDef` values; richer thought-derived semantics remain
  an extensible follow-on.
- The shared editor composition is declared in
  `glue/manifests/editor/manifest.rimworld.json`; it reuses the actor manifest
  and canonical observation/projection/receipt templates. The separate
  `glue/manifests/gluerimworld-csharp.json` is intentionally a bounded host
  bootstrap profile, so the editor surface and game process can share semantics
  without inheriting the full editor catalog into the native host.
- `Seeds/rimworld-session-mount-policy.json` and the shared
  `actor/runtime/rimworld-session-mount-heartbeat` template define the live
  `lens://rimworld` mount declaratively. The adapter supplies native map facts,
  a stable `rimworld-colony-<mapId>` session key, and lifecycle revisions;
  Glue persists/readbacks the envelope and then renders the same canonical
  `lens-renderer-projection` used by GlueZomboid.
- `Seeds/rimworld-actor-session-catalog.json` supplies the matching provider-neutral
  actor identity, observation, typed-rejection, and reconnect-reconciliation
  vocabulary. It keeps client mutation disabled and leaves native authority in
  RimWorld's adapter hooks.
- This consumer's entrypoint is `manifest.json`; it inherits the shared editor
  layer and declares `GLUE_RimworldCandidatePack` as a seed-catalog resource,
  keeping the candidate pack editor-visible without duplicating it into Glue.
- `Seeds/rimworld-workshop-affordance-catalog.json` declares the Steam Workshop
  review set, isolated acquisition plan, source revisions, native authority
  boundaries, and candidate fixtures for mechanics that can enrich this surface.
  The cited synthesis and live/headless harness options are recorded in
  `docs/research/2026-09-09-workshop-affordance-review.md`.
- `Seeds/rimworld-cross-corpus-affordance-map.json` makes the synthesis executable
  as editor-visible data: it maps Zomboid needs/logistics, Vintage control,
  Burn-the-Colonies projected affordances, and shared Glue templates onto each
  RimWorld mod target, with native admission and receipt boundaries.
- `Seeds/` holds the RimWorld-specific adapter tables (`NeedDef` -> glue need key,
  `TimeAssignmentDef` -> schedule mode, and the relocated `pawn-skill-taxonomy.json`
  that the Z-1 lane left behind) -- genuinely new content for this integration, kept
  out of the shared glue monorepo since nothing in the shared engine consumes it yet.

## Why the bridge is HTTP, not an in-process DLL reference (unlike vintage-actors)

`vintage-actors` references `GlueCore.dll`/`GlueFp.dll`/`GlueServer.dll` (glue's real,
first-class `runtimes/csharp` port) directly and calls a synchronous `RenderTemplate`
API in-process, because Vintage Story's own server process is itself modern .NET.
RimWorld hosts mod assemblies inside Unity's Mono runtime at the `netstandard2.1` API
level; `runtimes/csharp` multi-targets `net8.0;net10.0` (modern CoreCLR) and cannot be
loaded by that host at all. `runtimes/csharp/glue-runtime-host` already runs those
assemblies out-of-process and exposes exactly the render call this mod needs
(`GET /ping`, `POST /api/execute`) over plain loopback HTTP -- reused as-is via
`System.Net.Http.HttpClient` (a real `netstandard2.1` API), not reinvented.

## Running it (once glue-runtime-host is available)

```
# run from the glue monorepo root
$env:GLUE_MANIFEST_PATH = "manifests/gluerimworld-csharp.json"
$env:GLUE_TEMPLATES_ROOT = "work/csharp-engine-root/templates"
dotnet run -c Release --project runtimes/csharp/glue-runtime-host
# then load this mod in RimWorld and start/load a colony
```

The native C# lane is now buildable on this machine: SDK 9.0.318/10.0.401 and the
.NET 8 runtime are installed, and the Release build produces the merged
`Assemblies/GlueRimworld.dll`. The bounded shared actor profile has a live Glue
MCP create -> validate -> save proof and the packaged mod has consumed real
engine receipts in Quicktest. The opt-in native lane now admits `eat-meal` through
RimWorld's JobTracker boundary and exposes a WorkGiver-backed construction mapping;
native receipts are independently read back and busy/no-target pawns remain
fail-closed. The official `.rws` save/load probe
also passes, including the `LoadedGame` reset hook, and a live host-outage
Quicktest remounts the same session after host relaunch. Cross-runtime
replay/session parity and broader native affordances remain before a publishable
"wired end-to-end" claim;
the host and mod now have reproducible zero-error Release builds. See
`NEXT_STEPS.md` for the remaining full-corpus, replay/session, and native-affordance
gates.

## Declarative headless fixture

`Seeds/rimworld-headless-world-fixture.json` is the deterministic world/actor input
for `Seeds/rimworld-headless-harness.json`. The harness resolves that fixture,
invokes the declared template over the bounded Glue host contract
(`POST /api/execute`), and emits a structured receipt only after the declared
assertions pass and the loopback lease is released. The fixture contains semantic
facts and revisions, never RimWorld object handles; native legality remains outside
the fixture. This makes the harness reusable for later Pick Up And Haul, While
You're Up, and Dubs Bad Hygiene fixtures without changing the invocation contract.

## Workshop cache structural review

The checked-in `Seeds/rimworld-workshop-structural-review.json` limits inspection to
the seven IDs in the affordance catalog, including explicit evidence when a payload
is missing or lacks a 1.6 directory. Run the read-only scanner against Steam's cache:

```powershell
.\Tools\scan-workshop-structural-review.ps1 `
  -WorkshopRoot "C:\Program Files (x86)\Steam\steamapps\workshop\content\294100"
```

It emits a bounded JSON receipt containing package metadata, version directories,
assembly names, XML capability evidence, and unavailable/review-gap facts. It does
not enable mods, copy third-party files, launch Steam/RimWorld, or infer native
legality from a filename. The route is declared in
`Seeds/rimworld-help-tooling-lens-policy.json`: this local cache mode is a sanctioned
fallback and its receipt must say that it is not equivalent to the Glue SteamCMD lens
or the embedded editor lifecycle.
