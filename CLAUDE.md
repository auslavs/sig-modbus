# SigEnStor Modbus TCP Reader

F# script that reads live data from a Sigenergy SigEnStor battery ESS via Modbus TCP and displays it in the console.

## Running

```
dotnet fsi battery.fsx
```

## Configuration

Edit `battery.fsx` line 28 to set the SigEnStor IP address. The device must have Modbus TCP Server enabled in the MySigen app (port 502).

## Architecture

- **NModbus** for Modbus TCP communication
- **FsToolkit.ErrorHandling** with `taskResult` CE for Railway Oriented Programming
- **Spectre.Console** for formatted console output with tables, progress bars, and color
- 2-space indentation throughout

### Slave IDs

- **247** — Plant-level registers (overall SOC, grid power, ESS power, load, etc.)
- **1** — Inverter-level registers (cell voltage/temp, grid frequency, PV strings)

### Key Registers

| Address | Name | Slave | Scale |
|---------|------|-------|-------|
| 30003 | EMS Work Mode | 247 | enum |
| 30005 | Grid Active Power | 247 | /1000 kW |
| 30014 | ESS SOC | 247 | /10 % |
| 30035 | PV Power | 247 | /1000 kW |
| 30037 | ESS Power | 247 | /1000 kW |
| 30083 | ESS Rated Capacity | 247 | /100 kWh |
| 30087 | ESS SOH | 247 | /10 % |
| 30284 | Total Load Power | 247 | /1000 kW |
| 30601 | Battery SOC | 1 | /10 % |
| 30603 | Avg Cell Temp | 1 | /10 C |
| 30604 | Avg Cell Voltage | 1 | /1000 V |
| 31002 | Grid Frequency | 1 | /100 Hz |

### Inferred Solar Production

The Enphase solar system feeds AC into the house, bypassing the SigEnStor's PV inputs. Solar production is inferred from the energy balance:

```
Solar = Total Load Power + ESS Power - Grid Active Power
```

### Display Layout

- **Row 1** (3-across): System Status | Grid | Solar (Enphase inferred)
- **Row 2** (full width): Battery with SOC/SOH progress bars

### ROP Pattern

Every I/O operation (TCP connect, Modbus register read) returns `Task<Result<'T, string>>`. The `taskResult` CE chains operations and short-circuits on first error. The top-level `match` handles Ok (print report) vs Error (print error and exit 1).
