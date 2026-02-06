#r "nuget: NModbus"
#r "nuget: FsToolkit.ErrorHandling"
#r "nuget: Spectre.Console"

open System
open System.Net.Sockets
open System.Threading.Tasks
open NModbus
open FsToolkit.ErrorHandling
open Spectre.Console
open Spectre.Console.Rendering

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

[<Literal>]
let PlantSlaveId = 247uy

[<Literal>]
let InverterSlaveId = 1uy

type Config =
  { Host: string
    Port: int }

let config =
  { Host = "YOUR_IP_HERE" // <-- Replace with your SigEnStor IP
    Port = 502 }

// ---------------------------------------------------------------------------
// Domain types
// ---------------------------------------------------------------------------

type PlantData =
  { EmsWorkMode: uint16
    GridActivePower: float
    OnOffGridStatus: uint16
    EssSoc: float
    PlantActivePower: float
    PvPower: float
    EssPower: float
    PlantRunningState: uint16
    EssRatedCapacity: float
    ChargeCutOffSoc: float
    DischargeCutOffSoc: float
    EssSoh: float
    TotalLoadPower: float }

type InverterData =
  { BatterySoc: float
    AvgCellTemperature: float
    AvgCellVoltage: float
    GridFrequency: float
    InverterTemperature: float
    Pv1Voltage: float
    Pv1Current: float
    Pv2Voltage: float
    Pv2Current: float }

type BatteryReport =
  { Plant: PlantData
    Inverter: InverterData }

// ---------------------------------------------------------------------------
// Modbus helpers — each wraps exceptions into Result Error
// ---------------------------------------------------------------------------

let createConnection (cfg: Config) : Task<Result<TcpClient * IModbusMaster, string>> =
  task {
    try
      let client = new TcpClient()
      do! client.ConnectAsync(cfg.Host, cfg.Port)
      let factory = new ModbusFactory()
      let master = factory.CreateMaster(client)
      master.Transport.ReadTimeout <- 5000
      master.Transport.WriteTimeout <- 5000
      return Ok(client, master)
    with ex ->
      return Error $"Connection failed: {ex.Message}"
  }

let readU16 (master: IModbusMaster) (slaveId: byte) (address: uint16) : Task<Result<uint16, string>> =
  task {
    try
      let! regs = master.ReadHoldingRegistersAsync(slaveId, address, 1us)
      return Ok regs[0]
    with ex ->
      return Error $"Read U16 @ {address} failed: {ex.Message}"
  }

let readS16 (master: IModbusMaster) (slaveId: byte) (address: uint16) : Task<Result<int16, string>> =
  task {
    try
      let! regs = master.ReadHoldingRegistersAsync(slaveId, address, 1us)
      return Ok(int16 regs[0])
    with ex ->
      return Error $"Read S16 @ {address} failed: {ex.Message}"
  }

let readU32 (master: IModbusMaster) (slaveId: byte) (address: uint16) : Task<Result<uint32, string>> =
  task {
    try
      let! regs = master.ReadHoldingRegistersAsync(slaveId, address, 2us)
      let value = (uint32 regs[0] <<< 16) ||| uint32 regs[1]
      return Ok value
    with ex ->
      return Error $"Read U32 @ {address} failed: {ex.Message}"
  }

let readS32 (master: IModbusMaster) (slaveId: byte) (address: uint16) : Task<Result<int32, string>> =
  task {
    try
      let! regs = master.ReadHoldingRegistersAsync(slaveId, address, 2us)
      let value = (int32 regs[0] <<< 16) ||| int32 regs[1]
      return Ok value
    with ex ->
      return Error $"Read S32 @ {address} failed: {ex.Message}"
  }

// ---------------------------------------------------------------------------
// Read pipelines
// ---------------------------------------------------------------------------

let readPlantData (master: IModbusMaster) : Task<Result<PlantData, string>> =
  taskResult {
    let sid = PlantSlaveId
    let! emsWorkMode = readU16 master sid 30003us
    let! gridActivePower = readS32 master sid 30005us
    let! onOffGridStatus = readU16 master sid 30009us
    let! essSoc = readU16 master sid 30014us
    let! plantActivePower = readS32 master sid 30031us
    let! pvPower = readS32 master sid 30035us
    let! essPower = readS32 master sid 30037us
    let! plantRunningState = readU16 master sid 30051us
    let! essRatedCapacity = readU32 master sid 30083us
    let! chargeCutOffSoc = readU16 master sid 30085us
    let! dischargeCutOffSoc = readU16 master sid 30086us
    let! essSoh = readU16 master sid 30087us
    let! totalLoadPower = readS32 master sid 30284us

    return
      { EmsWorkMode = emsWorkMode
        GridActivePower = float gridActivePower / 1000.0
        OnOffGridStatus = onOffGridStatus
        EssSoc = float essSoc / 10.0
        PlantActivePower = float plantActivePower / 1000.0
        PvPower = float pvPower / 1000.0
        EssPower = float essPower / 1000.0
        PlantRunningState = plantRunningState
        EssRatedCapacity = float essRatedCapacity / 100.0
        ChargeCutOffSoc = float chargeCutOffSoc / 10.0
        DischargeCutOffSoc = float dischargeCutOffSoc / 10.0
        EssSoh = float essSoh / 10.0
        TotalLoadPower = float totalLoadPower / 1000.0 }
  }

let readInverterData (master: IModbusMaster) : Task<Result<InverterData, string>> =
  taskResult {
    let sid = InverterSlaveId
    let! batterySoc = readU16 master sid 30601us
    let! avgCellTemp = readS16 master sid 30603us
    let! avgCellVoltage = readU16 master sid 30604us
    let! gridFrequency = readU16 master sid 31002us
    let! inverterTemp = readS16 master sid 31003us
    let! pv1Voltage = readS16 master sid 31027us
    let! pv1Current = readS16 master sid 31028us
    let! pv2Voltage = readS16 master sid 31029us
    let! pv2Current = readS16 master sid 31030us

    return
      { BatterySoc = float batterySoc / 10.0
        AvgCellTemperature = float avgCellTemp / 10.0
        AvgCellVoltage = float avgCellVoltage / 1000.0
        GridFrequency = float gridFrequency / 100.0
        InverterTemperature = float inverterTemp / 10.0
        Pv1Voltage = float pv1Voltage / 10.0
        Pv1Current = float pv1Current / 100.0
        Pv2Voltage = float pv2Voltage / 10.0
        Pv2Current = float pv2Current / 100.0 }
  }

// ---------------------------------------------------------------------------
// Display — Spectre.Console
// ---------------------------------------------------------------------------

let emsWorkModeLabel (mode: uint16) =
  match mode with
  | 0us -> "Max Self Consumption"
  | 1us -> "AI Mode"
  | 2us -> "TOU"
  | 3us -> "Remote EMS"
  | _ -> $"Unknown ({mode})"

let gridStatusLabel (status: uint16) =
  match status with
  | 0us -> "On-Grid"
  | 1us -> "Off-Grid"
  | 2us -> "Fault"
  | _ -> $"Unknown ({status})"

let runningStateLabel (state: uint16) =
  match state with
  | 0us -> "Standby"
  | 1us -> "Running"
  | 2us -> "Fault"
  | 3us -> "Shutdown"
  | _ -> $"Unknown ({state})"

let coloredPower (kw: float) =
  if kw > 0.0 then $"[green]{kw:F3} kW[/]"
  elif kw < 0.0 then $"[red]{kw:F3} kW[/]"
  else $"{kw:F3} kW"

let progressBar (value: float) (max: float) (width: int) (color: string) =
  let pct = Math.Clamp(value / max, 0.0, 1.0)
  let filled = int (pct * float width)
  let empty = width - filled
  let filledStr = String.replicate filled "\u2588"
  let emptyStr = String.replicate empty "\u2591"
  $"[{color}]{filledStr}[/][dim]{emptyStr}[/]"

let makeTable (title: string) (color: Color) (rows: (string * string) list) =
  let table =
    Table()
      .Title(title)
      .BorderColor(color)
      .AddColumn("Parameter")
      .AddColumn("Value")
      .Expand()
  for (param, value) in rows do
    table.AddRow(param, value) |> ignore
  table

let rowOf (items: IRenderable list) =
  let container =
    Table()
      .NoBorder()
      .HideHeaders()
      .Expand()
  for _ in items do
    container.AddColumn(TableColumn("").NoWrap()) |> ignore
  container.AddRow(items |> Array.ofList) |> ignore
  container :> IRenderable

let printReport (report: BatteryReport) =
  let p = report.Plant
  let i = report.Inverter

  // Header
  AnsiConsole.Write(FigletText("SigEnStor").Color(Color.Green))
  let timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
  AnsiConsole.MarkupLine $"[dim]Read at {timestamp}[/]"
  AnsiConsole.WriteLine()

  // Row 1: System Status | Grid | Solar | Plant
  let statusTable =
    makeTable "System Status" Color.Grey [
      "Running State", runningStateLabel p.PlantRunningState
      "Grid Status", gridStatusLabel p.OnOffGridStatus
      "EMS Work Mode", emsWorkModeLabel p.EmsWorkMode
    ]
  let gridLabel = if p.GridActivePower > 0.0 then "Importing" elif p.GridActivePower < 0.0 then "Exporting" else "Balanced"
  let gridTable =
    makeTable "Grid" Color.Blue [
      $"Power ({gridLabel})", coloredPower p.GridActivePower
      "Frequency", $"{i.GridFrequency:F2} Hz"
      "Inverter Temp", $"{i.InverterTemperature:F1} °C"
    ]
  let inferredSolar = p.TotalLoadPower + p.EssPower - p.GridActivePower
  let solarTable =
    makeTable "Solar (Enphase)" Color.Orange1 [
      "Production", coloredPower inferredSolar
      "Home Load", $"{p.TotalLoadPower:F3} kW"
    ]
  AnsiConsole.Write(rowOf [ statusTable; gridTable; solarTable ])
  AnsiConsole.WriteLine()

  // Row 2: Battery (full width with progress bars)
  let socColor = if p.EssSoc > 50.0 then "green" elif p.EssSoc > 20.0 then "yellow" else "red"
  let sohColor = if p.EssSoh > 80.0 then "green" elif p.EssSoh > 50.0 then "yellow" else "red"
  let essPowerLabel = if p.EssPower > 0.0 then "Charging" elif p.EssPower < 0.0 then "Discharging" else "Idle"
  let socBar = $"{progressBar p.EssSoc 100.0 30 socColor} [{socColor}]{p.EssSoc:F1}%%[/]"
  let sohBar = $"{progressBar p.EssSoh 100.0 30 sohColor} [{sohColor}]{p.EssSoh:F1}%%[/]"
  let battTable =
    Table()
      .Title("Battery")
      .BorderColor(Color.Yellow)
      .AddColumn("Parameter")
      .AddColumn("Value")
      .AddColumn("Bar")
      .Expand()
  battTable.AddRow("SOC", $"[{socColor}]{p.EssSoc:F1}%%[/]", socBar) |> ignore
  battTable.AddRow("SOH", $"[{sohColor}]{p.EssSoh:F1}%%[/]", sohBar) |> ignore
  battTable.AddRow($"Power ({essPowerLabel})", coloredPower p.EssPower, "") |> ignore
  battTable.AddRow("Rated Capacity", $"{p.EssRatedCapacity:F1} kWh", "") |> ignore
  battTable.AddRow("Avg Cell Voltage", $"{i.AvgCellVoltage:F3} V", "") |> ignore
  battTable.AddRow("Avg Cell Temp", $"{i.AvgCellTemperature:F1} °C", "") |> ignore
  battTable.AddRow("Charge Cut-Off", $"{p.ChargeCutOffSoc:F1}%%", "") |> ignore
  battTable.AddRow("Discharge Cut-Off", $"{p.DischargeCutOffSoc:F1}%%", "") |> ignore
  AnsiConsole.Write(battTable)

// ---------------------------------------------------------------------------
// Main pipeline
// ---------------------------------------------------------------------------

let readBatteryReport (master: IModbusMaster) : Task<Result<BatteryReport, string>> =
  taskResult {
    let! plantData = readPlantData master
    let! inverterData = readInverterData master
    return { Plant = plantData; Inverter = inverterData }
  }

let run () =
  task {
    match! createConnection config with
    | Error e -> return Error e
    | Ok (client, master) ->
      try
        return! readBatteryReport master
      finally
        client.Dispose()
  }

match run () |> Async.AwaitTask |> Async.RunSynchronously with
| Ok report ->
  printReport report
| Error msg ->
  let escaped = Markup.Escape msg
  AnsiConsole.MarkupLine $"[red bold]Error:[/] [red]{escaped}[/]"
  exit 1
