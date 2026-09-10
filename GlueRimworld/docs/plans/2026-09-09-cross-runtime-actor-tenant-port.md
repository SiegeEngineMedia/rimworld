# Cross-runtime actor tenant port

## Objective

Make GlueRimworld a real `glue-editor` actor tenant with the same shared actor
component-tree architecture used by GlueActors/GlueZomboid, while preserving the
fact that RimWorld already owns most colony simulation authority. The same JSON
behavior and workflow records should be reusable in Zomboid, RimWorld, Vintage,
and future Burn-the-Colonies integrations; only observation and native admission
should vary by tenant.

The declarative tenant contract is
`Seeds/rimworld-actors-tenant-manifest.json`. It composes the shared actor runtime
manifest, the new canonical `glue/schemas/seeds/actors/manifest.json` domain
manifest, and RimWorld extensions. It is intentionally an editor-visible contract
first: no new C# is justified by the manifest alone.

## What the Zomboid review established

GlueActors is currently a mirrored content tenant, not a standalone runtime
implementation. `../zomboid/GlueActors/glueactors-sync-manifest.json` declares which
canonical `glue/schemas/seeds/actors/**` files are copied into the mod tree. The
tenant runtime is declared by `../zomboid/GlueZomboid/manifest.json`, which extends
the shared actor manifest and exposes `GLUE_ActorRuntime`, behavior-candidate
catalogs, actor pack catalogs, mutable actor state, and native bridge surfaces.

The important portable unit is therefore the component tree and its contracts:

1. observe native actor facts;
2. derive pressure and eligibility declaratively;
3. choose a candidate or workflow intent;
4. ask the host-native runtime to admit and execute it;
5. persist/read back a typed receipt and reconcile by semantic identity/revision.

Zomboid's `GLUE_ActorRuntime` makes this boundary visible. RimWorld can use the
same `actorRuntime` vocabulary while routing admission to JobGiver, WorkGiver,
Pawn_JobTracker, managers, or a named bridge. The actor engine should not be
forked into a second RimWorld scorer.

## RimWorld advantage

RimWorld gives the tenant a much richer native observation surface than the current
Zomboid foothold: `Need`, `Pawn_SkillTracker`, `HediffSet`, inventory and Thing
filters, reservations, reachability/regions, WorkSettings, Bills, Research, Rooms,
factions, thoughts, lords, and save serialization. The port should therefore start
with projection and reconciliation, not with simulated need decay or invented job
execution. A Glue candidate is useful only when its native evidence is fresh and
its native admission seam is named.

## Shared component-tree target

```text
actor.identity
  -> actor.observation
      -> actor.needs / skills / health / inventory / world / community
  -> actor.pressure
      -> actor.eligibility
          -> actor.behavior-candidate or actor.workflow-plan
              -> actor.affordance-proposal
                  -> actor.native-admission
                      -> actor.action-receipt
                          -> actor.readback
                              -> actor.reconciliation
```

The tree is shared. The leaf bindings are tenant-specific:

| Shared component | Zomboid binding | RimWorld binding |
|---|---|---|
| Actor identity | PZ actor/embodiment registry | `Pawn.thingIDNumber`, map key |
| Need pressure | PZ needs, stress, infection, threat | `Need.CurLevel`, thoughts, health, threat facts |
| Inventory/logistics | PZ inventory and item effects | Thing filters, mass, reservations, storage, reachability |
| Work candidate | PZ declarative runtime / native action | WorkGiver, JobGiver, JobTracker |
| Community workflow | PZ actor contracts, factions, task plans | pawn relations, factions, lords, policies, storyteller events |
| Receipt | `GLUE_ActorRuntime` and actor stores | GlueRimworld native receipt and retained mount |

## Declarative implementation waves

### Wave 0 — make the tenant composition real

- Keep `GlueRimworld/manifest.json` as the consumer-owned tenant manifest extending
  the shared editor and actor manifests.
- Register the tenant manifest seed and the shared actor component-tree vocabulary
  as editor-visible datasources.
- Run the newly declared GlueActors mirror entry for the canonical actor-domain
  manifest; do not hand-copy shared seeds into source.
- Add a parity fixture that mounts the same actor roster/observation/candidate/
  receipt shape in Zomboid and RimWorld with different native evidence.

### Wave 1 — actor observation envelope

- Expand the existing RimWorld observation envelope to carry stable identity,
  current job, native revision, needs, skills, health, inventory summary, position,
  community references, and world/room facts.
- Use source-backed seed maps for names and tags; do not invent missing NeedDefs.
- Keep `social`, `safety`, and thought-derived values explicitly typed as relational
  or observed facts when they are not vanilla needs.
- Prove stale actor, unloaded map, late join, save/load, and hook-not-fired receipts.

### Wave 2 — portable behavior and workflow candidates

- Port the shared Zomboid candidate shapes: need pressure, urgency bands, schedule /
  work priority, haul routing, medical treatment, defense response, and production
  obligation.
- Add RimWorld bindings only for native targets that can be checked by WorkGiver,
  JobGiver, JobTracker, Bill, or a named bridge.
- Use the existing Glue arbiter, eligibility filter, guarded action kernel,
  inventory resolver, spatial projection, and receipt bridge templates.
- First acceptance targets: batch haul, opportunistic detour, medical treatment,
  and bill ingredient delivery. Each must have a native rejection case.

### Wave 3 — colony and community workflows

- Reuse the Zomboid colony seeds for stockpile policy, reservation policy, squad
  roles, social factions, incidents, and storyteller pressure as portable schemas.
- Bind RimWorld policies to Work/Schedule/Assign managers, pawn relations, factions,
  Lords, and storyteller letters without taking ownership of their serialization.
- Model multi-pawn workflows as claims, obligations, selected actor refs, and
  native command receipts; do not persist Pawn or Thing handles.

### Wave 4 — round-trip back to Zomboid and future tenants

- Move any genuinely universal candidate or workflow back into
  `glue/schemas/seeds/actors/**` with a source-of-truth declaration.
- Let GlueActors mirror it through its declared sync manifest.
- Keep RimWorld, Vintage, and BTC extensions as adapters/profiles, not forks of
  the universal component tree.
- Require shared headless fixtures to exercise selection and receipt semantics, then
  runtime-specific fixtures to exercise native legality and execution.

## Source-code gate

No new runtime source is allowed merely to make the manifest appear complete. A
source change requires a recorded failure showing that an existing declarative
component tree cannot express one of: native observation, native admission, typed
readback, lifecycle/reconnect, or hook-attempt evidence. The adapter must remain a
thin native seam, and an un-fired hook must be recorded as such rather than replaced
with synthetic success.

## Exit criteria

- RimWorld and Zomboid load the same shared actor component-tree vocabulary.
- The editor renders the same identity, observation, candidate, admission, and
  receipt panels for both tenants.
- A candidate can be selected without native mutation, and a native action can be
  admitted/rejected with typed evidence.
- Save/load and reconnect remount by semantic session/actor keys and revisions.
- Shared improvements are canonical in Glue and mirrored into GlueActors; no tenant
  owns a silent fork.
- Unsupported mechanics remain visible as deferred capabilities with explicit
  reasons and no false execution claims.
