using HardwareLite.Configuration;
using HardwareLite.Monitoring;
using HardwareLite.UI.Shell;
namespace HardwareLite.UI.Case;
internal sealed class CaseOptionsForm : OptionsForm<CaseOptions>
{
    public CaseOptionsForm(CaseOptions value, int defaultSeconds, SensorSnapshot snapshot) : base("机箱屏幕 · 页面与来源", value, options => options.Validate())
    {
        UiTheme.Section(LeftFields, "轮播顺序", "留空使用原自动轮播；自定义条目按位置播放，不能重复。");
        var slots = new CaseSubPage?[3];
        for (int position = 0; position < value.CyclePages.Length && position < slots.Length; position++) slots[position] = value.CyclePages[position];
        for(int position=0;position<3;position++)
        {
            int index=position;
            Choice(LeftFields, "位置 "+(position+1), ["不参与","CPU 频率","CPU 风扇","GPU"], _ => slots[index].HasValue ? (int)slots[index]!.Value - 1 : 0,
                (x,n) => { slots[index]=n==0?null:(CaseSubPage)(n+1);return x with {CyclePages=slots.Where(p=>p.HasValue).Select(p=>p!.Value).ToArray()}; });
        }
        foreach(var page in Enum.GetValues<CaseSubPage>())
        {
            var key=page;
            Number(LeftFields, key==CaseSubPage.CpuFrequency?"CPU 频率页 · ms":key==CaseSubPage.CpuFan?"CPU 风扇页 · ms":"GPU 页 · ms", 100, 60000,
                x => x.PageDurationsMs.GetValueOrDefault(key,defaultSeconds*1000),
                (x,n) => {var times=new Dictionary<CaseSubPage,int>(x.PageDurationsMs){[key]=n};return x with {PageDurationsMs=times};});
        }
        UiTheme.Section(RightFields, "来源与更新");
        Source(RightFields, "风扇页传感器", snapshot.Sensors.Where(p=>p.Value.Unit=="RPM").ToDictionary(p=>p.Key,p=>p.Value), x=>x.FanSensorId,(x,id)=>x with {FanSensorId=id},"跟随共享 CPU 风扇");
        Number(RightFields,"更新间隔 · ms",0,10000,x=>x.UpdateIntervalMs,(x,n)=>x with {UpdateIntervalMs=n});
        Choice(RightFields,"温度单位",["跟随共享设置","摄氏 °C","华氏 °F"],x=>x.TemperatureUnit.HasValue?(int)x.TemperatureUnit.Value+1:0,(x,n)=>x with {TemperatureUnit=n==0?null:(TemperatureUnit)(n-1)});
        UiTheme.Section(RightFields,"数据与停用","间隔 0 跟随共享采样。部分显示时设备缺失数字为 0，不能显示普通 LCD 的文字占位符。");
        Choice(RightFields,"数据缺失时",["显示可用值／缺失为0","保留最后画面","关闭显示","重新连接"],x=>(int)x.MissingData,(x,n)=>x with {MissingData=(MissingDataPolicy)n});
        Choice(RightFields,"停用时",["保持设备状态","关闭显示"],x=>(int)x.OnStop,(x,n)=>x with {OnStop=(DeviceStopBehavior)n});
        Finish();
    }
}
