# AVR AlwaysOne binder audit

Controller: `Assets\__YUCP_AVR_Profile_CompactHoldBaselineAAP.controller`

The Math child `Folded constants by __YUCP_AVR_ONE` contains **313** active parameter curves: 291 zero baselines and 22 non-zero affine offsets.

A conservative same-epoch proof finds **65 curves removable exactly** (61 zero, 4 non-zero) and **248 curves that must remain** in the current topology.

## By semantic group

| Group | Binder curves | Exact removal | Retain | Non-zero removed/retained |
|---|---:|---:|---:|---:|
| articulation-fusion-and-render | 47 | 4 | 43 | 0/0 |
| beta-coarticulation | 5 | 0 | 5 | 0/0 |
| constraints-evidence-and-velocity | 47 | 4 | 43 | 0/3 |
| misc-control | 27 | 0 | 27 | 0/6 |
| phone-posterior-and-corpus | 41 | 12 | 29 | 0/4 |
| timing-and-alpha | 2 | 0 | 2 | 0/0 |
| tongue-inference | 95 | 29 | 66 | 4/5 |
| tracking-decode-and-observer | 38 | 16 | 22 | 0/0 |
| voice-presence-and-hangover | 11 | 0 | 11 | 0/0 |

## Why the exact removal is safe

For a target `y = c + dynamic`, a zero `c` curve is redundant when another AlwaysOne Math subtree authors `y` on every possible path. For non-zero `c`, add `c` to that subtree's existing 1D leaves (or its guaranteed AlwaysOne child). The same parameter is written in the same Math evaluation and every reader still observes the same previous-frame AAP value.

The removable parameters currently feed 86 reader sites across 73 Math children. None of those readers or their thresholds/weights needs to change.

## Why the rest cannot be shifted away

The remaining 248 parameters have no other total writer. If their conditional producer becomes inactive, an unbound AAP may retain its previous value; an Animator default is not a per-frame zero write. Merely changing coordinates moves the baseline but does not provide that missing write.

They feed 297 reader sites across 231 Math children. Reader classes: 1d=109 parameters, direct=106 parameters, direct-and-1d=20 parameters, no-math-readers=13 parameters.

In particular, parameters used as Direct weights cannot generally be shifted around zero: the shifted coordinate may be negative, which is not a usable Direct blend weight. The audit therefore does not claim savings from signed weights, correlated gates, or moving consumers across the Math AAP epoch.

## Non-zero offsets that can be absorbed

| Parameter | Baseline | Total writer cover | Reader sites |
|---|---:|---|---:|
| `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Fast/LowerHeadroom` | 1 | #564 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Fast | 1 |
| `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Fast/UpperHeadroom` | 1 | #564 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Fast | 1 |
| `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Slow/LowerHeadroom` | 1 | #571 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Slow | 1 |
| `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Slow/UpperHeadroom` | 1 | #571 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Slow | 1 |

## Exact-removal parameter list

| Group | Parameter | Baseline | Cover child | Readers |
|---|---|---:|---|---:|
| articulation-fusion-and-render | `YUCP/AdvancedViseme/Articulation/SmileSad` | 0 | #622 Vector map YUCP/AdvancedViseme/_Internal/Articulation/SmileSad/FusedSlow | 1 |
| articulation-fusion-and-render | `YUCP/AdvancedViseme/Articulation/TongueArchY` | 0 | #627 Vector map YUCP/AdvancedViseme/_Internal/Articulation/TongueArchY/RenderedSpeech | 2 |
| articulation-fusion-and-render | `YUCP/AdvancedViseme/Articulation/TongueShape` | 0 | #628 Vector map YUCP/AdvancedViseme/_Internal/Articulation/TongueShape/RenderedSpeech | 1 |
| articulation-fusion-and-render | `YUCP/AdvancedViseme/Articulation/TongueY` | 0 | #625 Vector map YUCP/AdvancedViseme/_Internal/Articulation/TongueY/RenderedSpeech | 2 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueArchY/Fast` | 0 | #446 Hidden phone rank-one tongue articulation correction signed row 6 | 1 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueArchY/Slow` | 0 | #447 Hidden phone rank-one tongue articulation correction signed row 7 | 1 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueOut/Fast` | 0 | #440 Hidden phone rank-one tongue articulation correction signed row 0 | 2 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueOut/Slow` | 0 | #441 Hidden phone rank-one tongue articulation correction signed row 1 | 2 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueRoll/Fast` | 0 | #444 Hidden phone rank-one tongue articulation correction signed row 4 | 1 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueRoll/Slow` | 0 | #445 Hidden phone rank-one tongue articulation correction signed row 5 | 1 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueShape/Fast` | 0 | #448 Hidden phone rank-one tongue articulation correction signed row 8 | 1 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueShape/Slow` | 0 | #449 Hidden phone rank-one tongue articulation correction signed row 9 | 1 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Fast` | 0 | #442 Hidden phone rank-one tongue articulation correction signed row 2 | 2 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Slow` | 0 | #443 Hidden phone rank-one tongue articulation correction signed row 3 | 2 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Model/Logit` | 0 | #428 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Model/NormalizedLogit | 1 |
| phone-posterior-and-corpus | `YUCP/AdvancedViseme/_Internal/PhonePosterior/Model/NormalizedLogit` | 0 | #310 Vector map YUCP/AdvancedViseme/_Internal/TongueInference/Feature/JawOpen/Clamped | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Feature/JawOpen/CurrentMinusFast` | 0 | #310 Vector map YUCP/AdvancedViseme/_Internal/TongueInference/Feature/JawOpen/Clamped | 2 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Feature/JawOpen/FastMinusSlow` | 0 | #311 Vector map YUCP/AdvancedViseme/_Internal/TongueInference/Feature/JawOpen/Fast | 2 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Feature/LipAperture/CurrentMinusFast` | 0 | #328 Vector map YUCP/AdvancedViseme/_Internal/TongueInference/Feature/LipAperture/Clamped | 2 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Feature/LipAperture/FastMinusSlow` | 0 | #329 Vector map YUCP/AdvancedViseme/_Internal/TongueInference/Feature/LipAperture/Fast | 2 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Feature/LipProtrusion/CurrentMinusFast` | 0 | #346 Vector map YUCP/AdvancedViseme/_Internal/TongueInference/Feature/LipProtrusion/Clamped | 2 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Feature/LipProtrusion/FastMinusSlow` | 0 | #347 Vector map YUCP/AdvancedViseme/_Internal/TongueInference/Feature/LipProtrusion/Fast | 2 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/Reliability` | 0 | #519 Tongue inference viseme contraction rank-one correction | 2 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueOut/ContractedBase` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueOut/MixUnit/0` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueOut/MixUnit/1` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueOut/MixUnit/2` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueOut/MixUnit/3` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueOut/Normalized` | 0 | #528 Tongue inference contracted output sum signed row 0 | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueY/ContractedBase` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueY/MixUnit/0` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueY/MixUnit/1` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueY/MixUnit/2` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueY/MixUnit/3` | 0 | #519 Tongue inference viseme contraction rank-one correction | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueY/Normalized` | 0 | #529 Tongue inference contracted output sum signed row 1 | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/Visible/0` | 0 | #495 Tongue inference visible latent contraction signed row 0 | 3 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/Visible/1` | 0 | #495 Tongue inference visible latent contraction signed row 0 | 3 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/Visible/2` | 0 | #495 Tongue inference visible latent contraction signed row 0 | 3 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/Model/Visible/3` | 0 | #495 Tongue inference visible latent contraction signed row 0 | 3 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Fast/LowerHeadroom` | 1 | #564 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Fast | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Fast/TargetRaw` | 0 | #564 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Fast | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Fast/UpperHeadroom` | 1 | #564 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Fast | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Slow/LowerHeadroom` | 1 | #571 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Slow | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Slow/TargetRaw` | 0 | #571 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Slow | 1 |
| tongue-inference | `YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Slow/UpperHeadroom` | 1 | #571 Vector map YUCP/AdvancedViseme/_Internal/PhonePosterior/Articulation/TongueY/Slow | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/JawOpen/MotionDifference` | 0 | #152 Vector map YUCP/AdvancedViseme/_Internal/Tracking/JawOpen/FastCalibrated | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/JawOpen/PriorDifference` | 0 | #232 Vector map YUCP/AdvancedViseme/_Internal/Tracking/JawOpen/Pose | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/LipClose/MotionDifference` | 0 | #162 Vector map YUCP/AdvancedViseme/_Internal/Tracking/LipClose/FastCalibrated | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/LipClose/PriorDifference` | 0 | #237 Vector map YUCP/AdvancedViseme/_Internal/Tracking/LipClose/Pose | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/LipFunnel/MotionDifference` | 0 | #180 Vector map YUCP/AdvancedViseme/_Internal/Tracking/LipFunnel/FastCalibrated | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/LipFunnel/PriorDifference` | 0 | #245 Vector map YUCP/AdvancedViseme/_Internal/Tracking/LipFunnel/Pose | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/LipPucker/MotionDifference` | 0 | #189 Vector map YUCP/AdvancedViseme/_Internal/Tracking/LipPucker/FastCalibrated | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/LipPucker/PriorDifference` | 0 | #249 Vector map YUCP/AdvancedViseme/_Internal/Tracking/LipPucker/Pose | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/LipSuck/MotionDifference` | 0 | #198 Vector map YUCP/AdvancedViseme/_Internal/Tracking/LipSuck/FastCalibrated | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/LipSuck/PriorDifference` | 0 | #253 Vector map YUCP/AdvancedViseme/_Internal/Tracking/LipSuck/Pose | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/MouthOpen/MotionDifference` | 0 | #171 Vector map YUCP/AdvancedViseme/_Internal/Tracking/MouthOpen/FastCalibrated | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/MouthOpen/PriorDifference` | 0 | #241 Vector map YUCP/AdvancedViseme/_Internal/Tracking/MouthOpen/Pose | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/SmileSad/MotionDifference` | 0 | #207 Vector map YUCP/AdvancedViseme/_Internal/Tracking/SmileSad/FastCalibrated | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/SmileSad/PriorDifference` | 0 | #257 Vector map YUCP/AdvancedViseme/_Internal/Tracking/SmileSad/Pose | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/TongueOut/MotionDifference` | 0 | #216 Vector map YUCP/AdvancedViseme/_Internal/Tracking/TongueOut/FastCalibrated | 1 |
| tracking-decode-and-observer | `YUCP/AdvancedViseme/_Internal/Tracking/TongueOut/PriorDifference` | 0 | #261 Vector map YUCP/AdvancedViseme/_Internal/Tracking/TongueOut/Pose | 1 |
| constraints-evidence-and-velocity | `YUCP/AdvancedViseme/_Internal/Velocity/SmileSad/Raw` | 0 | #641 Vector map YUCP/AdvancedViseme/_Internal/Articulation/SmileSad/FusedFast | 1 |
| constraints-evidence-and-velocity | `YUCP/AdvancedViseme/_Internal/Velocity/TongueArchY/Raw` | 0 | #651 Vector map YUCP/AdvancedViseme/_Internal/Articulation/TongueArchY/TunedSpeechFast | 1 |
| constraints-evidence-and-velocity | `YUCP/AdvancedViseme/_Internal/Velocity/TongueShape/Raw` | 0 | #653 Vector map YUCP/AdvancedViseme/_Internal/Articulation/TongueShape/TunedSpeechFast | 1 |
| constraints-evidence-and-velocity | `YUCP/AdvancedViseme/_Internal/Velocity/TongueY/Raw` | 0 | #647 Vector map YUCP/AdvancedViseme/_Internal/Articulation/TongueY/TunedSpeechFast | 1 |

The JSON artifact contains every retained parameter, producer, reader, and proof result.
