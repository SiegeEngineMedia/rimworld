# C# transform provenance and declarative admission plan

This note records the native RimWorld legitimacy audit. It is deliberately
separate from the mod's game-facing adapter: no RimWorld decision logic was
added to compensate for a Glue runtime defect.

## Evidence

The shared C# actor closure was exercised through
`actor/runtime/zomboid-day-tick` and the RimWorld candidate pack. The complete
host audit must combine stdout and stderr. The first audit was built from the
checked-out Glue `main` at `f7e6f582a91d` (a local branch 167 commits behind
`origin/main`). Canonical upstream history contains `d5d2aa042d`
(`fix(csharp): register array insert transform`), and `origin/main` contains
its implementation and registry test. The missing-transform result below is
therefore release-provenance evidence for the tested local artifact, not a
RimWorld behavior gap.

| signature | count in failed native run | provenance | disposition |
| --- | ---: | --- | --- |
| `TRANSFORM_MISSING: array-insert-at` | 120 | The tested local C# `ArrayTransforms.Register` did not register the transform. Upstream `origin/main` now contains the general C# registration in `d5d2aa042d`; the tested host/package predates that commit. | Reconcile the publish build with the upstream implementation and rerun the original shared template. Do not add a RimWorld adapter workaround or duplicate the runtime implementation. |
| `array-nth` JObject-versus-JArray | 60 | Cascade from the missing transform returning its `{array,index,value}` envelope as `_lowerPadded`; `array-nth` then correctly rejects that object. | No independent RimWorld or authored-input fix. It disappeared in the isolated declarative probe. |

The isolated host probe against a temporary work copy of
`templates/functional/actor/behavior/response-curve-eval.json` returned correct
linear, quadratic, and piecewise results with zero stderr errors. This was a
diagnostic proof that the failure was declarative and isolated; it is not the
publish target now that the upstream C# transform exists. The fresh
180-second native run then recorded:

- 6 engine selections and 6 native `JobGiver` admissions;
- 12 verified execution receipts and 3 mount heartbeat/readback events;
- zero bridge failures, zero host warnings, zero host errors, and zero
  transform diagnostics.

The native run is
`GlueRimworld/work/namespace-declarative-probe-20260909-130824.log`; the host
streams are the matching `.log` and `.err.log` files. The 75-second attempt
before it is intentionally not counted: it stopped before the pawn fixture
reached its assertions, and one later attempt correctly exposed a port
collision and failed closed.

## Headless gate

The new declarative harness at
`Seeds/rimworld-headless-harness.json` is runnable without launching RimWorld.
It uses the same bounded `gluerimworld-csharp` profile and isolated template
root, expands the candidate pack by declared file reference, and asserts three
response-curve branches, one RimWorld-namespaced day-tick receipt, and one retained
mount heartbeat/readback. Its runner is only process lifetime, reference
expansion, assertion, and port-lease plumbing:
`glue/work/run-rimworld-headless-harness.ps1`.

The latest receipt was `status: passed`, `hostErrors: 0`, and
`portLease: acquired-and-released`; all five declared cases passed. The six
declarative artifacts (including the harness) also pass the bounded Rust JSON
validator. This is now the cheap preflight gate; the real-game Quicktest stays
the explicitly admitted final smoke.

A read-only transform-closure audit found 11 transform names across the 21
templates in `gluerimworld-csharp`: the shared source profile is missing only
`array-insert-at`; substituting the tested work-copy response-curve template
leaves zero missing transform names. That bounds the integration risk instead
of assuming the first error was the only one.

## Smallest declarative admission

The current template prepends the first point to the lower-point array with
`array-insert-at` at index zero. Express the same operation using already
registered transforms:

```json
{
  "type": "transform-resolver",
  "transform": { "type": "concat_array" },
  "resolve": [
    {
      "type": "transform-resolver",
      "transform": { "type": "array-append" },
      "resolve": {
        "type": "map-resolver",
        "resolve": {
          "array": { "type": "value-resolver", "value": [] },
          "element": { "type": "transform-resolver", "transform": { "type": "array-nth" } }
        }
      }
    },
    { "type": "target-resolver", "target": "_lowerPts" }
  ]
}
```

The real admitted version retains the existing `array-nth` map, including its
index `0` and `{x:0,y:0}` default. It is already tested in the isolated host
template copy. The shared Glue checkout was not changed during this
coordinated pass. Since the canonical upstream C# implementation is already
present, the next integration step is source/package reconciliation and a
direct probe of the original shared template—not admission of a permanent JSON
workaround.

## Acceptance order

1. Reconcile the publish checkout with upstream `d5d2aa042d` (or an equivalent
   admitted ancestor), verify the C# registration/test and the existing
   Rust/TypeScript contract, and run the original shared three-curve probe.
2. Rebuild the bounded host from that reconciled source and repeat the native Quicktest, auditing stdout
   and stderr together.
3. Keep the mod adapter restricted to observation, candidate projection,
   native JobGiver admission, receipt/readback, and lifecycle hooks. Any
   candidate without a real RimWorld native mapping remains fail-closed.
