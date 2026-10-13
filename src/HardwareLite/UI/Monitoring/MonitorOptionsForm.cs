using HardwareLite.Configuration;
using HardwareLite.Monitoring;
using HardwareLite.Display;
using HardwareLite.UI.Shell;
namespace HardwareLite.UI.Monitoring;
internal sealed class MonitorOptionsForm : OptionsForm<Settings>
{
    public MonitorOptionsForm(Settings value, SensorSnapshot snapshot) : base("共享监控 · 来源与后台", value, settings => settings.Validate())
    {
        UiTheme.Section(LeftFields, "监控类别");
        Toggle(LeftFields, "CPU", x => x.Monitoring.Cpu, (x,b) => x with { Monitoring=x.Monitoring with { Cpu=b } });
        Toggle(LeftFields, "GPU", x => x.Monitoring.Gpu, (x,b) => x with { Monitoring=x.Monitoring with { Gpu=b } });
        Toggle(LeftFields, "主板", x => x.Monitoring.Motherboard, (x,b) => x with { Monitoring=x.Monitoring with { Motherboard=b } });
        Toggle(LeftFields, "内存", x => x.Monitoring.Memory, (x,b) => x with { Monitoring=x.Monitoring with { Memory=b } });
        Toggle(LeftFields, "磁盘", x => x.Monitoring.Storage, (x,b) => x with { Monitoring=x.Monitoring with { Storage=b } });
        Toggle(LeftFields, "网络", x => x.Monitoring.Network, (x,b) => x with { Monitoring=x.Monitoring with { Network=b } });
        Toggle(LeftFields, "控制器", x => x.Monitoring.Controller, (x,b) => x with { Monitoring=x.Monitoring with { Controller=b } });
        Number(LeftFields, "数据过期时间 · ms", 100, 300000, x => x.Monitoring.ExpiryMs, (x,n) => x with { Monitoring=x.Monitoring with { ExpiryMs=n } });
        Choice(LeftFields, "CPU 频率统计", ["核心平均", "核心最大", "核心最小", "指定核心"], x => (int)x.Monitoring.CpuClockMethod,
            (x,n) => x with { Monitoring=x.Monitoring with { CpuClockMethod=(CpuClockMethod)n } });
        Source(LeftFields, "指定核心来源", snapshot.Sensors, x => x.Monitoring.CpuClockSensorId,
            (x,id) => x with { Monitoring=x.Monitoring with { CpuClockSensorId=id } }, "未指定核心");
        UiTheme.Section(LeftFields, "后台时序");
        Number(LeftFields, "连接失败重试 · ms", 100, 60000, x => x.Runtime.RetryMs, (x,n) => x with { Runtime=x.Runtime with { RetryMs=n } });
        Number(LeftFields, "进程冲突等待 · ms", 100, 60000, x => x.Runtime.ConflictRetryMs, (x,n) => x with { Runtime=x.Runtime with { ConflictRetryMs=n } });
        Number(LeftFields, "进程冲突检查 · ms", 100, 60000, x => x.Runtime.ConflictCheckMs, (x,n) => x with { Runtime=x.Runtime with { ConflictCheckMs=n } });
        Number(LeftFields, "停用轮询 · ms", 100, 60000, x => x.Runtime.DisabledPollMs, (x,n) => x with { Runtime=x.Runtime with { DisabledPollMs=n } });
        Number(LeftFields, "唤醒等待轮询 · ms", 100, 60000, x => x.Runtime.ResumePollMs, (x,n) => x with { Runtime=x.Runtime with { ResumePollMs=n } });
        UiTheme.Section(LeftFields, "设备唤醒延迟", "-1 沿用主页面的共享延迟，0 表示立即恢复。");
        Number(LeftFields, "水冷唤醒 · ms", -1, 60000, x => x.Runtime.ScreenResumeDelayMs, (x,n) => x with { Runtime=x.Runtime with { ScreenResumeDelayMs=n } });
        Number(LeftFields, "主板唤醒 · ms", -1, 60000, x => x.Runtime.LightingResumeDelayMs, (x,n) => x with { Runtime=x.Runtime with { LightingResumeDelayMs=n } });
        Number(LeftFields, "机箱唤醒 · ms", -1, 60000, x => x.Runtime.CaseResumeDelayMs, (x,n) => x with { Runtime=x.Runtime with { CaseResumeDelayMs=n } });
        UiTheme.Section(RightFields, "内置项目来源", "默认来源沿用原选择规则；指定来源失联后显示缺失，不自动改用其他通道。");
        foreach (var metric in Enum.GetValues<ScreenMetric>().Where(x => x is not (ScreenMetric.Time or ScreenMetric.Date or ScreenMetric.Text or ScreenMetric.AnalogClock or ScreenMetric.Sensor)))
        {
            var key = metric;
            Source(RightFields, MetricText.Names[(int)key], snapshot.Sensors, x => x.Monitoring.Sources.GetValueOrDefault(key) ?? "",
                (x,id) => { var sources=new Dictionary<ScreenMetric,string>(x.Monitoring.Sources); if(id.Length==0)sources.Remove(key);else sources[key]=id;return x with { Monitoring=x.Monitoring with { Sources=sources } }; });
        }
        Finish();
    }
}
