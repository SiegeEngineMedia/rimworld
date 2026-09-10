# RimWorld full UI playability plan

The target is a RimWorld player who can complete a colony loop through the Glue
semantic UI surface: survey the current screen, navigate by named controls,
select a native target, issue an action, read back the native result, and recover
after save/load or reconnect. The target is not a second game UI or a coordinate
automation layer.

The contract is declared in `Seeds/rimworld-playable-ui-surface.json` and is
bound to `Seeds/rimworld-accessibility-lens.json`. It follows the stronger
Zomboid accessibility shape: bounded native survey, snapshot-addressed locators,
fresh re-resolution before dispatch, stable semantic identities, and typed
receipts for success, rejection, staleness, disconnection, and hooks that did not
fire.

## Surface ladder

| Layer | Surface families | Current state |
|---|---|---|
| Entry | main menu, load/save, dialogs | declared; native survey deferred |
| Core play | colony shell, time, camera, selection, inspector | colony projection partially proven; UI dispatch deferred |
| Colony management | work, schedule, architect, designations, inventory | declarative domains exist; native UI proof deferred |
| Actor care | health, medical, social, faction | domain contracts declared; native UI proof deferred |
| Conflict/world | combat, threat, world map, travel | native targeter and route proof deferred |
| Continuity | save/load, reconnect, stale selection, focus return | mount lifecycle partially proven; full UI reconciliation deferred |

## Native boundary

RimWorld remains authoritative for `WindowStack`, dialogs, `Selector`, camera and
map bounds, `TimeController`, work settings, designators, reservations,
reachability, targeters, jobs, world travel, and save lifecycle. Glue may project
these as accessible nodes and submit a declared semantic action. It may not
invent a target from a display name, bypass a targeter with screen coordinates,
optimistically mutate gameplay, or treat a missing hook as success.

Every dispatch must carry:

- the retained `sessionKey`;
- a fresh `snapshotId`;
- the current native revision;
- a stable locator for the current node or target;
- a declared logical action.

The adapter must return a native readback or a typed receipt. A stale snapshot
causes a fresh survey and, when necessary, a rejection. A missing or pending
hook remains visible as `not-fired` or `pending`.

## Proof order

1. Prove main-menu survey and semantic load/new transition.
2. Prove colony-shell survey, pause/speed, camera navigation, selection, and
   inspection using stable locators.
3. Prove one positive and one typed rejection for work, designation/build,
   inventory transfer, medical action, dialog choice, and combat target.
4. Prove world-map travel legality and return-to-colony continuity.
5. Prove save/load, reconnect, stale-locator rejection, and semantic focus return.
6. Run the whole loop from the declared entry surface with no hidden host-only
   step and no coordinate/OCR fallback.

The current RimWorld bridge has real observation and native `eat-meal` receipt
evidence, but it does not yet expose a native RimWorld UI tree or logical UI
dispatcher. Therefore this plan is a publishable declaration of the finish line,
not a claim that the whole game is currently playable through the lens.

