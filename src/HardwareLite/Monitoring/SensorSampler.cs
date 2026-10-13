using HardwareLite.Configuration;
using LibreHardwareMonitor.Hardware;

namespace HardwareLite.Monitoring;

public sealed class SensorSampler : ISensorSource
{
    private SensorSnapshot _snapshot = SensorSnapshot.Empty;
    private SensorOption[] _gpus = [];
    private SensorOption[] _fans = [];
    private SensorOption[] _boardTemperatures = [];
    public SensorSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public IReadOnlyList<SensorOption> Gpus => Volatile.Read(ref _gpus);
    public IReadOnlyList<SensorOption> Fans => Volatile.Read(ref _fans);
    public IReadOnlyList<SensorOption> BoardTemperatures => Volatile.Read(ref _boardTemperatures);
    public void ReportFailure(string message) => Volatile.Write(ref _snapshot, SensorSnapshot.Empty with { Status = message });

    public async Task RunAsync(Func<Settings> settings, Func<bool> needed, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (!needed()) { await Task.Delay(1000, cancellation).ConfigureAwait(false); continue; }
            var options = settings().Monitoring;
            int categories = Categories(options);
            var computer = new Computer
            {
                IsCpuEnabled = options.Cpu, IsGpuEnabled = options.Gpu, IsMotherboardEnabled = options.Motherboard,
                IsStorageEnabled = options.Storage, IsNetworkEnabled = options.Network,
                IsMemoryEnabled = options.Memory, IsControllerEnabled = options.Controller
            };
            using var catalog = new SensorCatalog(computer);
            try
            {
                computer.Open();
                SensorTopology? published = null;
                Settings? selectedSettings = null;
                SensorSelection? selection = null;
                while (!cancellation.IsCancellationRequested && needed())
                {
                    var config = settings();
                    if (Categories(config.Monitoring) != categories) break;
                    var topology = catalog.Topology;
                    foreach (var hardware in topology.Hardware) hardware.Update();
                    topology = catalog.Topology;
                    if (!ReferenceEquals(topology, published))
                    {
                        Volatile.Write(ref _gpus, topology.GpuOptions);
                        Volatile.Write(ref _fans, topology.FanOptions);
                        Volatile.Write(ref _boardTemperatures, topology.TemperatureOptions);
                        published = topology; selectedSettings = null;
                    }
                    if (!ReferenceEquals(config, selectedSettings))
                    {
                        selection = new SensorSelection(topology, config);
                        selectedSettings = config;
                    }
                    var readings = topology.Capture();
                    float? Source(ScreenMetric metric, float? automatic) => config.Monitoring.Sources.TryGetValue(metric, out string? id) && id.Length > 0
                        ? readings.GetValueOrDefault(id)?.Value : automatic;
                    float? Read(ScreenMetric metric) => Source(metric, selection!.Read(metric));
                    float? automaticTemperature = selection!.Read(ScreenMetric.CpuTemperature);
                    float? temperature = Source(ScreenMetric.CpuTemperature, automaticTemperature);
                    var snapshot = new SensorSnapshot(DateTimeOffset.UtcNow,
                        temperature, Read(ScreenMetric.CpuPower), Read(ScreenMetric.CpuUsage),
                        Source(ScreenMetric.CpuClock, selection!.CpuClock(config.Monitoring)),
                        Source(ScreenMetric.CpuFan, selection.CpuFan?.Value),
                        Read(ScreenMetric.GpuTemperature), Read(ScreenMetric.GpuPower), Read(ScreenMetric.GpuUsage), Read(ScreenMetric.GpuClock),
                        Source(ScreenMetric.SystemTemperature, selection.SystemTemperature(config)),
                        automaticTemperature.HasValue ? "监控已连接" : "CPU 温度未读取；请以管理员权限运行并检查 PawnIO")
                    {
                        ExpiryMs = config.Monitoring.ExpiryMs,
                        CpuVoltage = Source(ScreenMetric.CpuVoltage, selection.Read(ScreenMetric.CpuVoltage) ?? selection.BoardVoltage()),
                        GpuVoltage = Read(ScreenMetric.GpuVoltage),
                        MemoryClock = Read(ScreenMetric.MemoryClock),
                        AioPump = Source(ScreenMetric.AioPump, selection.AioPump?.Value),
                        SystemFan = Source(ScreenMetric.SystemFan, selection.SystemFan?.Value),
                        Sensors = readings
                    };
                    Volatile.Write(ref _snapshot, snapshot);
                    await Task.Delay(config.SampleIntervalMs, cancellation).ConfigureAwait(false);
                }
            }
            finally { computer.Close(); }
        }
    }

    private static string? Unit(SensorType type) => type switch
    {
        SensorType.Temperature => "°C", SensorType.Power => "W", SensorType.Load => "%",
        SensorType.Clock => "MHz", SensorType.Fan => "RPM", SensorType.Voltage => "V",
        SensorType.Data => "GB", _ => null
    };
    private static int Categories(MonitorOptions options) => (options.Cpu ? 1 : 0) | (options.Gpu ? 2 : 0) | (options.Motherboard ? 4 : 0)
        | (options.Memory ? 8 : 0) | (options.Storage ? 16 : 0) | (options.Network ? 32 : 0) | (options.Controller ? 64 : 0);

    private sealed class SensorCatalog : IDisposable
    {
        private readonly Computer _computer;
        private readonly object _subscriptionsGate = new();
        private readonly Dictionary<IHardware, IHardware> _subscriptions = new(ReferenceEqualityComparer.Instance);
        private int _revision, _builtRevision = -1;
        private SensorTopology? _topology;

        public SensorCatalog(Computer computer)
        {
            _computer = computer;
            computer.HardwareAdded += Added;
            computer.HardwareRemoved += Removed;
        }
        public SensorTopology Topology
        {
            get
            {
                int revision = Volatile.Read(ref _revision);
                if (_topology is null || _builtRevision != revision)
                {
                    _topology = new SensorTopology(_computer.Hardware);
                    _builtRevision = revision;
                }
                return _topology;
            }
        }
        private void Added(IHardware hardware)
        {
            Subscribe(hardware, hardware);
            Interlocked.Increment(ref _revision);
        }
        private void Subscribe(IHardware hardware, IHardware root)
        {
            lock (_subscriptionsGate)
                if (!_subscriptions.TryAdd(hardware, root)) return;
            foreach (var sensor in hardware.Sensors) sensor.ValuesTimeWindow = TimeSpan.Zero;
            hardware.SensorAdded += SensorAdded;
            hardware.SensorRemoved += SensorRemoved;
            foreach (var child in hardware.SubHardware) Subscribe(child, root);
        }
        private void SensorAdded(ISensor sensor)
        {
            sensor.ValuesTimeWindow = TimeSpan.Zero;
            Interlocked.Increment(ref _revision);
        }
        private void SensorRemoved(ISensor sensor) => Interlocked.Increment(ref _revision);
        private void Removed(IHardware hardware)
        {
            IHardware[] removed;
            lock (_subscriptionsGate)
            {
                removed = _subscriptions.Where(pair => ReferenceEquals(pair.Value, hardware)).Select(pair => pair.Key).ToArray();
                foreach (var item in removed) _subscriptions.Remove(item);
            }
            foreach (var item in removed) Unsubscribe(item);
            Interlocked.Increment(ref _revision);
        }
        private void Unsubscribe(IHardware hardware)
        {
            hardware.SensorAdded -= SensorAdded;
            hardware.SensorRemoved -= SensorRemoved;
        }
        public void Dispose()
        {
            _computer.HardwareAdded -= Added;
            _computer.HardwareRemoved -= Removed;
            IHardware[] remaining;
            lock (_subscriptionsGate) { remaining = [.. _subscriptions.Keys]; _subscriptions.Clear(); }
            foreach (var hardware in remaining) Unsubscribe(hardware);
        }
    }

    private sealed record SensorEntry(string Key, string Name, string Unit, ISensor Sensor);
    private sealed class SensorTopology
    {
        public IHardware[] Hardware { get; }
        public IHardware[] Gpus { get; }
        public ISensor[] CpuSensors { get; }
        public ISensor[] FanSensors { get; }
        public ISensor[] BoardSensors { get; }
        public ISensor[] BoardTemperatures { get; }
        public SensorOption[] GpuOptions { get; }
        public SensorOption[] FanOptions { get; }
        public SensorOption[] TemperatureOptions { get; }
        private readonly SensorEntry[] _entries;
        private Dictionary<string, SensorReading> _readings;

        public SensorTopology(IEnumerable<IHardware> roots)
        {
            Hardware = roots.SelectMany(Flatten).ToArray();
            Gpus = Hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToArray();
            CpuSensors = Hardware.Where(h => h.HardwareType == HardwareType.Cpu).SelectMany(h => h.Sensors).ToArray();
            FanSensors = Hardware.SelectMany(h => h.Sensors).Where(s => s.SensorType == SensorType.Fan).ToArray();
            BoardSensors = Hardware.Where(h => h.HardwareType == HardwareType.SuperIO).SelectMany(h => h.Sensors).ToArray();
            BoardTemperatures = BoardSensors.Where(s => s.SensorType == SensorType.Temperature).ToArray();
            GpuOptions = Gpus.Select(h => new SensorOption(h.Identifier.ToString(), h.Name)).ToArray();
            FanOptions = FanSensors.Select(s => new SensorOption(s.Identifier.ToString(), s.Hardware.Name + " · " + s.Name)).ToArray();
            TemperatureOptions = BoardTemperatures.Select(s => new SensorOption(s.Identifier.ToString(), s.Hardware.Name + " · " + s.Name)).ToArray();
            _entries = Hardware.SelectMany(h => h.Sensors).Where(s => Unit(s.SensorType) is not null)
                .Select(s => new SensorEntry(s.Identifier + ":" + s.Name, s.Hardware.Name + " · " + s.Name, Unit(s.SensorType)!, s))
                .DistinctBy(entry => entry.Key).ToArray();
            _readings = _entries.ToDictionary(entry => entry.Key, entry => new SensorReading(entry.Name, Value(entry.Sensor), entry.Unit), StringComparer.Ordinal);
        }
        public IReadOnlyDictionary<string, SensorReading> Capture()
        {
            Dictionary<string, SensorReading>? changed = null;
            foreach (var entry in _entries)
            {
                float? value = Value(entry.Sensor);
                var previous = _readings[entry.Key];
                if (previous.Value.HasValue == value.HasValue && (!value.HasValue ||
                    BitConverter.SingleToInt32Bits(previous.Value!.Value) == BitConverter.SingleToInt32Bits(value.Value))) continue;
                changed ??= new Dictionary<string, SensorReading>(_readings, StringComparer.Ordinal);
                changed[entry.Key] = previous with { Value = value };
            }
            if (changed is not null) _readings = changed;
            return _readings;
        }
        private static float? Value(ISensor sensor) => sensor.Value is float value && float.IsFinite(value) ? value : null;
        private static IEnumerable<IHardware> Flatten(IHardware hardware)
        {
            yield return hardware;
            foreach (var child in hardware.SubHardware)
                foreach (var item in Flatten(child)) yield return item;
        }
    }

    private sealed class SensorSelection
    {
        private readonly Dictionary<ScreenMetric, ISensor[]> _defaults;
        private readonly ISensor[] _coreClocks, _boardVoltage, _systemTemperature;
        private readonly ISensor? _selectedClock, _selectedTemperature;
        public ISensor? CpuFan { get; }
        public ISensor? AioPump { get; }
        public ISensor? SystemFan { get; }

        public SensorSelection(SensorTopology topology, Settings settings)
        {
            var gpu = topology.Gpus.FirstOrDefault(h => h.Identifier.ToString() == settings.GpuId)
                ?? topology.Gpus.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia) ?? topology.Gpus.FirstOrDefault();
            var gpuSensors = gpu?.Sensors ?? [];
            var cpu = topology.CpuSensors;
            _defaults = new()
            {
                [ScreenMetric.CpuTemperature] = Bind(cpu, SensorType.Temperature, "CPU (Tctl/Tdie)", "CPU Package", "Core (Tctl/Tdie)"),
                [ScreenMetric.CpuPower] = Bind(cpu, SensorType.Power, "Package", "CPU Package"),
                [ScreenMetric.CpuUsage] = Bind(cpu, SensorType.Load, "CPU Total"),
                [ScreenMetric.CpuVoltage] = Bind(cpu, SensorType.Voltage, "Core (SVI3 TFN)", "Core (SVI2 TFN)", "CPU Core", "CPU VCore"),
                [ScreenMetric.MemoryClock] = Bind(cpu, SensorType.Clock, "Memory", "Memory Clock"),
                [ScreenMetric.GpuTemperature] = Bind(gpuSensors, SensorType.Temperature, "GPU Core"),
                [ScreenMetric.GpuPower] = Bind(gpuSensors, SensorType.Power, "GPU Package", "GPU Power"),
                [ScreenMetric.GpuUsage] = Bind(gpuSensors, SensorType.Load, "GPU Core"),
                [ScreenMetric.GpuClock] = Bind(gpuSensors, SensorType.Clock, "GPU Core"),
                [ScreenMetric.GpuVoltage] = Bind(gpuSensors, SensorType.Voltage, "GPU Core")
            };
            CpuFan = topology.FanSensors.FirstOrDefault(s => s.Identifier.ToString() == settings.CpuFanId);
            AioPump = topology.FanSensors.FirstOrDefault(s => s.Identifier.ToString() == settings.AioPumpId);
            SystemFan = topology.FanSensors.FirstOrDefault(s => s.Identifier.ToString() == settings.SystemFanId);
            _coreClocks = cpu.Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("Core", StringComparison.OrdinalIgnoreCase)).ToArray();
            _selectedClock = cpu.FirstOrDefault(s => s.Identifier + ":" + s.Name == settings.Monitoring.CpuClockSensorId);
            _boardVoltage = Bind(topology.BoardSensors, SensorType.Voltage, "VCore", "CPU VCore");
            _systemTemperature = Bind(topology.BoardTemperatures, SensorType.Temperature, "System", "Motherboard");
            _selectedTemperature = topology.BoardTemperatures.FirstOrDefault(s => s.Identifier.ToString() == settings.SystemTemperatureId);
        }
        public float? Read(ScreenMetric metric) => Pick(_defaults[metric]);
        public float? BoardVoltage() => Pick(_boardVoltage);
        public float? SystemTemperature(Settings settings) => settings.SystemTemperatureId.Length == 0 ? Pick(_systemTemperature) : _selectedTemperature?.Value;
        public float? CpuClock(MonitorOptions options)
        {
            if (options.CpuClockMethod == CpuClockMethod.SelectedCore) return _selectedClock?.Value;
            double sum = 0; int count = 0; float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (var sensor in _coreClocks)
            {
                if (sensor.Value is not float value || !float.IsFinite(value)) continue;
                sum += value; ++count;
                if (value < minimum) minimum = value;
                if (value > maximum) maximum = value;
            }
            return count == 0 ? null : options.CpuClockMethod switch
            {
                CpuClockMethod.Maximum => maximum, CpuClockMethod.Minimum => minimum, _ => (float)(sum / count)
            };
        }
        private static ISensor[] Bind(ISensor[] sensors, SensorType type, params string[] names) => names
            .Select(name => sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .OfType<ISensor>().ToArray();
        private static float? Pick(ISensor[] sensors)
        {
            foreach (var sensor in sensors)
                if (sensor.Value is float value && float.IsFinite(value)) return value;
            return null;
        }
    }
}
