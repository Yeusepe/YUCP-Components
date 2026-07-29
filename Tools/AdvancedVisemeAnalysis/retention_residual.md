# Rank-1 retention residual against the floor

- Baseline C[cur, age]:            RMSE 0.05751 all, 0.06699 transition
- **C[cur, age] + d(age)*Delta:    RMSE 0.04977 all, 0.05427 transition**
- Lookup-table floor:              RMSE 0.05133 all, 0.05602 transition

Gap closed: **123.4% overall**, **114.3% in transitions**.
Active ordered pairs: 194 of 225; largest Delta component 0.4361.

## Fitted decay d(age)

| age ms | d |
|---|---|
| 0.0 | 1.1605 |
| 21.3 | 0.8161 |
| 42.7 | 0.5547 |
| 64.0 | 0.3695 |
| 85.3 | 0.2463 |
| 106.7 | 0.1585 |
| 128.0 | 0.1022 |
| 149.3 | 0.0688 |
| 170.7 | 0.0577 |
| 192.0 | 0.0382 |
| 213.3 | 0.0321 |
| 234.7 | -0.0538 |
