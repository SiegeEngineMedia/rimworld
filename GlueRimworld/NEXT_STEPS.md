# Next Steps

Honest accounting of what this foothold pass did and did not verify, and what a
follow-on pass should do next -- "foothold, not full build-out" per the mission.

## What is real vs. what is unverified in this pass

- **Real, verified against the installed game**: RimWorld 1.6.4871 is actually
  installed on this machine (`C:\Program Files (x86)\Steam\steamapps\common\RimWorld`).
  `About/About.xml`, the schedule/need/work-type vocabulary in `Seeds/`, and the
  `netstandard2.1` target framework decision were all checked against the real
  `Data/Core/Defs/**` XML and the real `RimWorldWin64_Data/Managed/*.dll` list on this
  machine, not invented from memory.
- **Real, verified against the glue monorepo**: `actor/runtime/zomboid-day-tick`'s
  input/output contract (`{hour, actorId, values, rates, worldFacts, observers,
  detectionRange}` -> `{scheduleBand, selectedId, selectedVerb, ...}`), the
  `HttpTransport` `/api/execute` route contract, and the already-landed
  `colony/pawn-need-taxonomy` / `colony/pawn-schedule-templates` /
  `colony/pawn-work-priorities` seeds (commit `f40f3d710`) were read directly from the
  glue monorepo source, not guessed.
- **NOT verified by compilation or execution**: this machine has no .NET SDK
  installed (`dotnet` is not on PATH in either shell). `Source/GlueRimworld.csproj`
  and its four `.cs` files were written to be structurally and API-correct against
  well-established, stable RimWorld modding APIs (`Verse.Mod`, `GameComponent`,
  `Pawn.needs`/`Pawn.timetable`, `GenLocalDate.HourOfDay`, `MoteMaker.ThrowText`,
  Harmony's `Game.FinalizeInit` postfix injection pattern) but were never actually
  built, so a real compiler has not checked them. Likewise `runtimes/csharp/glue-
  runtime-host` was never actually started on this machine (also blocked by the
  missing .NET SDK), so the HTTP round-trip this mod depends on has not been
  exercised live end-to-end in this pass.
- **The Rust-side proof this pass DID attempt**: `templates/tests/actor/zomboid-day/
  deterministic-day-test.json` (the Z-1 lane's own aggregate acceptance test, whose own
  description discloses it as "rust-verification-pending" -- never actually executed,
  only schema-validated plus individually hand-probed) was run for real via the
  sanctioned `template_selected_tests` rail in this pass's worktree. It did not reach a
  pass/fail verdict: the worktree's cargo build failed on an unrelated, pre-existing
  break (`manifest process BuildPlan games/tictactoe/lib/tictactoe-play-turn has no
  source template`), a base-branch issue with nothing to do with actors, Zomboid, or
  RimWorld. This is disclosed rather than hidden -- it means the strongest available
  "real trace" evidence for this specific pass is the mechanism-by-mechanism
  verification already recorded in commit `f40f3d710`'s own message (seven hand-
  computed tick outcomes, both quirk gates, the shame-appraisal raise), not a fresh
  run produced here.

## Concrete follow-on work

1. **Fix or route around the tictactoe build break**, then actually run
   `deterministic-day-test.json` (and, once it passes, a RimWorld-shaped variant) via
   `template_selected_tests` -- this closes the Z-1 lane's own disclosed gap and gives
   GlueRimworld a real, fresh, green Rust trace to point to instead of relying on the
   Z-1 commit message's hand-verification.
2. **Install a .NET SDK on a build machine and actually compile** `runtimes/csharp`
   (`GlueCore`/`GlueFp`/`GlueServer`/`glue-runtime-host`) and `GlueRimworld.csproj`,
   then run `glue-runtime-host` and load this mod in a real RimWorld save to get an
   actual in-game trace (log lines + motes) -- the genuine "full build-out" proof this
   pass could not produce given the missing SDK.
3. **Author a RimWorld-flavored behavior-candidate seed set** (e.g.
   `rimworld-work-candidates.json`: work/mining, work/cooking, work/hauling, ... skill-
   gated via `Seeds/rimworld-pawn-skill-taxonomy.json`), mirroring
   `zomboid-day-labor.json`'s shape exactly, through the glue MCP session authoring
   loop (open -> batch -> validate -> save, per the monorepo's `templates/**`
   fail-closed write policy) -- `zomboid-day-tick`'s candidate verbs today are still
   Zomboid-flavored (forage/cook/repair-car), which is honest evidence the *engine
   wiring* works but not yet evidence of RimWorld-*flavored* content.
4. **Model `has-kitchen`/`has-garage`/`has-car-damage`/daylight properly** instead of
   the placeholder `worldFacts` in `PawnBehaviorBridge.cs` (real building/affordance
   lookups via `Map.listerBuildings`, real `GenCelestial` daylight calc) so cook/
   repair-car-equivalent candidates can actually win a tick rather than structurally
   losing to wake/forage/sleep.
5. **Replace the `social`/`safety` need placeholders** with something real (mood
   Thought-derived isolation signal for social; hazard/manhunter-pawn proximity for
   safety) -- both are currently honest constants, disclosed as such in
   `Seeds/rimworld-need-map.json`'s `unmappedGlueNeeds`, not real signals.
6. **Move `GlueBridgeClient.Execute` off the blocking call it is today** onto
   RimWorld's long-event queue or an async pattern, once colonist counts grow past a
   handful.
7. **Resolve the bundled-`Newtonsoft.Json` collision risk** flagged in
   `Source/GlueRimworld.csproj` (ILRepack/rename, or depend on a shared
   Newtonsoft.Json "library" mod) before treating this as more than a foothold.
8. **Decide whether `GlueBehaviorTickComponent` should ever actually override
   RimWorld's own `JobGiver` stack** (start a real `Job` from the engine's selection)
   versus staying purely observational (motes + logs) -- this pass deliberately chose
   observational-only as the safer foothold scope; a full build-out needs an explicit
   decision here, not a silent one.
