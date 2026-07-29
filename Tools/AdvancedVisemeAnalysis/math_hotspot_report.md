# YUCP AVR Math active-path hotspot audit

Controller: `Assets\__YUCP_AVR_Profile_CompactHoldBaselineAAP.controller`

Deterministic synchronous AAP replay: 1,695 frames at 15, 30, 60, 90, 144 FPS. Mean Math work was **1178.57 active clip references** and **2137.38 active curve bindings** per frame (p95 1528/2594).

## Scenarios

| Scenario | Frames | Clips/frame | Curves/frame |
|---|---:|---:|---:|
| realistic-tracking | 678 | 1182.96 | 2136.49 |
| realistic-no-tracking | 678 | 1045.89 | 1975.44 |
| randomized-stress | 339 | 1435.15 | 2463.04 |

## Semantic output groups

| Group | Clips/frame | Curves/frame | Curve share |
|---|---:|---:|---:|
| tongue-inference | 267.84 | 543.96 | 25.4% |
| tracking-decode-and-observer | 236.18 | 355.92 | 16.7% |
| beta-coarticulation | 244.64 | 348.47 | 16.3% |
| articulation-fusion-and-render | 146.03 | 246.38 | 11.5% |
| phone-posterior-and-corpus | 115.19 | 175.05 | 8.2% |
| constraints-evidence-and-velocity | 99.24 | 147.33 | 6.9% |
| viseme-observer-and-render | 30.22 | 112.80 | 5.3% |
| misc-control | 78.76 | 107.72 | 5.0% |
| timing-and-alpha | 17.96 | 56.81 | 2.7% |
| voice-presence-and-hangover | 27.89 | 42.95 | 2.0% |

## Largest generated families

| Family | Members | Clips/frame | Curves/frame | Curve share |
|---|---:|---:|---:|---:|
| Folded constants by __YUCP_AVR_ONE | 1 | 1.00 | 313.00 | 14.6% |
| Compact transient-silence hold | 1 | 165.21 | 238.72 | 11.2% |
| Vector blend by YUCP/AdvancedViseme/_Internal/TongueInference/Observer/Alpha | 1 | 49.97 | 75.97 | 3.6% |
| Beta retention: context-to-projected copies | 60 | 59.09 | 59.09 | 2.8% |
| Viseme and projected-articulation slow observer | 1 | 19.61 | 55.45 | 2.6% |
| Frame-rate-correct alpha vector | 1 | 1.99 | 47.79 | 2.2% |
| Speech-liveliness articulation vector | 1 | 24.19 | 47.19 | 2.2% |
| Vector blend by YUCP/AdvancedViseme/_Internal/Alpha/TrackingMotion | 1 | 12.49 | 42.49 | 2.0% |
| Speech-liveliness viseme render vector | 1 | 9.28 | 36.11 | 1.7% |
| Tongue inference: contracted output row sums | 14 | 28.00 | 36.00 | 1.7% |
| Corpus projection/contraction | 7 | 25.04 | 35.24 | 1.6% |
| BetaCoarticulation Mean slow | 1 | 9.08 | 33.61 | 1.6% |
| BetaCoarticulation TongueTip observed slow | 1 | 9.08 | 33.61 | 1.6% |
| Tracking observer slow vector | 1 | 11.52 | 25.46 | 1.2% |
| Vector product by YUCP/AdvancedViseme/_Internal/Voice/Gain | 1 | 25.35 | 25.35 | 1.2% |
| Tracking observer fast vector | 1 | 10.97 | 24.91 | 1.2% |
| Tongue inference viseme contraction rank-one correction | 1 | 2.00 | 22.00 | 1.0% |
| Beta retention: projected-by-fast contractions | 15 | 13.78 | 13.78 | 0.6% |
| Vector blend by IsLocal | 1 | 4.91 | 12.91 | 0.6% |
| Beta coarticulation one-minus retention vector | 1 | 4.04 | 11.09 | 0.5% |
| Vector blend by YUCP/AdvancedViseme/_Internal/Tracking/SmileSad/BaseGain | 1 | 7.51 | 9.01 | 0.4% |
| Vector blend by YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Confidence | 1 | 7.22 | 8.66 | 0.4% |
| Vector select by IsLocal | 1 | 1.00 | 8.00 | 0.4% |
| Vector blend by YUCP/AdvancedViseme/_Internal/PhonePosterior/Observer/Alpha | 1 | 6.00 | 8.00 | 0.4% |
| Tongue inference visible latent contraction signed row 0 | 1 | 2.00 | 8.00 | 0.4% |

## Largest individual Math-root children

| # | Child | Active | Clips/frame | Curves/frame | Intra-Math reads |
|---:|---|---:|---:|---:|---:|
| 2 | Folded constants by __YUCP_AVR_ONE | 100.0% | 1.00 | 313.00 | 0 |
| 17 | Compact transient-silence hold | 100.0% | 165.21 | 238.72 | 121 |
| 309 | Vector blend by YUCP/AdvancedViseme/_Internal/TongueInference/Observer/Alpha | 100.0% | 49.97 | 75.97 | 22 |
| 19 | Viseme and projected-articulation slow observer | 100.0% | 19.61 | 55.45 | 39 |
| 3 | Frame-rate-correct alpha vector | 100.0% | 1.99 | 47.79 | 1 |
| 585 | Speech-liveliness articulation vector | 100.0% | 24.19 | 47.19 | 25 |
| 157 | Vector blend by YUCP/AdvancedViseme/_Internal/Alpha/TrackingMotion | 100.0% | 12.49 | 42.49 | 33 |
| 135 | Speech-liveliness viseme render vector | 100.0% | 9.28 | 36.11 | 31 |
| 96 | BetaCoarticulation Mean slow | 100.0% | 9.08 | 33.61 | 1 |
| 97 | BetaCoarticulation TongueTip observed slow | 100.0% | 9.08 | 33.61 | 1 |
| 133 | Tracking observer slow vector | 100.0% | 11.52 | 25.46 | 17 |
| 100 | Vector product by YUCP/AdvancedViseme/_Internal/Voice/Gain | 75.5% | 25.35 | 25.35 | 29 |
| 132 | Tracking observer fast vector | 100.0% | 10.97 | 24.91 | 17 |
| 519 | Tongue inference viseme contraction rank-one correction | 100.0% | 2.00 | 22.00 | 1 |
| 144 | Corpus Lips sparse contracted slow | 100.0% | 5.77 | 14.22 | 1 |
| 103 | Vector blend by IsLocal | 100.0% | 4.91 | 12.91 | 18 |
| 95 | Beta coarticulation one-minus retention vector | 100.0% | 4.04 | 11.09 | 4 |
| 592 | Vector blend by YUCP/AdvancedViseme/_Internal/Tracking/SmileSad/BaseGain | 100.0% | 7.51 | 9.01 | 5 |
| 570 | Vector blend by YUCP/AdvancedViseme/_Internal/TongueInference/TongueY/Confidence | 100.0% | 7.22 | 8.66 | 5 |
| 236 | Vector select by IsLocal | 100.0% | 1.00 | 8.00 | 8 |
| 430 | Vector blend by YUCP/AdvancedViseme/_Internal/PhonePosterior/Observer/Alpha | 100.0% | 6.00 | 8.00 | 4 |
| 495 | Tongue inference visible latent contraction signed row 0 | 100.0% | 2.00 | 8.00 | 1 |
| 496 | Tongue inference visible latent contraction signed row 1 | 100.0% | 2.00 | 8.00 | 1 |
| 497 | Tongue inference visible latent contraction signed row 2 | 100.0% | 2.00 | 8.00 | 1 |
| 498 | Tongue inference visible latent contraction signed row 3 | 100.0% | 2.00 | 8.00 | 1 |
| 499 | Tongue inference visible latent contraction signed row 4 | 100.0% | 2.00 | 8.00 | 1 |
| 500 | Tongue inference visible latent contraction signed row 5 | 100.0% | 2.00 | 8.00 | 1 |
| 501 | Tongue inference visible latent contraction signed row 6 | 100.0% | 2.00 | 8.00 | 1 |
| 502 | Tongue inference visible latent contraction signed row 7 | 100.0% | 2.00 | 8.00 | 1 |
| 503 | Tongue inference visible latent contraction signed row 8 | 100.0% | 2.00 | 8.00 | 1 |
| 549 | Vector map YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueOut/Stable | 100.0% | 1.95 | 7.79 | 1 |
| 563 | Vector map YUCP/AdvancedViseme/_Internal/TongueInference/Model/TongueY/Stable | 100.0% | 1.95 | 7.79 | 1 |
| 212 | Vector blend by YUCP/AdvancedViseme/_Internal/Tracking/SmileSad/Motion | 100.0% | 6.47 | 7.77 | 4 |
| 9 | Vector blend by YUCP/AdvancedViseme/_Internal/Alpha/Viseme/Tuned | 100.0% | 5.01 | 7.00 | 4 |
| 504 | Tongue inference viseme contraction row 0 | 56.4% | 0.56 | 6.20 | 1 |

## Interpretation

- A clip reference is counted only when every weight on its evaluated path is positive.
- A curve binding is one populated float curve in that active leaf clip.
- Every Math child is a sibling in one Direct tree. An `intra-Math read` therefore observes the previous frame's value, not a sibling's current write.
- This report ranks structural sampling work. It does not convert a curve or clip count into milliseconds.
- Semantic clip counts count a leaf once for every semantic group it binds; therefore group clip counts are not additive. Curve counts are additive.
