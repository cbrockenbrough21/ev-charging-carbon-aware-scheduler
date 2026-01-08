# EV Charging Carbon-Aware API — Design

## 1) Purpose & Motivation
Electric vehicles have zero tailpipe emissions, but charging emissions depend on the grid. Grid carbon intensity varies hour-by-hour as the fuel mix changes (solar drops after sunset, gas ramps, wind varies, imports fluctuate).  
This project builds a backend API that recommends *when* to charge within a user-provided window to minimize estimated CO₂ emissions.

**Why this matters:** Many EV charging sessions are flexible (e.g., “any time overnight”), which creates an opportunity to shift charging to cleaner hours without reducing convenience.

## 2) Scope (MVP)
The MVP provides a deterministic recommendation for a single charging session using a static snapshot of real CAISO fuel-mix data.

### MVP capabilities
- Accept a charging request: energy needed (kWh), charging power (kW), and a time window (start/end)
- Load hourly grid fuel mix data for a region (initially CAISO)
- Compute hourly grid carbon intensity (kg CO₂ / kWh) from fuel mix and emissions factors
- Find the lowest-emissions feasible charging start time within the window
- Return a recommendation with estimated emissions and a short explanation

### Out of scope for MVP
- Live data ingestion / real-time updates
- Carbon forecasting
- Electricity price optimization
- User accounts / authentication
- Charger hardware integration (OCPP)
- Battery degradation modeling
- Multi-vehicle or fleet optimization

## 3) Data Source (MVP) & Assumptions

### Data source
The MVP uses a **GridStatus.io export of “CAISO Hourly Standardized Data”**, derived from public CAISO fuel-mix reporting.

Key columns used:
- `interval_start_utc` (timestamp)
- `fuel_mix.*` (average MW by source), for example:
  - `fuel_mix.natural_gas`
  - `fuel_mix.coal`
  - `fuel_mix.solar`, `fuel_mix.wind`
  - `fuel_mix.large_hydro`, `fuel_mix.small_hydro`
  - `fuel_mix.nuclear`, `fuel_mix.geothermal`
  - `fuel_mix.imports`, `fuel_mix.other`, `fuel_mix.batteries`

### Assumptions (MVP)
- Timestamps are interpreted in **UTC** (`interval_start_utc`) to avoid daylight savings ambiguity.
- Fuel mix values are reported in **megawatts (MW)** and represent average power over the interval.
- Hourly energy is derived as `MWh = MW × 1 hour`.
- Emissions factors are simplified and documented in `DATA_NOTES.md`.
- **Imports / other:** excluded from both numerator and denominator for MVP.
- **Batteries:** treated as zero-emissions (energy storage, not primary generation).

**Why a static snapshot for MVP:** a fixed dataset enables reproducible testing and demos, avoids rate limits or credentials, and keeps the focus on planning logic. The architecture supports live data providers later.

## 4) Architecture Overview
The system is split into three layers:

1. **Core (domain + algorithm)**  
   - Pure logic: domain models and the charging planner  
   - No I/O (no file reads, HTTP calls, or database access)

2. **Data providers (ingestion)**  
   - Produce a time-ordered series of `CarbonIntensityPoint`
   - Swappable via an interface so planner logic does not change when the data source changes

3. **API (transport)**  
   - ASP.NET Core endpoints that validate input and call Core logic

### Key interfaces
- `ChargingPlanner` consumes:
  - `ChargingRequest`
  - `IReadOnlyList<CarbonIntensityPoint>`
- Data providers implement:
  - `ICarbonIntensityProvider`

## 5) Data Provider Abstraction (Swappable Sources)
The core design decision is to isolate data sourcing behind an interface.

```csharp
public interface ICarbonIntensityProvider
{
    Task<IReadOnlyList<CarbonIntensityPoint>> GetHourlyAsync(
        string zone,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken ct = default);
}
```

### MVP implementation
- `CsvCarbonIntensityProvider`
- Reads a GridStatus CAISO CSV export
- Aggregates 5-minute data to hourly if needed
- Computes hourly carbon intensity and returns `CarbonIntensityPoint` values

### Why this abstraction exists
- Enables switching from CSV → API → database without changing planner logic
- Keeps the planner pure and easy to unit test
- Mirrors production backend design patterns

## 6) Carbon Intensity Computation

Carbon intensity is computed from hourly grid fuel-mix data rather than sourced
directly from a third-party carbon feed. This keeps assumptions explicit and
avoids reliance on proprietary black-box APIs.

### Input data
- Hourly fuel mix values are provided in **megawatts (MW)** by source.
- Each row represents a one-hour interval.
- Timestamps are interpreted in **UTC**.

### Emissions factors
Emissions factors are approximate and documented in `DATA_NOTES.md`.

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

### Per-hour calculation
For a single hour:

```text
energy_kwh = fuel_mw × 1 hour × 1000
```

```text
total_emissions_kg =
  Σ (energy_kwh[fuel] × emission_factor[fuel])
```

```text
total_generation_kwh =
  Σ (energy_kwh[all fuels])
```

```text
carbon_intensity_kg_per_kwh =
  total_emissions_kg / total_generation_kwh
```

Each hourly interval produces one `CarbonIntensityPoint`:
- `timestampUtc`
- `kgCo2PerKwh`

## 7) Charging Planner Algorithm

The planner determines the lowest-emissions feasible charging window within a
user-provided time range.

### Inputs
- Energy required: `kWhNeeded`
- Maximum charging power: `maxChargingKw`
- Allowed charging window: `windowStartUtc` → `windowEndUtc`
- Hourly carbon intensity series: `CarbonIntensityPoint[]`

### Derived values
Charging duration (hours):

```text
duration_hours = kWhNeeded / maxChargingKw
```

The planner operates at hourly resolution for the MVP and supports a partial
final hour if needed.

### Algorithm
1. Enumerate feasible candidate start times at hourly boundaries within the window.
2. For each candidate:
   - Compute energy delivered per hour.
   - Multiply by the corresponding hourly carbon intensity.
   - Sum total emissions across the duration.
3. Discard candidates that extend beyond available data or the allowed window.
4. Select the candidate with the **minimum total emissions**.
5. Return a recommendation with:
   - start time
   - end time
   - estimated total emissions
   - short explanation

### Notes
- The planner is deterministic.
- The planner performs no I/O and has no dependency on data ingestion logic.

## 8) API Outline (v1)

Base path: `/v1`

### POST `/v1/charging-sessions/recommendation`
Returns the lowest-emissions charging window within the provided constraints.

#### Request body
```json
{
  "zone": "CAISO",
  "windowStartUtc": "2026-01-05T06:00:00Z",
  "windowEndUtc": "2026-01-05T16:00:00Z",
  "kWhNeeded": 18.0,
  "maxChargingKw": 7.2
}
```

#### Response body
```json
{
  "zone": "CAISO",
  "recommendedStartUtc": "2026-01-05T10:00:00Z",
  "recommendedEndUtc": "2026-01-05T12:30:00Z",
  "estimatedEmissionsKg": 3.2,
  "assumptions": {
    "hourlyResolution": true,
    "emissionsFactors": "see DATA_NOTES.md"
  }
}
```

### GET `/v1/health`
Returns HTTP 200 if the service is running.

### Future endpoints (not in MVP)
- `GET /v1/zones`
- `GET /v1/carbon-intensity`
- Fleet or batch optimization endpoints

## 9) Testing Strategy

Testing focuses on validating the planner logic independently of data ingestion.

### Unit tests (core logic)
- Constant carbon intensity (any window equivalent)
- Single high-emissions spike (e.g., 7–9pm ramp)
- Infeasible windows (not enough time)
- Partial-hour charging durations
- Boundary conditions at window start/end

### Data provider tests
- CSV parsing correctness
- Timestamp handling (UTC)
- Carbon intensity calculation for a known row

### Test principles
- Planner tests use synthetic data for clarity.
- A small subset of real CSV rows may be used for integration tests.
- No tests require network access.
