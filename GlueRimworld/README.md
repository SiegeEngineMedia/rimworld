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
# from the glue monorepo
dotnet run --project runtimes/csharp/glue-runtime-host
# then load this mod in RimWorld and start/load a colony
```

See `NEXT_STEPS.md` for why this could not be built or run end-to-end on this machine
in this pass (no local .NET SDK), and what that means for the strength of the claim
"wired end-to-end."
