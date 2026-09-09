# GlueRimworld release checklist

GlueRimworld has a compiler-green foothold and a real native Quicktest proof,
but it is not yet declared publishable. The target remains the same embedded
Glue Editor/manifest surface as GlueZomboid: shared JSON selects an intent,
RimWorld supplies facts and native legality, and the adapter only transports,
projects, admits, observes, and reconciles.

## Current evidence

- .NET SDKs are installed and the mod builds deterministically against the
  installed RimWorld 1.6.4871 assemblies.
- The consumer manifest inherits the shared RimWorld editor manifest and
  declares the candidate pack, mount policy, actor/session catalog, and
  retained mount-session map.
- The bounded C# host harness passes the three response-curve branches, the
  RimWorld-namespaced day-tick receipt, and mount heartbeat/readback.
- A packaged native run has admitted real `eat-meal` work through RimWorld's
  JobTracker boundary, verified receipts, save/load remount, and canonical
  renderer projection with zero targeted bridge/host errors.
- The adapter contains no behavior scorer. Candidate selection is declarative;
  native JobGiver/WorkGiver checks remain authoritative and fail closed.

## Required release order

1. Reconcile the publish checkout with upstream Glue commit `d5d2aa042d`
   (`fix(csharp): register array insert transform`). The tested local Glue
   artifact came from `f7e6f582a91d`, which predates that C# registration. Do
   not duplicate the transform in RimWorld or retain the temporary JSON
   response-curve rewrite as a package fork.
2. Rebuild the bounded C# host from the reconciled source. Run the registry
   test and the original shared `response-curve-eval` template, recording the
   source commit and built-artifact hash.
3. Run `Seeds/rimworld-headless-harness.json` through the declared bounded
   profile and require host ping, zero host errors, all assertions, and release
   of the loopback port lease.
4. Build the deterministic mod package and compare every staged manifest,
   seed, metadata, and assembly hash with the source package.
5. Run the isolated native Quicktest with the C# host live. Require shared
   JSON selection, native admission, verified action/readback receipts,
   retained mount heartbeat, canonical projection, save/load revision
   continuity, host reconnect, and fail-closed busy/illegal-target behavior.
6. Validate the inherited editor manifest and exercise the retained editor
   session through the sanctioned Glue lifecycle (`open -> status -> validate
   -> save -> close`). Do not substitute direct JSONL or file writes for the
   declared tooling path.
7. Re-run the shared RimWorld/Zomboid mount-parity fixture and package only
   after the parity output is byte-stable across consecutive runs.

## Remaining gates

The detailed evidence, commands, logs, and known harness limitations live in
[the transform-provenance handoff](docs/handoffs/2026-09-09-csharp-transform-provenance.md).
Before publication, close the upstream-source/package reconciliation, the
fresh full shared-template probe, live editor validation, reconnect/replay
parity, broader native affordance fixtures, and the full-corpus test-harness
diagnostic. Keep every unsupported native affordance explicitly fail-closed.
