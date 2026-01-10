# EV Charging Carbon-Aware Scheduler

A backend API that recommends optimal charging times for electric vehicles to minimize carbon emissions based on grid electricity mix data.

## Overview

Electric vehicles produce zero tailpipe emissions, but their charging emissions depend on the electricity grid's fuel mix, which varies hour by hour. This project provides an API that analyzes grid carbon intensity and recommends when to charge within a user-provided time window to minimize CO₂ emissions.

## Features (MVP)

- **Carbon-aware charging recommendations**: Find the cleanest charging window based on grid fuel mix
- **Flexible scheduling**: Specify your charging needs and available time window
- **CAISO grid data**: Built on real California Independent System Operator fuel mix data
- **Deterministic planning**: Reproducible results using historical grid data
- **RESTful API**: Simple HTTP endpoints for integration

## Project Structure

```
EvCharging/
├── src/
│   ├── EvCharging.API/          # ASP.NET Core Web API
│   │   └── Controllers/         # REST endpoints
│   ├── EvCharging.Core/         # Domain logic & planning algorithm
│   │   ├── Domain/              # Domain models
│   │   ├── Planner/             # Charging optimization logic
│   │   └── Providers/           # Data provider interfaces
│   ├── EvCharging.Data/         # Data ingestion & processing
│   │   └── Data/                # CSV datasets (CAISO fuel mix)
│   └── EvCharging.Tests/        # Unit tests (xUnit)
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A code editor (Visual Studio, VS Code, or Rider)

## Getting Started

### Clone the repository

```bash
git clone https://github.com/cbrockenbrough21/ev-charging-carbon-aware-scheduler.git
cd ev-charging-carbon-aware-scheduler/EvCharging
```

### Build the solution

```bash
dotnet build
```

### Run tests

```bash
dotnet test
```

### Run the API

```bash
cd src/EvCharging.API
dotnet run
```

The API will be available at `https://localhost:5001` (or the port shown in your console).

## API Endpoints

### Health Check
```http
GET /v1/health
```

Returns HTTP 200 if the service is running.

### Get Charging Recommendation
```http
POST /v1/charging-sessions/recommendation
```

**Request body:**
```json
{
  "zone": "CAISO",
  "windowStartUtc": "2025-12-15T06:00:00Z",
  "windowEndUtc": "2025-12-15T16:00:00Z",
  "kWhNeeded": 18.0,
  "maxChargingKw": 7.2
}
```

**Response:** _(Coming soon - currently returns 501 Not Implemented)_
```json
{
  "zone": "CAISO",
  "recommendedStartUtc": "2025-12-15T10:00:00Z",
  "recommendedEndUtc": "2025-12-15T12:30:00Z",
  "estimatedEmissionsKg": 3.2,
  "assumptions": {
    "hourlyResolution": true,
    "emissionsFactors": "see DATA_NOTES.md"
  }
}
```

## How It Works

1. **Load Grid Data**: The system reads hourly fuel mix data for CAISO (California grid)
2. **Calculate Carbon Intensity**: Converts fuel mix to kg CO₂ per kWh using emissions factors
3. **Optimize Schedule**: Finds the charging window with lowest total emissions
4. **Return Recommendation**: Provides start time, end time, and estimated emissions

**Tie-breaking**: If multiple time windows have identical emissions, the earliest available time is chosen.

### Emissions Factors

| Fuel Type   | kg CO₂ / kWh |
|-------------|--------------|
| Natural Gas | 0.40         |
| Coal        | 1.00         |
| Oil         | 0.80         |
| Solar       | 0.00         |
| Wind        | 0.00         |
| Hydro       | 0.00         |
| Nuclear     | 0.00         |
| Geothermal  | 0.00         |

See [DATA_NOTES.md](../DATA_NOTES.md) for detailed assumptions and limitations.

## Data Source

The MVP uses static CSV exports of CAISO fuel mix data from [GridStatus.io](https://gridstatus.io/), providing:
- Hourly grid fuel mix by source (MW)
- UTC timestamps
- Real California grid data from December 2025

This static dataset enables reproducible testing and offline development. Future versions may integrate live APIs.

## Architecture

The project follows clean architecture principles:

- **Core**: Pure domain logic with no external dependencies
- **Data**: Swappable data providers (CSV, API, database)
- **API**: HTTP transport layer

Key interfaces:
- `ICarbonIntensityProvider`: Abstracts data source
- `ChargingPlanner`: Core optimization algorithm

## Development Status

🚧 **MVP Phase - Active Development**

- [x] Project structure and plumbing
- [x] API endpoints (skeleton)
- [x] Test infrastructure
- [ ] Domain models (CarbonIntensityPoint, ChargingRequest)
- [ ] CSV data provider implementation
- [ ] Charging planner algorithm
- [ ] End-to-end integration

## Contributing

This is a learning/portfolio project. Suggestions and improvements are welcome!

## Acknowledgments

- Grid data sourced from [GridStatus.io](https://gridstatus.io/)
- CAISO (California Independent System Operator) for fuel mix reporting

---

**Questions or feedback?** Open an issue or reach out via GitHub.
