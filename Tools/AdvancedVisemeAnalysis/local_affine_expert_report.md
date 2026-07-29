# Local-affine viseme expert experiment

This deterministic offline experiment fits 15 hard-routed affine experts with eight unit-range face-tracking inputs and 19 representative lower-face/tongue outputs. It does not modify an avatar, controller, clip, renderer, or mesh.

## Result

**PASS** - fitted max=8.993e-15; pruned replay p99=0.010993, max=0.024460; constraints=0; connected clips=10 steady/20 transitioning.

| Model | Static RMS | Static p99 | Static max | Replay RMS | Replay p99 | Replay max | Velocity p99/s |
|---|---:|---:|---:|---:|---:|---:|---:|
| fitted | 1.084e-15 | 4.274e-15 | 8.993e-15 | 7.727e-16 | 2.998e-15 | 4.802e-15 | 9.193e-14 |
| pruned | 0.008086 | 0.024840 | 0.052823 | 0.003213 | 0.010993 | 0.024460 | 0.606761 |

## Structural estimate

| Quantity | Value |
|---|---:|
| Dense merged reference, connected clips/frame | 2084 |
| Hard-routed steady state | 10 |
| During one state transition | 20 |
| Steady connected reduction | 99.52% |
| Transition connected reduction | 99.04% |
| Unpruned unique authored clips | 150 |
| Pruned unique authored clips (estimate) | 91 |

The compressor removed **66 / 120** viseme/input residual groups (55.0%).

> Common and retained residual coefficients must be fused into each active tracker clip. Evaluating them as separate shared/residual layers saves storage but can increase frame cost.

## Constraint proof over the complete input box

Random replay is not used as the proof. For each protected affine output, the script computes its exact minimum or maximum over `f in [0,1]^8` and `Voice in [0,1]`.

| Constraint | Required | Analytic extreme | Margin |
|---|---:|---:|---:|
| PP lip closure | >= 0.880 | 0.883105 | 3.105e-03 |
| PP mouth opening | <= 0.180 | 0.173384 | 6.616e-03 |
| FF labiodental bite | >= 0.720 | 0.720000 | 0.000e+00 |
| CH mouth opening | <= 0.340 | 0.306629 | 3.337e-02 |
| SS mouth opening | <= 0.340 | 0.305178 | 3.482e-02 |
| Exact shared jaw passthrough | coefficient error = 0 | 0.000e+00 | -0.000e+00 |

## Replay coverage

The held-out replay contains **30 traces / 38,568 frames**: random speech, every directed viseme pair, and one-frame/35 ms interruptions at 15, 30, 60, 90, and 144 FPS.

| Replay group | RMS | p99 | max | velocity p99/s |
|---|---:|---:|---:|---:|
| interruptions@15 | 0.003256 | 0.011333 | 0.019625 | 0.176578 |
| interruptions@30 | 0.003077 | 0.010556 | 0.019186 | 0.280343 |
| interruptions@60 | 0.003315 | 0.011726 | 0.020098 | 0.496072 |
| interruptions@90 | 0.003303 | 0.011576 | 0.020935 | 0.646020 |
| interruptions@144 | 0.003174 | 0.010631 | 0.022313 | 0.882905 |
| random@15 | 0.003211 | 0.010482 | 0.018056 | 0.169106 |
| random@30 | 0.003216 | 0.010614 | 0.019905 | 0.297181 |
| random@60 | 0.003185 | 0.010743 | 0.019210 | 0.487116 |
| random@90 | 0.003123 | 0.010129 | 0.022657 | 0.629173 |
| random@144 | 0.003209 | 0.011299 | 0.022205 | 0.845522 |
| transitions@15 | 0.003222 | 0.010908 | 0.020597 | 0.162580 |
| transitions@30 | 0.003225 | 0.011001 | 0.019606 | 0.268128 |
| transitions@60 | 0.003207 | 0.010960 | 0.021397 | 0.412055 |
| transitions@90 | 0.003207 | 0.010941 | 0.024460 | 0.544870 |
| transitions@144 | 0.003239 | 0.011217 | 0.023835 | 0.706626 |

## Interpretation

The affine fit itself should be numerically exact; all meaningful error above is from dropping small local residual groups. The large structural win comes from hard routing: only one state's ten fused coefficient clips are connected in steady speech. The common matrix decomposition is useful for detecting reusable/prunable groups, but should not be left as an additional live Animator layer.

This is a mathematical and structural gate, not yet an end-to-end Unity CPU measurement. The next test must confirm that VRCFury preserves the state machine and that Unity really disconnects inactive state motions after merging.

Generated with seed `20260718` and prune threshold `0.055`.
