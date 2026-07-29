# Advanced Viseme reconstruction limits

Corpus: 124159 frames (2648.7 s) at 46.875 Hz (21.33 ms per frame).

## B. Identifiability from switch times

- Winner switches: 18109
- Switch rate: **6.84 Hz**
- Dwell: mean 142.0 ms, median 85.3 ms (p10 21.3, p90 213.3)
- Dwell excluding silence: mean 89.6 ms, median 85.3 ms
- Trajectory bandwidth: f50 1.46 Hz, f90 4.39 Hz, f95 5.86 Hz, f99 10.99 Hz
- Nyquist rate at f95: 11.72 Hz -> density ratio **0.58x**
- Nyquist rate at f99: 21.97 Hz -> density ratio **0.31x**

Verdict (95% energy): switch times are TOO SPARSE to determine the full trajectory.

Identifiable band: the 6.84 Hz switch rate supports reconstruction to **3.42 Hz**, which carries **86.2%** of the trajectory energy. The remaining 13.8% is faster than the token stream can express and needs real tracking.

## G. Conditional-mean error floor (held-out)

Transition frames = age < 107 ms (14805 of 27818).

| conditioning statistic | RMSE all | R2 all | RMSE transition | RMSE steady |
|---|---|---|---|---|
| global mean (no information) | 0.16081 | -0.0026 | 0.16207 | 0.15936 |
| current winner only (today's static halo row) | 0.06000 | 0.8604 | 0.07074 | 0.04475 |
| winner + time since switch | 0.05751 | 0.8718 | 0.06699 | 0.04431 |
| previous + current winner (retention pair) | 0.05512 | 0.8822 | 0.06182 | 0.04633 |
| previous + current + age (HSMM statistic) | 0.05133 | 0.8979 | 0.05602 | 0.04540 |

Headroom over today's static per-winner row: **26.8% MSE overall**, **37.3% MSE in transitions**.
