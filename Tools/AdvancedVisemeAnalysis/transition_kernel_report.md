# Advanced Viseme transition-kernel reduction experiment

Deterministic seed `730241`; 2,154 traces; 223,802 frames; FPS 15, 30, 60, 90, 144.

The reference is the generated four-group retention table evaluated as `cᵀ R f`, with the same piecewise-linear frame-time alpha lookup emitted by `AlphaFromDeltaTime` and the default 24 ms fast-viseme pole. Errors are absolute normalized-retention errors. The transient-silence freeze gate is intentionally excluded; it is an orthogonal selector that can wrap every realization identically.

## Aggregate error across random and adversarial streams

| Method | Group | RMS | p99 | Max |
|---|---:|---:|---:|---:|
| `isolated-k1` | Jaw | 0.047614 | 0.197902 | 0.425485 |
| `isolated-k1` | Lips | 0.052174 | 0.199812 | 0.462025 |
| `isolated-k1` | TongueTip | 0.037058 | 0.151183 | 0.331247 |
| `isolated-k1` | TongueBody | 0.043936 | 0.165130 | 0.335311 |
| `isolated-k2` | Jaw | 0.085598 | 0.317355 | 0.646402 |
| `isolated-k2` | Lips | 0.104439 | 0.348236 | 0.676015 |
| `isolated-k2` | TongueTip | 0.079051 | 0.274434 | 0.480822 |
| `isolated-k2` | TongueBody | 0.100521 | 0.318669 | 0.489772 |
| `isolated-k3` | Jaw | 0.119554 | 0.450480 | 1.001878 |
| `isolated-k3` | Lips | 0.150078 | 0.505159 | 1.044975 |
| `isolated-k3` | TongueTip | 0.114745 | 0.414513 | 0.747298 |
| `isolated-k3` | TongueBody | 0.148115 | 0.477763 | 0.837199 |
| `isolated-k4` | Jaw | 0.144196 | 0.600603 | 1.416031 |
| `isolated-k4` | Lips | 0.181376 | 0.665338 | 1.378153 |
| `isolated-k4` | TongueTip | 0.138980 | 0.569510 | 1.070731 |
| `isolated-k4` | TongueBody | 0.179072 | 0.645314 | 1.118183 |
| `volterra-k1` | Jaw | 0.047614 | 0.197902 | 0.425485 |
| `volterra-k1` | Lips | 0.052174 | 0.199812 | 0.462025 |
| `volterra-k1` | TongueTip | 0.037058 | 0.151183 | 0.331247 |
| `volterra-k1` | TongueBody | 0.043936 | 0.165130 | 0.335311 |
| `volterra-k2` | Jaw | 0.021078 | 0.090060 | 0.253428 |
| `volterra-k2` | Lips | 0.026643 | 0.103847 | 0.291652 |
| `volterra-k2` | TongueTip | 0.017803 | 0.071854 | 0.231908 |
| `volterra-k2` | TongueBody | 0.024245 | 0.089204 | 0.280552 |
| `volterra-k3` | Jaw | 0.012413 | 0.061353 | 0.275479 |
| `volterra-k3` | Lips | 0.015937 | 0.075917 | 0.261091 |
| `volterra-k3` | TongueTip | 0.011228 | 0.056230 | 0.206647 |
| `volterra-k3` | TongueBody | 0.014768 | 0.072576 | 0.209877 |
| `volterra-k4` | Jaw | 0.009209 | 0.044823 | 0.221877 |
| `volterra-k4` | Lips | 0.011605 | 0.056040 | 0.240448 |
| `volterra-k4` | TongueTip | 0.007797 | 0.036979 | 0.179886 |
| `volterra-k4` | TongueBody | 0.010558 | 0.049938 | 0.216743 |
| `exact-switched-recurrence` | Jaw | 2.71e-17 | 1.11e-16 | 2.78e-16 |
| `exact-switched-recurrence` | Lips | 3.42e-17 | 1.11e-16 | 3.33e-16 |
| `exact-switched-recurrence` | TongueTip | 2.66e-17 | 1.11e-16 | 2.22e-16 |
| `exact-switched-recurrence` | TongueBody | 3.41e-17 | 1.11e-16 | 2.78e-16 |
| `exact-commuted-projection` | Jaw | 2.82e-17 | 1.11e-16 | 3.33e-16 |
| `exact-commuted-projection` | Lips | 3.23e-17 | 1.11e-16 | 4.44e-16 |
| `exact-commuted-projection` | TongueTip | 2.76e-17 | 1.11e-16 | 3.33e-16 |
| `exact-commuted-projection` | TongueBody | 3.29e-17 | 1.11e-16 | 3.33e-16 |
| `decay-svd-r2` | Jaw | 0.273641 | 0.569163 | 0.569163 |
| `decay-svd-r2` | Lips | 0.288587 | 0.695092 | 0.695092 |
| `decay-svd-r2` | TongueTip | 0.262974 | 0.621908 | 0.628597 |
| `decay-svd-r2` | TongueBody | 0.265015 | 0.596122 | 0.609736 |
| `decay-svd-r4` | Jaw | 0.187237 | 0.514591 | 0.520302 |
| `decay-svd-r4` | Lips | 0.219902 | 0.496963 | 0.496963 |
| `decay-svd-r4` | TongueTip | 0.221510 | 0.630994 | 0.637859 |
| `decay-svd-r4` | TongueBody | 0.219488 | 0.446171 | 0.446171 |
| `decay-svd-r6` | Jaw | 0.112101 | 0.323129 | 0.323129 |
| `decay-svd-r6` | Lips | 0.139850 | 0.376230 | 0.376230 |
| `decay-svd-r6` | TongueTip | 0.160738 | 0.406892 | 0.406892 |
| `decay-svd-r6` | TongueBody | 0.181421 | 0.371103 | 0.371103 |
| `decay-svd-r8` | Jaw | 0.075956 | 0.221201 | 0.222371 |
| `decay-svd-r8` | Lips | 0.110353 | 0.295154 | 0.295154 |
| `decay-svd-r8` | TongueTip | 0.114251 | 0.279783 | 0.282137 |
| `decay-svd-r8` | TongueBody | 0.144145 | 0.375958 | 0.375958 |
| `decay-svd-r10` | Jaw | 0.052820 | 0.124279 | 0.124391 |
| `decay-svd-r10` | Lips | 0.069039 | 0.169946 | 0.175255 |
| `decay-svd-r10` | TongueTip | 0.068173 | 0.225010 | 0.226793 |
| `decay-svd-r10` | TongueBody | 0.102771 | 0.330395 | 0.330395 |
| `decay-svd-r12` | Jaw | 0.031431 | 0.108995 | 0.108995 |
| `decay-svd-r12` | Lips | 0.044380 | 0.165044 | 0.170834 |
| `decay-svd-r12` | TongueTip | 0.036484 | 0.134684 | 0.134684 |
| `decay-svd-r12` | TongueBody | 0.051058 | 0.155572 | 0.155572 |
| `decay-svd-r2-sparse01` | Jaw | 0.000357 | 0.001809 | 0.003925 |
| `decay-svd-r2-sparse01` | Lips | 0.000227 | 0.001141 | 0.004024 |
| `decay-svd-r2-sparse01` | TongueTip | 0.000363 | 0.001928 | 0.003813 |
| `decay-svd-r2-sparse01` | TongueBody | 0.000294 | 0.001593 | 0.004063 |
| `decay-svd-r4-sparse01` | Jaw | 0.001135 | 0.006186 | 0.006186 |
| `decay-svd-r4-sparse01` | Lips | 0.000290 | 0.001642 | 0.004238 |
| `decay-svd-r4-sparse01` | TongueTip | 0.000269 | 0.001447 | 0.003782 |
| `decay-svd-r4-sparse01` | TongueBody | 0.000407 | 0.002067 | 0.004307 |
| `decay-svd-r6-sparse01` | Jaw | 0.000831 | 0.003505 | 0.003892 |
| `decay-svd-r6-sparse01` | Lips | 0.000413 | 0.002167 | 0.004231 |
| `decay-svd-r6-sparse01` | TongueTip | 0.000428 | 0.001986 | 0.003486 |
| `decay-svd-r6-sparse01` | TongueBody | 0.000294 | 0.001556 | 0.003872 |
| `decay-svd-r8-sparse01` | Jaw | 0.001279 | 0.006434 | 0.006434 |
| `decay-svd-r8-sparse01` | Lips | 0.000673 | 0.002875 | 0.005086 |
| `decay-svd-r8-sparse01` | TongueTip | 0.000360 | 0.001855 | 0.003927 |
| `decay-svd-r8-sparse01` | TongueBody | 0.002065 | 0.009661 | 0.009661 |
| `decay-svd-r10-sparse01` | Jaw | 0.000524 | 0.002354 | 0.003883 |
| `decay-svd-r10-sparse01` | Lips | 0.001337 | 0.006127 | 0.006127 |
| `decay-svd-r10-sparse01` | TongueTip | 0.000658 | 0.002468 | 0.003765 |
| `decay-svd-r10-sparse01` | TongueBody | 0.001624 | 0.007815 | 0.007815 |
| `decay-svd-r12-sparse01` | Jaw | 0.001476 | 0.007056 | 0.007163 |
| `decay-svd-r12-sparse01` | Lips | 0.002241 | 0.009422 | 0.009422 |
| `decay-svd-r12-sparse01` | TongueTip | 0.002387 | 0.007510 | 0.007547 |
| `decay-svd-r12-sparse01` | TongueBody | 0.001117 | 0.005060 | 0.005287 |
| `trajectory-decay-r2` | Jaw | 0.079178 | 0.247372 | 0.478626 |
| `trajectory-decay-r2` | Lips | 0.087199 | 0.231374 | 0.467310 |
| `trajectory-decay-r2` | TongueTip | 0.076385 | 0.215375 | 0.338753 |
| `trajectory-decay-r2` | TongueBody | 0.080341 | 0.205815 | 0.352425 |
| `trajectory-decay-r4` | Jaw | 0.063866 | 0.212452 | 0.420367 |
| `trajectory-decay-r4` | Lips | 0.071825 | 0.214697 | 0.485012 |
| `trajectory-decay-r4` | TongueTip | 0.058035 | 0.180114 | 0.344833 |
| `trajectory-decay-r4` | TongueBody | 0.064133 | 0.192014 | 0.398120 |
| `trajectory-decay-r6` | Jaw | 0.052696 | 0.182579 | 0.359458 |
| `trajectory-decay-r6` | Lips | 0.054047 | 0.169747 | 0.391696 |
| `trajectory-decay-r6` | TongueTip | 0.049378 | 0.153392 | 0.312991 |
| `trajectory-decay-r6` | TongueBody | 0.056563 | 0.193444 | 0.399533 |
| `trajectory-decay-r8` | Jaw | 0.044499 | 0.160170 | 0.322973 |
| `trajectory-decay-r8` | Lips | 0.045752 | 0.149558 | 0.360511 |
| `trajectory-decay-r8` | TongueTip | 0.043127 | 0.146221 | 0.276684 |
| `trajectory-decay-r8` | TongueBody | 0.047574 | 0.158318 | 0.346074 |
| `trajectory-decay-r10` | Jaw | 0.038152 | 0.134823 | 0.307667 |
| `trajectory-decay-r10` | Lips | 0.040034 | 0.137947 | 0.357185 |
| `trajectory-decay-r10` | TongueTip | 0.036516 | 0.123617 | 0.220723 |
| `trajectory-decay-r10` | TongueBody | 0.043066 | 0.152708 | 0.338166 |
| `trajectory-decay-r12` | Jaw | 0.034720 | 0.122632 | 0.310420 |
| `trajectory-decay-r12` | Lips | 0.036483 | 0.125109 | 0.280332 |
| `trajectory-decay-r12` | TongueTip | 0.031725 | 0.107845 | 0.190923 |
| `trajectory-decay-r12` | TongueBody | 0.034573 | 0.116178 | 0.263912 |
| `trajectory-decay-r2-sparse01` | Jaw | 0.000490 | 0.001961 | 0.003819 |
| `trajectory-decay-r2-sparse01` | Lips | 0.000207 | 0.001117 | 0.003330 |
| `trajectory-decay-r2-sparse01` | TongueTip | 0.000295 | 0.001730 | 0.003709 |
| `trajectory-decay-r2-sparse01` | TongueBody | 0.000407 | 0.002077 | 0.003772 |
| `trajectory-decay-r4-sparse01` | Jaw | 0.002514 | 0.009726 | 0.009726 |
| `trajectory-decay-r4-sparse01` | Lips | 0.001360 | 0.007199 | 0.007199 |
| `trajectory-decay-r4-sparse01` | TongueTip | 0.000671 | 0.003825 | 0.003907 |
| `trajectory-decay-r4-sparse01` | TongueBody | 0.000359 | 0.001414 | 0.004129 |
| `trajectory-decay-r6-sparse01` | Jaw | 0.001073 | 0.005358 | 0.005358 |
| `trajectory-decay-r6-sparse01` | Lips | 0.001857 | 0.009155 | 0.009155 |
| `trajectory-decay-r6-sparse01` | TongueTip | 0.002074 | 0.009136 | 0.009136 |
| `trajectory-decay-r6-sparse01` | TongueBody | 0.001709 | 0.008432 | 0.008756 |
| `trajectory-decay-r8-sparse01` | Jaw | 0.001992 | 0.009124 | 0.009124 |
| `trajectory-decay-r8-sparse01` | Lips | 0.001258 | 0.004864 | 0.005401 |
| `trajectory-decay-r8-sparse01` | TongueTip | 0.001763 | 0.008493 | 0.008493 |
| `trajectory-decay-r8-sparse01` | TongueBody | 0.003258 | 0.008825 | 0.008825 |
| `trajectory-decay-r10-sparse01` | Jaw | 0.001931 | 0.007245 | 0.007245 |
| `trajectory-decay-r10-sparse01` | Lips | 0.002233 | 0.007954 | 0.007954 |
| `trajectory-decay-r10-sparse01` | TongueTip | 0.002209 | 0.008951 | 0.009090 |
| `trajectory-decay-r10-sparse01` | TongueBody | 0.000868 | 0.002745 | 0.004430 |
| `trajectory-decay-r12-sparse01` | Jaw | 0.002866 | 0.009758 | 0.009758 |
| `trajectory-decay-r12-sparse01` | Lips | 0.002617 | 0.009304 | 0.009661 |
| `trajectory-decay-r12-sparse01` | TongueTip | 0.002393 | 0.008192 | 0.008362 |
| `trajectory-decay-r12-sparse01` | TongueBody | 0.004000 | 0.009143 | 0.009292 |

## Random versus adversarial p99

| Method | Group | Random p99 | Adversarial p99 |
|---|---:|---:|---:|
| `isolated-k1` | Jaw | 0.169778 | 0.201349 |
| `isolated-k1` | Lips | 0.185204 | 0.202545 |
| `isolated-k1` | TongueTip | 0.139275 | 0.152848 |
| `isolated-k1` | TongueBody | 0.165998 | 0.165060 |
| `isolated-k2` | Jaw | 0.208685 | 0.327480 |
| `isolated-k2` | Lips | 0.246750 | 0.359828 |
| `isolated-k2` | TongueTip | 0.187821 | 0.280532 |
| `isolated-k2` | TongueBody | 0.214821 | 0.325670 |
| `isolated-k3` | Jaw | 0.243613 | 0.470465 |
| `isolated-k3` | Lips | 0.300679 | 0.517514 |
| `isolated-k3` | TongueTip | 0.228902 | 0.427175 |
| `isolated-k3` | TongueBody | 0.278211 | 0.490600 |
| `isolated-k4` | Jaw | 0.255627 | 0.631594 |
| `isolated-k4` | Lips | 0.318291 | 0.688937 |
| `isolated-k4` | TongueTip | 0.238792 | 0.588787 |
| `isolated-k4` | TongueBody | 0.298492 | 0.667254 |
| `volterra-k1` | Jaw | 0.169778 | 0.201349 |
| `volterra-k1` | Lips | 0.185204 | 0.202545 |
| `volterra-k1` | TongueTip | 0.139275 | 0.152848 |
| `volterra-k1` | TongueBody | 0.165998 | 0.165060 |
| `volterra-k2` | Jaw | 0.049935 | 0.094528 |
| `volterra-k2` | Lips | 0.063589 | 0.107913 |
| `volterra-k2` | TongueTip | 0.035294 | 0.076063 |
| `volterra-k2` | TongueBody | 0.050390 | 0.093442 |
| `volterra-k3` | Jaw | 0.016546 | 0.066499 |
| `volterra-k3` | Lips | 0.022859 | 0.080927 |
| `volterra-k3` | TongueTip | 0.010865 | 0.060535 |
| `volterra-k3` | TongueBody | 0.018541 | 0.077590 |
| `volterra-k4` | Jaw | 0.004360 | 0.049922 |
| `volterra-k4` | Lips | 0.008299 | 0.061145 |
| `volterra-k4` | TongueTip | 0.003046 | 0.040896 |
| `volterra-k4` | TongueBody | 0.006314 | 0.054615 |
| `exact-switched-recurrence` | Jaw | 1.11e-16 | 1.11e-16 |
| `exact-switched-recurrence` | Lips | 1.11e-16 | 1.11e-16 |
| `exact-switched-recurrence` | TongueTip | 1.11e-16 | 8.33e-17 |
| `exact-switched-recurrence` | TongueBody | 1.39e-16 | 1.11e-16 |
| `exact-commuted-projection` | Jaw | 1.39e-16 | 1.11e-16 |
| `exact-commuted-projection` | Lips | 1.67e-16 | 1.11e-16 |
| `exact-commuted-projection` | TongueTip | 1.11e-16 | 1.11e-16 |
| `exact-commuted-projection` | TongueBody | 1.67e-16 | 1.11e-16 |
| `decay-svd-r2` | Jaw | 0.539756 | 0.569163 |
| `decay-svd-r2` | Lips | 0.570979 | 0.695092 |
| `decay-svd-r2` | TongueTip | 0.590768 | 0.626042 |
| `decay-svd-r2` | TongueBody | 0.550739 | 0.602846 |
| `decay-svd-r4` | Jaw | 0.488897 | 0.518554 |
| `decay-svd-r4` | Lips | 0.467035 | 0.496963 |
| `decay-svd-r4` | TongueTip | 0.599990 | 0.635368 |
| `decay-svd-r4` | TongueBody | 0.415272 | 0.446171 |
| `decay-svd-r6` | Jaw | 0.292984 | 0.323129 |
| `decay-svd-r6` | Lips | 0.323836 | 0.376230 |
| `decay-svd-r6` | TongueTip | 0.393493 | 0.406892 |
| `decay-svd-r6` | TongueBody | 0.351577 | 0.371103 |
| `decay-svd-r8` | Jaw | 0.200305 | 0.221201 |
| `decay-svd-r8` | Lips | 0.270040 | 0.295154 |
| `decay-svd-r8` | TongueTip | 0.260907 | 0.282137 |
| `decay-svd-r8` | TongueBody | 0.350561 | 0.375958 |
| `decay-svd-r10` | Jaw | 0.121380 | 0.124391 |
| `decay-svd-r10` | Lips | 0.163475 | 0.172347 |
| `decay-svd-r10` | TongueTip | 0.209606 | 0.226793 |
| `decay-svd-r10` | TongueBody | 0.280605 | 0.330395 |
| `decay-svd-r12` | Jaw | 0.105201 | 0.108995 |
| `decay-svd-r12` | Lips | 0.140198 | 0.167512 |
| `decay-svd-r12` | TongueTip | 0.121697 | 0.134684 |
| `decay-svd-r12` | TongueBody | 0.143095 | 0.155572 |
| `decay-svd-r2-sparse01` | Jaw | 0.001751 | 0.001821 |
| `decay-svd-r2-sparse01` | Lips | 0.001361 | 0.001103 |
| `decay-svd-r2-sparse01` | TongueTip | 0.001984 | 0.001924 |
| `decay-svd-r2-sparse01` | TongueBody | 0.001782 | 0.001570 |
| `decay-svd-r4-sparse01` | Jaw | 0.005453 | 0.006186 |
| `decay-svd-r4-sparse01` | Lips | 0.002143 | 0.001528 |
| `decay-svd-r4-sparse01` | TongueTip | 0.001294 | 0.001473 |
| `decay-svd-r4-sparse01` | TongueBody | 0.002262 | 0.002026 |
| `decay-svd-r6-sparse01` | Jaw | 0.003249 | 0.003505 |
| `decay-svd-r6-sparse01` | Lips | 0.002442 | 0.002110 |
| `decay-svd-r6-sparse01` | TongueTip | 0.002169 | 0.001959 |
| `decay-svd-r6-sparse01` | TongueBody | 0.001869 | 0.001486 |
| `decay-svd-r8-sparse01` | Jaw | 0.005874 | 0.006434 |
| `decay-svd-r8-sparse01` | Lips | 0.002926 | 0.002866 |
| `decay-svd-r8-sparse01` | TongueTip | 0.001911 | 0.001841 |
| `decay-svd-r8-sparse01` | TongueBody | 0.008581 | 0.009661 |
| `decay-svd-r10-sparse01` | Jaw | 0.002508 | 0.002320 |
| `decay-svd-r10-sparse01` | Lips | 0.005688 | 0.006127 |
| `decay-svd-r10-sparse01` | TongueTip | 0.002454 | 0.002472 |
| `decay-svd-r10-sparse01` | TongueBody | 0.006919 | 0.007815 |
| `decay-svd-r12-sparse01` | Jaw | 0.006420 | 0.007056 |
| `decay-svd-r12-sparse01` | Lips | 0.008827 | 0.009422 |
| `decay-svd-r12-sparse01` | TongueTip | 0.007077 | 0.007510 |
| `decay-svd-r12-sparse01` | TongueBody | 0.004791 | 0.005060 |
| `trajectory-decay-r2` | Jaw | 0.262235 | 0.245280 |
| `trajectory-decay-r2` | Lips | 0.251946 | 0.226684 |
| `trajectory-decay-r2` | TongueTip | 0.222121 | 0.213450 |
| `trajectory-decay-r2` | TongueBody | 0.233138 | 0.200752 |
| `trajectory-decay-r4` | Jaw | 0.213121 | 0.212448 |
| `trajectory-decay-r4` | Lips | 0.229490 | 0.212471 |
| `trajectory-decay-r4` | TongueTip | 0.177655 | 0.180750 |
| `trajectory-decay-r4` | TongueBody | 0.195537 | 0.191331 |
| `trajectory-decay-r6` | Jaw | 0.175729 | 0.183921 |
| `trajectory-decay-r6` | Lips | 0.170602 | 0.169594 |
| `trajectory-decay-r6` | TongueTip | 0.158965 | 0.151829 |
| `trajectory-decay-r6` | TongueBody | 0.179662 | 0.194733 |
| `trajectory-decay-r8` | Jaw | 0.167291 | 0.158909 |
| `trajectory-decay-r8` | Lips | 0.148462 | 0.149775 |
| `trajectory-decay-r8` | TongueTip | 0.150319 | 0.145380 |
| `trajectory-decay-r8` | TongueBody | 0.145720 | 0.162292 |
| `trajectory-decay-r10` | Jaw | 0.130278 | 0.136011 |
| `trajectory-decay-r10` | Lips | 0.133584 | 0.138350 |
| `trajectory-decay-r10` | TongueTip | 0.131200 | 0.122720 |
| `trajectory-decay-r10` | TongueBody | 0.134050 | 0.158998 |
| `trajectory-decay-r12` | Jaw | 0.121288 | 0.122980 |
| `trajectory-decay-r12` | Lips | 0.123677 | 0.125522 |
| `trajectory-decay-r12` | TongueTip | 0.117291 | 0.106282 |
| `trajectory-decay-r12` | TongueBody | 0.108242 | 0.118307 |
| `trajectory-decay-r2-sparse01` | Jaw | 0.002222 | 0.001920 |
| `trajectory-decay-r2-sparse01` | Lips | 0.001227 | 0.001103 |
| `trajectory-decay-r2-sparse01` | TongueTip | 0.001556 | 0.001740 |
| `trajectory-decay-r2-sparse01` | TongueBody | 0.001665 | 0.002077 |
| `trajectory-decay-r4-sparse01` | Jaw | 0.009370 | 0.009726 |
| `trajectory-decay-r4-sparse01` | Lips | 0.006148 | 0.007199 |
| `trajectory-decay-r4-sparse01` | TongueTip | 0.003532 | 0.003859 |
| `trajectory-decay-r4-sparse01` | TongueBody | 0.001385 | 0.001414 |
| `trajectory-decay-r6-sparse01` | Jaw | 0.004827 | 0.005358 |
| `trajectory-decay-r6-sparse01` | Lips | 0.007971 | 0.009155 |
| `trajectory-decay-r6-sparse01` | TongueTip | 0.008637 | 0.009136 |
| `trajectory-decay-r6-sparse01` | TongueBody | 0.007567 | 0.008548 |
| `trajectory-decay-r8-sparse01` | Jaw | 0.008746 | 0.009124 |
| `trajectory-decay-r8-sparse01` | Lips | 0.004672 | 0.004864 |
| `trajectory-decay-r8-sparse01` | TongueTip | 0.007635 | 0.008493 |
| `trajectory-decay-r8-sparse01` | TongueBody | 0.008240 | 0.008825 |
| `trajectory-decay-r10-sparse01` | Jaw | 0.006393 | 0.007245 |
| `trajectory-decay-r10-sparse01` | Lips | 0.006924 | 0.007954 |
| `trajectory-decay-r10-sparse01` | TongueTip | 0.008173 | 0.009027 |
| `trajectory-decay-r10-sparse01` | TongueBody | 0.002672 | 0.002745 |
| `trajectory-decay-r12-sparse01` | Jaw | 0.008908 | 0.009758 |
| `trajectory-decay-r12-sparse01` | Lips | 0.008740 | 0.009432 |
| `trajectory-decay-r12-sparse01` | TongueTip | 0.007590 | 0.008260 |
| `trajectory-decay-r12-sparse01` | TongueBody | 0.008721 | 0.009292 |

## Structural runtime cost estimate

These are active-path curve/clip counts for the beta-retention block only, not measured milliseconds. They exclude shared frame-time lookup, upstream fast-viseme observation, downstream lead arithmetic, and VRCFury rewrites.

| Realization | Active curves | Active clips | Dynamic state | Relative curves |
|---|---:|---:|---:|---:|
| `dense-current` | 1005 | 188 | 90 floats | 100.0% |
| `exact-commuted-projection-direct` | 240 | 132 | 60 floats | 23.9% |
| `exact-commuted-projection-stage-preserving` | 360 | 196 | 120 floats | 35.8% |
| `exact-switched-recurrence` | 240 | 184 | 34 floats | 23.9% |
| `trajectory-decay-r2` | 48 | 36 | 12 floats | 4.8% |
| `trajectory-decay-r2-sparse01` | 914 | 902 | 12 floats + 866 residuals | 90.9% |
| `decay-svd-r2-sparse01` | 902 | 890 | 12 floats + 854 residuals | 89.8% |
| `trajectory-decay-r4` | 92 | 56 | 24 floats | 9.2% |
| `trajectory-decay-r4-sparse01` | 964 | 928 | 24 floats + 872 residuals | 95.9% |
| `decay-svd-r4-sparse01` | 935 | 899 | 24 floats + 843 residuals | 93.0% |
| `trajectory-decay-r6` | 136 | 76 | 36 floats | 13.5% |
| `trajectory-decay-r6-sparse01` | 989 | 929 | 36 floats + 853 residuals | 98.4% |
| `decay-svd-r6-sparse01` | 961 | 901 | 36 floats + 825 residuals | 95.6% |
| `trajectory-decay-r8` | 180 | 96 | 48 floats | 17.9% |
| `trajectory-decay-r8-sparse01` | 1029 | 945 | 48 floats + 849 residuals | 102.4% |
| `decay-svd-r8-sparse01` | 975 | 891 | 48 floats + 795 residuals | 97.0% |
| `trajectory-decay-r10` | 224 | 116 | 60 floats | 22.3% |
| `trajectory-decay-r10-sparse01` | 1062 | 954 | 60 floats + 838 residuals | 105.7% |
| `decay-svd-r10-sparse01` | 987 | 879 | 60 floats + 763 residuals | 98.2% |
| `trajectory-decay-r12` | 268 | 136 | 72 floats | 26.7% |
| `trajectory-decay-r12-sparse01` | 1093 | 961 | 72 floats + 825 residuals | 108.8% |
| `decay-svd-r12-sparse01` | 933 | 801 | 72 floats + 665 residuals | 92.8% |

## Animator-frame staging replay

All current context update, context projection, and destination contraction motions are siblings in one Direct state. Under normal Animator feedback semantics they read the parameters present at evaluation start: `c` updates for the next frame, `z` sees the previous `c`, and retention sees the previous `z` and `f`. A direct projected-state EMA removes the `c -> z` pipeline delay.

| Replacement | Group | RMS versus current staging | p99 | Max |
|---|---:|---:|---:|---:|
| `direct-commuted` | Jaw | 0.064677 | 0.326553 | 0.637193 |
| `direct-commuted` | Lips | 0.061106 | 0.290611 | 0.591964 |
| `direct-commuted` | TongueTip | 0.061729 | 0.302335 | 0.623738 |
| `direct-commuted` | TongueBody | 0.057340 | 0.283203 | 0.573685 |
| `copy-stage-preserving` | Jaw | 3.32e-17 | 1.11e-16 | 4.44e-16 |
| `copy-stage-preserving` | Lips | 3.70e-17 | 1.11e-16 | 5.55e-16 |
| `copy-stage-preserving` | TongueTip | 3.27e-17 | 1.11e-16 | 3.89e-16 |
| `copy-stage-preserving` | TongueBody | 3.78e-17 | 1.11e-16 | 3.89e-16 |

Therefore a strict replay-compatible replacement requires the extra projected-vector copy (or an experimentally verified equivalent layer boundary). The copy raises the estimate from 240 curves / 132 clips to 360 curves / 196 clips, still 64% fewer active curve bindings than the estimated current block. It has slightly more clip references, so the expected win specifically depends on the previously observed dense-curve sampling bottleneck and must be profiled.

## Exact identities tested

For hard symbol `v`, context decay `a`, and fast decay `d`:

```text
c' = a c + (1-a) e_v
f' = d f + (1-d) e_v
y' = ad y + a(1-d) cᵀR e_v + (1-a)d e_vᵀR f + (1-a)(1-d)R_vv
z' = a z + (1-a) R[v,:],  where z = cᵀR;  y' = z' f'
```

The first is an exact switched scalar recurrence. The second commutes the linear context observer through the matrix, so it updates one selected authored row instead of sampling the full dense tensor. Neither truncates history or changes the learned table.

## Event-kernel basis size

| K | Isolated exponentials | Volterra exponentials |
|---:|---:|---:|
| 1 | 5 | 5 |
| 2 | 10 | 14 |
| 3 | 15 | 27 |
| 4 | 20 | 44 |

These counts are only the shared exponential arithmetic basis. An Animator implementation must also store/shift K transition identities and select the corresponding table coefficients, so they are not comparable to the curve counts above without a concrete compiler.

## Sparse residual guarantee

For every trajectory-weighted decay-rank model, coefficients whose residual magnitude exceeds 0.01 are restored exactly. Since `c_i f_j >= 0` and `sum_ij c_i f_j = 1`, the remaining output error is a convex combination of coefficient residuals and is therefore universally at most 0.01 for any legal simplex states.

| Family | Rank | Corrections | Remaining coefficient max | Active curves | Active clips | Curves below dense? |
|---|---:|---:|---:|---:|---:|---:|
| trajectory | 2 | 866 | 0.009859 | 914 | 902 | yes |
| trajectory | 4 | 872 | 0.009726 | 964 | 928 | yes |
| trajectory | 6 | 853 | 0.009847 | 989 | 929 | yes |
| trajectory | 8 | 849 | 0.009855 | 1029 | 945 | no |
| trajectory | 10 | 838 | 0.009459 | 1062 | 954 | no |
| trajectory | 12 | 825 | 0.009849 | 1093 | 961 | no |
| coefficient SVD | 2 | 854 | 0.009976 | 902 | 890 | yes |
| coefficient SVD | 4 | 843 | 0.009979 | 935 | 899 | yes |
| coefficient SVD | 6 | 825 | 0.009892 | 961 | 901 | yes |
| coefficient SVD | 8 | 795 | 0.009981 | 975 | 891 | yes |
| coefficient SVD | 10 | 763 | 0.009970 | 987 | 879 | yes |
| coefficient SVD | 12 | 665 | 0.009949 | 933 | 801 | yes |

## Worst observed cases

| Method | Group | Error | FPS | Trace | Frame | Ref | Pred |
|---|---:|---:|---:|---|---:|---:|---:|
| `isolated-k1` | Jaw | 0.425485 | 144 | `random-9` | 1114 | 0.439976 | 0.014491 |
| `isolated-k1` | Lips | 0.462025 | 144 | `random-0` | 596 | 0.501161 | 0.039136 |
| `isolated-k1` | TongueTip | 0.331247 | 90 | `interrupt-U-aa-FF-h1-12` | 19 | 0.421958 | 0.090711 |
| `isolated-k1` | TongueBody | 0.335311 | 144 | `random-7` | 806 | 0.407875 | 0.072564 |
| `isolated-k2` | Jaw | 0.646402 | 90 | `interrupt-RR-O-kk-h1-62` | 20 | 0.298425 | 0.944827 |
| `isolated-k2` | Lips | 0.676015 | 60 | `alt-kk-nn-h1-4` | 15 | 0.330450 | 1.006465 |
| `isolated-k2` | TongueTip | 0.480822 | 144 | `interrupt-CH-kk-I-h1-75` | 125 | 0.250892 | 0.731714 |
| `isolated-k2` | TongueBody | 0.489772 | 60 | `alt-FF-sil-h1-11` | 15 | 0.250028 | 0.739800 |
| `isolated-k3` | Jaw | 1.001877 | 90 | `alt-kk-O-h1-43` | 25 | 0.397415 | 1.399293 |
| `isolated-k3` | Lips | 1.044975 | 60 | `alt-kk-nn-h1-4` | 17 | 0.408440 | 1.453415 |
| `isolated-k3` | TongueTip | 0.747298 | 144 | `alt-RR-E-h1-47` | 49 | 0.332934 | 1.080232 |
| `isolated-k3` | TongueBody | 0.837199 | 90 | `alt-U-I-h1-6` | 25 | 0.259846 | 1.097045 |
| `isolated-k4` | Jaw | 1.416031 | 90 | `alt-O-kk-h1-18` | 25 | 0.400333 | 1.816364 |
| `isolated-k4` | Lips | 1.378153 | 60 | `alt-kk-nn-h1-4` | 17 | 0.408440 | 1.786593 |
| `isolated-k4` | TongueTip | 1.070731 | 144 | `alt-E-RR-h1-38` | 49 | 0.273746 | 1.344477 |
| `isolated-k4` | TongueBody | 1.118183 | 144 | `alt-FF-sil-h1-1` | 49 | 0.366176 | 1.484360 |
| `volterra-k1` | Jaw | 0.425485 | 144 | `random-9` | 1114 | 0.439976 | 0.014491 |
| `volterra-k1` | Lips | 0.462025 | 144 | `random-0` | 596 | 0.501161 | 0.039136 |
| `volterra-k1` | TongueTip | 0.331247 | 90 | `interrupt-U-aa-FF-h1-12` | 19 | 0.421958 | 0.090711 |
| `volterra-k1` | TongueBody | 0.335311 | 144 | `random-7` | 806 | 0.407875 | 0.072564 |
| `volterra-k2` | Jaw | 0.253428 | 60 | `cycle-nn-U-FF-h1-4` | 14 | 0.145318 | 0.398747 |
| `volterra-k2` | Lips | 0.291652 | 144 | `alt-TH-kk-h1-16` | 49 | 0.404318 | 0.112667 |
| `volterra-k2` | TongueTip | 0.231908 | 144 | `alt-aa-kk-h1-42` | 41 | 0.332988 | 0.101080 |
| `volterra-k2` | TongueBody | 0.280552 | 144 | `alt-I-U-h1-7` | 49 | 0.388481 | 0.107929 |
| `volterra-k3` | Jaw | 0.275479 | 144 | `cycle-I-DD-sil-h1-25` | 31 | 0.441233 | 0.165754 |
| `volterra-k3` | Lips | 0.261091 | 144 | `cycle-O-RR-DD-h1-11` | 33 | 0.414762 | 0.153671 |
| `volterra-k3` | TongueTip | 0.206647 | 144 | `cycle-sil-FF-kk-h1-18` | 54 | 0.403520 | 0.196873 |
| `volterra-k3` | TongueBody | 0.209877 | 90 | `cycle-I-aa-FF-h1-2` | 21 | 0.424951 | 0.215074 |
| `volterra-k4` | Jaw | 0.221877 | 144 | `cycle-SS-PP-FF-h1-22` | 31 | 0.343715 | 0.121837 |
| `volterra-k4` | Lips | 0.240448 | 144 | `cycle-O-RR-DD-h1-11` | 34 | 0.435129 | 0.194682 |
| `volterra-k4` | TongueTip | 0.179886 | 144 | `alt-aa-kk-h1-42` | 41 | 0.332988 | 0.153102 |
| `volterra-k4` | TongueBody | 0.216743 | 144 | `alt-I-U-h1-7` | 49 | 0.388481 | 0.171738 |
| `exact-switched-recurrence` | Jaw | 2.78e-16 | 90 | `random-5` | 523 | 0.344520 | 0.344520 |
| `exact-switched-recurrence` | Lips | 3.33e-16 | 144 | `random-3` | 752 | 0.487926 | 0.487926 |
| `exact-switched-recurrence` | TongueTip | 2.22e-16 | 90 | `random-5` | 648 | 0.352036 | 0.352036 |
| `exact-switched-recurrence` | TongueBody | 2.78e-16 | 144 | `random-5` | 745 | 0.474924 | 0.474924 |
| `exact-commuted-projection` | Jaw | 3.33e-16 | 144 | `random-4` | 981 | 0.414531 | 0.414531 |
| `exact-commuted-projection` | Lips | 4.44e-16 | 144 | `random-8` | 619 | 0.477602 | 0.477602 |
| `exact-commuted-projection` | TongueTip | 3.33e-16 | 144 | `random-9` | 597 | 0.430852 | 0.430852 |
| `exact-commuted-projection` | TongueBody | 3.33e-16 | 144 | `random-3` | 1286 | 0.416077 | 0.416077 |
| `decay-svd-r2` | Jaw | 0.569163 | 15 | `random-4` | 0 | 0 | 0.569163 |
| `decay-svd-r2` | Lips | 0.695092 | 15 | `random-1` | 0 | 0 | 0.695092 |
| `decay-svd-r2` | TongueTip | 0.628597 | 15 | `random-6` | 0 | 0 | 0.628597 |
| `decay-svd-r2` | TongueBody | 0.609736 | 15 | `random-6` | 0 | 0 | 0.609736 |
| `decay-svd-r4` | Jaw | 0.520302 | 15 | `random-6` | 0 | 0 | 0.520302 |
| `decay-svd-r4` | Lips | 0.496963 | 15 | `random-5` | 0 | 0 | 0.496963 |
| `decay-svd-r4` | TongueTip | 0.637859 | 15 | `random-6` | 0 | 0 | 0.637859 |
| `decay-svd-r4` | TongueBody | 0.446171 | 15 | `random-2` | 0 | 0 | 0.446171 |
| `decay-svd-r6` | Jaw | 0.323129 | 15 | `alt-TH-O-h1-2` | 0 | 0 | 0.323129 |
| `decay-svd-r6` | Lips | 0.376230 | 15 | `random-3` | 0 | 0 | 0.376230 |
| `decay-svd-r6` | TongueTip | 0.406892 | 15 | `random-5` | 0 | 0 | 0.406892 |
| `decay-svd-r6` | TongueBody | 0.371103 | 15 | `alt-PP-RR-h1-9` | 0 | 0 | 0.371103 |
| `decay-svd-r8` | Jaw | 0.222371 | 60 | `alt-TH-CH-h7-27` | 97 | 0.014267 | 0.236639 |
| `decay-svd-r8` | Lips | 0.295154 | 15 | `random-3` | 0 | 0 | 0.295154 |
| `decay-svd-r8` | TongueTip | 0.282137 | 15 | `random-0` | 0 | 0 | 0.282137 |
| `decay-svd-r8` | TongueBody | 0.375958 | 15 | `alt-PP-RR-h1-9` | 0 | 0 | 0.375958 |
| `decay-svd-r10` | Jaw | 0.124391 | 15 | `random-0` | 0 | 0 | 0.124391 |
| `decay-svd-r10` | Lips | 0.175255 | 15 | `alt-CH-kk-h1-13` | 0 | 0 | 0.175255 |
| `decay-svd-r10` | TongueTip | 0.226793 | 15 | `random-0` | 0 | 0 | 0.226793 |
| `decay-svd-r10` | TongueBody | 0.330395 | 15 | `alt-PP-RR-h1-9` | 0 | 0 | 0.330395 |
| `decay-svd-r12` | Jaw | 0.108995 | 15 | `random-3` | 0 | 0 | 0.108995 |
| `decay-svd-r12` | Lips | 0.170834 | 15 | `alt-CH-kk-h1-13` | 0 | 0 | 0.170834 |
| `decay-svd-r12` | TongueTip | 0.134684 | 15 | `random-3` | 0 | 0 | 0.134684 |
| `decay-svd-r12` | TongueBody | 0.155572 | 15 | `alt-O-aa-h1-1` | 0 | 0 | 0.155572 |
| `decay-svd-r2-sparse01` | Jaw | 0.003925 | 144 | `alt-I-nn-h4-45` | 38 | 0.262729 | 0.266654 |
| `decay-svd-r2-sparse01` | Lips | 0.004024 | 144 | `random-9` | 17 | 0.070856 | 0.074880 |
| `decay-svd-r2-sparse01` | TongueTip | 0.003813 | 30 | `interrupt-kk-U-PP-h1-74` | 6 | 0.229018 | 0.232831 |
| `decay-svd-r2-sparse01` | TongueBody | 0.004063 | 144 | `interrupt-sil-CH-RR-h4-34` | 32 | 0.116566 | 0.120629 |
| `decay-svd-r4-sparse01` | Jaw | 0.006186 | 15 | `alt-FF-RR-h1-4` | 0 | 0 | 0.006186 |
| `decay-svd-r4-sparse01` | Lips | 0.004238 | 144 | `random-4` | 16 | 0.136009 | 0.140247 |
| `decay-svd-r4-sparse01` | TongueTip | 0.003782 | 90 | `random-3` | 270 | 0.155164 | 0.158946 |
| `decay-svd-r4-sparse01` | TongueBody | 0.004307 | 144 | `interrupt-sil-FF-kk-h4-18` | 32 | 0.341882 | 0.346189 |
| `decay-svd-r6-sparse01` | Jaw | 0.003892 | 144 | `alt-PP-U-h4-44` | 38 | 0.182915 | 0.179023 |
| `decay-svd-r6-sparse01` | Lips | 0.004231 | 144 | `interrupt-CH-aa-TH-h4-70` | 32 | 0.385336 | 0.381106 |
| `decay-svd-r6-sparse01` | TongueTip | 0.003486 | 90 | `interrupt-SS-FF-U-h2-40` | 19 | 0.276655 | 0.280141 |
| `decay-svd-r6-sparse01` | TongueBody | 0.003872 | 30 | `interrupt-DD-SS-O-h1-1` | 6 | 0.174892 | 0.171020 |
| `decay-svd-r8-sparse01` | Jaw | 0.006434 | 15 | `random-7` | 0 | 0 | 0.006434 |
| `decay-svd-r8-sparse01` | Lips | 0.005086 | 90 | `interrupt-kk-RR-E-h2-58` | 19 | 0.192422 | 0.197508 |
| `decay-svd-r8-sparse01` | TongueTip | 0.003927 | 144 | `interrupt-I-E-aa-h4-27` | 32 | 0.199288 | 0.195361 |
| `decay-svd-r8-sparse01` | TongueBody | 0.009661 | 15 | `alt-nn-E-h1-28` | 0 | 0 | 0.009661 |
| `decay-svd-r10-sparse01` | Jaw | 0.003883 | 144 | `alt-E-RR-h4-38` | 38 | 0.367846 | 0.371729 |
| `decay-svd-r10-sparse01` | Lips | 0.006127 | 15 | `random-5` | 0 | 0 | 0.006127 |
| `decay-svd-r10-sparse01` | TongueTip | 0.003765 | 90 | `alt-kk-O-h2-43` | 23 | 0.235277 | 0.239041 |
| `decay-svd-r10-sparse01` | TongueBody | 0.007815 | 15 | `random-4` | 0 | 0 | 0.007815 |
| `decay-svd-r12-sparse01` | Jaw | 0.007163 | 60 | `alt-FF-E-h7-46` | 96 | 0.016236 | 0.009072 |
| `decay-svd-r12-sparse01` | Lips | 0.009422 | 15 | `random-5` | 0 | 0 | 0.009422 |
| `decay-svd-r12-sparse01` | TongueTip | 0.007547 | 144 | `random-4` | 29 | 0.061732 | 0.069278 |
| `decay-svd-r12-sparse01` | TongueBody | 0.005287 | 144 | `alt-SS-aa-h17-27` | 47 | 0.165934 | 0.171221 |
| `trajectory-decay-r2` | Jaw | 0.478626 | 60 | `interrupt-I-DD-RR-h1-59` | 13 | 0.553308 | 0.074683 |
| `trajectory-decay-r2` | Lips | 0.467310 | 90 | `random-6` | 386 | 0.587143 | 0.119833 |
| `trajectory-decay-r2` | TongueTip | 0.338753 | 90 | `random-0` | 79 | 0.402883 | 0.064130 |
| `trajectory-decay-r2` | TongueBody | 0.352425 | 60 | `interrupt-FF-U-I-h1-65` | 48 | 0.503058 | 0.150632 |
| `trajectory-decay-r4` | Jaw | 0.420367 | 60 | `interrupt-I-DD-RR-h2-59` | 14 | 0.526377 | 0.106010 |
| `trajectory-decay-r4` | Lips | 0.485012 | 90 | `random-6` | 386 | 0.587143 | 0.102131 |
| `trajectory-decay-r4` | TongueTip | 0.344833 | 90 | `alt-nn-I-h2-2` | 23 | 0.309340 | -0.035493 |
| `trajectory-decay-r4` | TongueBody | 0.398120 | 60 | `interrupt-PP-FF-U-h2-21` | 14 | 0.457455 | 0.059335 |
| `trajectory-decay-r6` | Jaw | 0.359458 | 144 | `random-0` | 26 | 0.418144 | 0.058686 |
| `trajectory-decay-r6` | Lips | 0.391696 | 60 | `interrupt-DD-RR-PP-h2-69` | 13 | 0.432236 | 0.040541 |
| `trajectory-decay-r6` | TongueTip | 0.312991 | 90 | `alt-nn-I-h2-2` | 23 | 0.309340 | -0.003651 |
| `trajectory-decay-r6` | TongueBody | 0.399533 | 60 | `interrupt-PP-FF-U-h2-21` | 14 | 0.457455 | 0.057921 |
| `trajectory-decay-r8` | Jaw | 0.322973 | 30 | `random-3` | 1 | 0.486653 | 0.163680 |
| `trajectory-decay-r8` | Lips | 0.360511 | 60 | `interrupt-DD-RR-PP-h2-69` | 13 | 0.432236 | 0.071725 |
| `trajectory-decay-r8` | TongueTip | 0.276684 | 90 | `alt-nn-I-h2-2` | 23 | 0.309340 | 0.032655 |
| `trajectory-decay-r8` | TongueBody | 0.346074 | 144 | `alt-PP-FF-h4-18` | 38 | 0.394905 | 0.048831 |
| `trajectory-decay-r10` | Jaw | 0.307667 | 30 | `random-3` | 1 | 0.486653 | 0.178986 |
| `trajectory-decay-r10` | Lips | 0.357185 | 60 | `interrupt-DD-RR-PP-h2-69` | 13 | 0.432236 | 0.075052 |
| `trajectory-decay-r10` | TongueTip | 0.220723 | 144 | `interrupt-U-nn-TH-h4-8` | 32 | 0.224622 | 0.445345 |
| `trajectory-decay-r10` | TongueBody | 0.338166 | 144 | `alt-PP-FF-h4-18` | 38 | 0.394905 | 0.056739 |
| `trajectory-decay-r12` | Jaw | 0.310420 | 30 | `random-3` | 1 | 0.486653 | 0.176233 |
| `trajectory-decay-r12` | Lips | 0.280332 | 144 | `random-0` | 20 | 0.333925 | 0.053592 |
| `trajectory-decay-r12` | TongueTip | 0.190923 | 90 | `alt-RR-aa-h3-7` | 24 | 0.303734 | 0.112811 |
| `trajectory-decay-r12` | TongueBody | 0.263912 | 60 | `interrupt-PP-FF-U-h2-21` | 13 | 0.388954 | 0.125042 |
| `trajectory-decay-r2-sparse01` | Jaw | 0.003819 | 144 | `random-0` | 554 | 0.046453 | 0.050272 |
| `trajectory-decay-r2-sparse01` | Lips | 0.003330 | 30 | `alt-U-O-h1-29` | 7 | 0.083917 | 0.080586 |
| `trajectory-decay-r2-sparse01` | TongueTip | 0.003709 | 144 | `random-6` | 722 | 0.287248 | 0.283539 |
| `trajectory-decay-r2-sparse01` | TongueBody | 0.003772 | 144 | `interrupt-O-CH-PP-h4-35` | 32 | 0.097464 | 0.093692 |
| `trajectory-decay-r4-sparse01` | Jaw | 0.009726 | 15 | `random-5` | 0 | 0 | -0.009726 |
| `trajectory-decay-r4-sparse01` | Lips | 0.007199 | 15 | `alt-PP-RR-h1-9` | 0 | 0 | 0.007199 |
| `trajectory-decay-r4-sparse01` | TongueTip | 0.003907 | 15 | `alt-kk-FF-h1-10` | 0 | 0 | -0.003907 |
| `trajectory-decay-r4-sparse01` | TongueBody | 0.004129 | 144 | `alt-U-TH-h4-37` | 38 | 0.155633 | 0.159763 |
| `trajectory-decay-r6-sparse01` | Jaw | 0.005358 | 15 | `random-4` | 0 | 0 | 0.005358 |
| `trajectory-decay-r6-sparse01` | Lips | 0.009155 | 15 | `random-4` | 0 | 0 | 0.009155 |
| `trajectory-decay-r6-sparse01` | TongueTip | 0.009136 | 15 | `alt-O-aa-h1-1` | 0 | 0 | 0.009136 |
| `trajectory-decay-r6-sparse01` | TongueBody | 0.008756 | 15 | `alt-kk-FF-h1-10` | 0 | 0 | -0.008756 |
| `trajectory-decay-r8-sparse01` | Jaw | 0.009124 | 15 | `random-5` | 0 | 0 | 0.009124 |
| `trajectory-decay-r8-sparse01` | Lips | 0.005401 | 30 | `alt-sil-U-h2-13` | 8 | 0.177842 | 0.172441 |
| `trajectory-decay-r8-sparse01` | TongueTip | 0.008493 | 15 | `alt-TH-O-h1-2` | 0 | 0 | 0.008493 |
| `trajectory-decay-r8-sparse01` | TongueBody | 0.008825 | 15 | `random-5` | 0 | 0 | 0.008825 |
| `trajectory-decay-r10-sparse01` | Jaw | 0.007245 | 15 | `alt-FF-RR-h1-4` | 0 | 0 | 0.007245 |
| `trajectory-decay-r10-sparse01` | Lips | 0.007954 | 15 | `random-4` | 0 | 0 | 0.007954 |
| `trajectory-decay-r10-sparse01` | TongueTip | 0.009090 | 15 | `alt-CH-kk-h1-13` | 0 | 0 | 0.009090 |
| `trajectory-decay-r10-sparse01` | TongueBody | 0.004430 | 144 | `alt-aa-sil-h4-0` | 37 | 0.316815 | 0.312385 |
| `trajectory-decay-r12-sparse01` | Jaw | 0.009758 | 15 | `random-7` | 0 | 0 | 0.009758 |
| `trajectory-decay-r12-sparse01` | Lips | 0.009661 | 15 | `alt-kk-FF-h1-10` | 0 | 0 | -0.009661 |
| `trajectory-decay-r12-sparse01` | TongueTip | 0.008362 | 15 | `alt-kk-FF-h1-10` | 0 | 0 | 0.008362 |
| `trajectory-decay-r12-sparse01` | TongueBody | 0.009292 | 15 | `random-0` | 0 | 0 | 0.009292 |

## Interpretation guardrails

- The exact rewrites should be implemented and profiled before accepting an approximation; they target the dense-curve bottleneck without behavioral error.
- A hard-viseme Simple1D selector is assumed to sample one threshold child. Unity/VRChat profiling must verify zero-weight branch pruning and the real cost after VRCFury flattening.
- Low-rank models need a perceptual acceptance threshold and avatar corpus validation; normalized retention error is not itself a mesh-space error.
- Animator float precision, transient-silence hold, and intra-frame parameter write ordering require a generated-controller equivalence test before shipping.
