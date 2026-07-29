# Transition crossfade against the (prev, current, age) floor

- No-prev baseline (winner + age only): RMSE 0.05751 all, 0.06699 transition
- Lookup-table floor (prev + current + age): RMSE 0.05133 all, 0.05602 transition

## Best crossfade

- **linear**, duration **0.0 ms**, outgoing state frozen
- RMSE 0.05751 all, 0.06699 transition
- Closes **0.0%** of the baseline-to-floor gap

## Duration sweep (frozen outgoing state)

| profile | duration ms | RMSE all | RMSE transition |
|---|---|---|---|
| linear | 0.0 | 0.05751 | 0.06699 |
| linear | 21.3 | 0.06903 | 0.08502 |
| linear | 42.7 | 0.07109 | 0.08815 |
| linear | 64.0 | 0.07557 | 0.09489 |
| linear | 72.0 | 0.07732 | 0.09750 |
| linear | 85.3 | 0.08052 | 0.10226 |
| linear | 106.7 | 0.08535 | 0.10937 |
| linear | 128.0 | 0.08973 | 0.11560 |
| linear | 149.3 | 0.09366 | 0.12068 |
| linear | 170.7 | 0.09715 | 0.12481 |
| linear | 192.0 | 0.10024 | 0.12816 |
| linear | 213.3 | 0.10300 | 0.13094 |
| linear | 256.0 | 0.10771 | 0.13525 |
| linear | 320.0 | 0.11333 | 0.13972 |
| smoothstep | 0.0 | 0.05751 | 0.06699 |
| smoothstep | 21.3 | 0.06903 | 0.08502 |
| smoothstep | 42.7 | 0.07110 | 0.08816 |
| smoothstep | 64.0 | 0.07698 | 0.09700 |
| smoothstep | 72.0 | 0.07926 | 0.10039 |
| smoothstep | 85.3 | 0.08296 | 0.10586 |
| smoothstep | 106.7 | 0.08864 | 0.11418 |
| smoothstep | 128.0 | 0.09377 | 0.12164 |
| smoothstep | 149.3 | 0.09833 | 0.12796 |
| smoothstep | 170.7 | 0.10237 | 0.13312 |
| smoothstep | 192.0 | 0.10591 | 0.13721 |
| smoothstep | 213.3 | 0.10904 | 0.14048 |
| smoothstep | 256.0 | 0.11430 | 0.14523 |
| smoothstep | 320.0 | 0.12037 | 0.14960 |
| quintic | 0.0 | 0.05751 | 0.06699 |
| quintic | 21.3 | 0.06903 | 0.08502 |
| quintic | 42.7 | 0.07110 | 0.08816 |
| quintic | 64.0 | 0.07821 | 0.09882 |
| quintic | 72.0 | 0.08067 | 0.10248 |
| quintic | 85.3 | 0.08462 | 0.10830 |
| quintic | 106.7 | 0.09063 | 0.11707 |
| quintic | 128.0 | 0.09604 | 0.12494 |
| quintic | 149.3 | 0.10084 | 0.13174 |
| quintic | 170.7 | 0.10508 | 0.13733 |
| quintic | 192.0 | 0.10878 | 0.14171 |
| quintic | 213.3 | 0.11202 | 0.14510 |
| quintic | 256.0 | 0.11741 | 0.14976 |
| quintic | 320.0 | 0.12350 | 0.15360 |
