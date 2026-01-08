# Data Notes & Assumptions

This document describes the data sources, assumptions, and simplifications
used in the MVP version of the Carbon-Aware EV Charging Scheduler.

The goal of the MVP is **clarity, reproducibility, and determinism**, not
perfect real-world carbon accounting.

---

## Data Source (MVP)

The MVP uses a **static CSV export** of CAISO fuel mix data generated via
GridStatus.io.

- Region: CAISO
- Temporal resolution: Hourly
- Timestamp field: `interval_start_utc`
- Units: Average power in megawatts (MW) per fuel source

The dataset is committed directly to the repository to enable:
- Reproducible builds and demos
- Offline development
- Deterministic testing

Future versions may replace this with live API ingestion or forecast-based
carbon intensity.

---

## Fuel Mix Interpretation

- Fuel mix values represent **average power (MW)** over a one-hour interval.
- Hourly energy is derived as:

```
energy_kwh = MW × 1 hour × 1000
```

- All timestamps are interpreted as **UTC** to avoid daylight savings ambiguity.

---

## Emissions Factors (Approximate)

The following emissions factors are used to estimate CO₂ emissions:

| Fuel Type    | Emissions Factor (kg CO₂ / kWh) |
|-------------|----------------------------------|
| Natural Gas | 0.40 |
| Coal        | 1.00 |
| Oil         | 0.80 |
| Solar       | 0.00 |
| Wind        | 0.00 |
| Hydro       | 0.00 |
| Nuclear     | 0.00 |
| Geothermal  | 0.00 |

These values are simplified and intentionally conservative. They are suitable
for **relative comparisons across charging windows**, not absolute lifecycle
emissions analysis.

---

## Included and Excluded Sources

### Included in emissions calculation
- Natural gas
- Coal
- Oil

### Included in total generation (zero emissions)
- Solar
- Wind
- Large hydro
- Small hydro
- Nuclear
- Geothermal

### Excluded (MVP simplification)
- Imports
- Other / unspecified sources

Imports are excluded to avoid making unverifiable assumptions about upstream
generation mixes outside CAISO.

---

## Battery Storage

- Battery discharge is treated as **zero-emissions** at the time of discharge.
- Upstream charging emissions are implicitly reflected in the grid mix.

This mirrors common grid carbon accounting practices while keeping the model
simple and explainable.

---

## Known Limitations

- No marginal emissions modeling
- No real-time data updates
- No carbon forecasting
- No electricity price optimization
- No regional sub-zonal differentiation

These limitations are intentional for the MVP.

---

## Intended Use

This data model is designed to:
- Compare candidate charging windows
- Explain *why* one charging window is cleaner than another
- Support deterministic planning logic

It is **not** intended to provide regulatory-grade or billing-grade emissions
estimates.
