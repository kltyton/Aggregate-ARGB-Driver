using System.Diagnostics;
using BitOperations = System.Numerics.BitOperations;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using HardwareLite.Configuration;

namespace HardwareLite.Devices.Maxsun;
internal sealed class AudioSpectrum : IDisposable
{
    private readonly IWaveIn _capture;
    private readonly MMDevice? _device;
    private readonly AudioOptions _settings;
    private readonly Complex[] _fft;
    private readonly int _power;
    private int _position;
    private float[] _levels;
    private readonly float[] _silence;
    private long _timestamp;
    private Exception? _failure;
    public AudioSpectrum(AudioOptions? settings = null)
    {
        _settings = settings ?? new(); _settings.Validate();
        _fft = new Complex[_settings.FftSize]; _power = BitOperations.Log2((uint)_fft.Length); _levels = new float[_settings.Bands]; _silence = new float[_settings.Bands];
        if (_settings.Source == AudioCaptureSource.Application)
            _capture = new ProcessLoopbackCapture(_settings.ApplicationName, _settings.ExcludeApplication, _settings.SampleRate);
        else
        {
            using var devices = new MMDeviceEnumerator();
            _device = _settings.DeviceId.Length > 0 ? devices.GetDevice(_settings.DeviceId)
                : devices.GetDefaultAudioEndpoint(_settings.Source == AudioCaptureSource.InputDevice ? DataFlow.Capture : DataFlow.Render, Role.Multimedia);
            if (_device.DataFlow != (_settings.Source == AudioCaptureSource.InputDevice ? DataFlow.Capture : DataFlow.Render))
            { _device.Dispose(); throw new InvalidDataException("音频设备方向与采集来源不符，请选择对应的输入或输出设备。"); }
            _capture = _settings.Source == AudioCaptureSource.InputDevice ? new WasapiCapture(_device) : new WasapiLoopbackCapture(_device);
        }
        try
        {
            _capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_settings.SampleRate, 1);
            _capture.DataAvailable += (_, args) => Receive(args);
            _capture.RecordingStopped += (_, args) => Volatile.Write(ref _failure, args.Exception);
            _capture.StartRecording();
        }
        catch { _capture.Dispose(); _device?.Dispose(); throw; }
    }
    public float[] Levels
    {
        get
        {
            if (Volatile.Read(ref _failure) is { } error) throw new IOException("声音设备采样停止。", error);
            return Stopwatch.GetElapsedTime(Interlocked.Read(ref _timestamp)).TotalMilliseconds > _settings.SilenceMs
                ? _silence : Volatile.Read(ref _levels);
        }
    }
    private void Receive(WaveInEventArgs args)
    {
        for (int offset=0; offset+4<=args.BytesRecorded; offset+=4)
        {
            double window=_settings.Window switch {
                SpectrumWindow.Hamming => 0.54-0.46*Math.Cos(2*Math.PI*_position/(_fft.Length-1)),
                SpectrumWindow.Rectangular => 1, _ => FastFourierTransform.HannWindow(_position,_fft.Length) };
            _fft[_position].X=BitConverter.ToSingle(args.Buffer,offset)*(float)window; _fft[_position].Y=0;
            if(++_position!=_fft.Length)continue;
            FastFourierTransform.FFT(true,_power,_fft);
            var levels=new float[_settings.Bands];
            int minimum=Math.Max(1,(int)Math.Round(_settings.MinimumHz*_fft.Length/(double)_settings.SampleRate));
            int maximum=Math.Min(_fft.Length/2,(int)Math.Round(_settings.MaximumHz*_fft.Length/(double)_settings.SampleRate));
            var old=Volatile.Read(ref _levels);
            for(int band=0;band<levels.Length;band++)
            {
                int start=Math.Max(minimum,(int)(minimum*Math.Pow(maximum/(double)minimum,band/(double)levels.Length)));
                int end=Math.Max(start+1,(int)(minimum*Math.Pow(maximum/(double)minimum,(band+1)/(double)levels.Length)));
                float value=0;int bins=0;
                for(int bin=start;bin<Math.Min(end,maximum);bin++)
                {
                    float magnitude=MathF.Sqrt(_fft[bin].X*_fft[bin].X+_fft[bin].Y*_fft[bin].Y)*(float)_settings.AnalysisGain;
                    value=_settings.Aggregation==SpectrumAggregation.Peak?Math.Max(value,magnitude):value+magnitude;bins++;
                }
                if(_settings.Aggregation==SpectrumAggregation.Average && bins>0)value/=bins;
                int smooth=value>old[band]?_settings.AttackMs:_settings.DecayMs;
                double factor=smooth==0?1:1-Math.Exp(-1000d*_fft.Length/_settings.SampleRate/smooth);
                levels[band]=(float)(old[band]+(value-old[band])*factor);
            }
            Volatile.Write(ref _levels,levels);Interlocked.Exchange(ref _timestamp,Stopwatch.GetTimestamp());_position=0;
        }
    }
    public void Dispose(){_capture.Dispose();_device?.Dispose();}
}
