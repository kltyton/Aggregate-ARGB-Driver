using HardwareLite.Configuration;
using HardwareLite.Monitoring;

namespace HardwareLite.Display;

internal static class MetricText
{
    public static readonly string[] Names = ["CPU 温度", "CPU 使用率", "CPU 功耗", "CPU 频率", "CPU 风扇", "GPU 温度", "GPU 使用率", "GPU 功耗", "GPU 频率", "主板温度", "指定传感器", "时间", "日期", "自定义文字", "指针时钟", "CPU 电压", "GPU 电压", "AIO 泵转速", "系统风扇转速", "内存频率"];
    public static string Format(ScreenElement item, SensorSnapshot data, TemperatureUnit unit)
    {
        unit = item.TemperatureUnit ?? unit;
        if (item.Metric == ScreenMetric.Text) return item.Text;
        if (item.Metric is ScreenMetric.Time or ScreenMetric.AnalogClock) return DateTime.Now.ToString(item.Format.Length > 0 ? item.Format : "HH:mm:ss");
        if (item.Metric == ScreenMetric.Date) return DateTime.Now.ToString(item.Format.Length > 0 ? item.Format : "yyyy-MM-dd");
        if (!data.IsFresh) return "--";
        if (item.Metric == ScreenMetric.Sensor)
            return data.Sensors.TryGetValue(item.SensorId, out var sensor) ? Value(sensor.Value, sensor.Unit, unit, item) : "--";
        var (value, suffix) = item.Metric switch
        {
            ScreenMetric.CpuTemperature => (data.CpuTemperature, "°C"),
            ScreenMetric.CpuUsage => (data.CpuUsage, "%"),
            ScreenMetric.CpuPower => (data.CpuPower, "W"),
            ScreenMetric.CpuClock => (data.CpuClock, "MHz"),
            ScreenMetric.CpuFan => (data.CpuFan, "RPM"),
            ScreenMetric.GpuTemperature => (data.GpuTemperature, "°C"),
            ScreenMetric.GpuUsage => (data.GpuUsage, "%"),
            ScreenMetric.GpuPower => (data.GpuPower, "W"),
            ScreenMetric.GpuClock => (data.GpuClock, "MHz"),
            ScreenMetric.SystemTemperature => (data.SystemTemperature, "°C"),
            ScreenMetric.CpuVoltage => (data.CpuVoltage, "V"), ScreenMetric.GpuVoltage => (data.GpuVoltage, "V"),
            ScreenMetric.AioPump => (data.AioPump, "RPM"), ScreenMetric.SystemFan => (data.SystemFan, "RPM"),
            ScreenMetric.MemoryClock => (data.MemoryClock, "MHz"),
            _ => throw new ArgumentOutOfRangeException(nameof(item.Metric))
        };
        return Value(value, suffix, unit, item);
    }

    private static string Value(float? value, string suffix, TemperatureUnit unit, ScreenElement item)
    {
        if (!value.HasValue) return "--";
        double number = value.Value;
        if (suffix == "°C" && unit == TemperatureUnit.Fahrenheit) { number = number * 1.8 + 32; suffix = "°F"; }
        string format = item.NumericFormat.Length > 0 ? item.NumericFormat : item.DecimalPlaces >= 0 ? "F" + item.DecimalPlaces : suffix == "V" ? "0.000" : "0";
        string text = number.ToString(format);
        return item.HideUnit ? text : text + " " + (item.CustomUnit.Length > 0 ? item.CustomUnit : suffix);
    }
}
