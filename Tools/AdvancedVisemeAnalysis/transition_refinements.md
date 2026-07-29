# Transition refinement ladder (held-out)

| model | RMSE all | RMSE transition |
|---|---|---|
| baseline C[cur, age] (ships today) | 0.05751 | 0.06699 |
| explicit fitted pull (previous best) | 0.04977 | 0.05427 |
| free lookup table (reference) | 0.05017 | 0.05425 |
| 1. shrinkage (n0=5) | 0.04942 | 0.05371 |
| 2. FPCA on shrunk (K=3) | 0.04946 | 0.05373 |
| 3. ilr refit (eps=0.01, n0=10) | 0.05611 | 0.05920 |
