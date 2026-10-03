using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Bonk.App.Audio;

public delegate void AudioFrameHandler(ReadOnlySpan<float> samples, long timestampMs, double durationMs);

public sealed class MicrophoneCapture : IAsyncDisposable
{
    private readonly AudioDeviceService _devices;
    private WasapiRecorder? _recorder;
    private MMDevice? _device;
    private float[] _frame = [];
    private int _frameOffset;
    private int _channels;
    private int _bytesPerSample;
    private int _sampleRate;
    private bool _floatFormat;
    private long _frameStart;
    private volatile bool _running;
    private volatile string _status = "Connecting to microphone…";
    public event AudioFrameHandler? FrameAvailable;
    public event Action? StreamReset;
    public string Status => _status;
    public bool IsRunning => _running;
    public string? DeviceId => _device?.ID;
    public static long NowMs => (long)(Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));

    public MicrophoneCapture(AudioDeviceService devices) => _devices = devices;

    // Called from the UI's slow maintenance timer, including after device removal.
    public async Task ReconcileAsync(string? selectedId)
    {
        MMDevice? desired = null;
        try
        {
            desired = _devices.Open(selectedId);
            if (desired.State != DeviceState.Active)
                throw new InvalidOperationException("The selected microphone is disconnected.");
            if (_running && _device?.ID == desired.ID) { desired.Dispose(); return; }

            await StopAsync();
            _device = desired;
            desired = null;
            _recorder = new WasapiRecorderBuilder().WithDevice(_device)
                .WithBufferLength(20).WithLowLatency().WithMmcssThreadPriority("Audio").Build();
            var format = _recorder.WaveFormat;
            _channels = format.Channels;
            _sampleRate = format.SampleRate;
            _bytesPerSample = format.BitsPerSample / 8;
            _floatFormat = format.Encoding == WaveFormatEncoding.IeeeFloat
                || format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            var pcm = format.Encoding == WaveFormatEncoding.Pcm
                || format is WaveFormatExtensible pcmExt && pcmExt.SubFormat == new Guid("00000001-0000-0010-8000-00aa00389b71");
            if ((!_floatFormat && !pcm) || (_floatFormat && _bytesPerSample != 4)
                || (!_floatFormat && _bytesPerSample is not (1 or 2 or 3 or 4)))
                throw new NotSupportedException($"Unsupported microphone format: {format}.");

            _frame = new float[Math.Max(1, _sampleRate / 100)];
            _frameOffset = 0;
            StreamReset?.Invoke();
            _recorder.DataAvailable += OnData;
            var activeRecorder = _recorder;
            _recorder.RecordingStopped += (_, args) =>
            {
                if (!ReferenceEquals(activeRecorder, _recorder)) return;
                _running = false;
                _status = args.Exception is null ? "Microphone stopped. Reconnecting…"
                    : $"Microphone unavailable. Retrying… {args.Exception.Message}";
                StreamReset?.Invoke();
            };
            _running = true;
            _recorder.StartRecording();
            _status = $"Listening · {_device.FriendlyName}";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            desired?.Dispose();
            await StopAsync();
            _status = $"Microphone unavailable. Retrying… {ex.Message}";
        }
    }

    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0)
        {
            _frameOffset = 0;
            StreamReset?.Invoke();
        }
        var blockAlign = _bytesPerSample * _channels;
        var frameCount = buffer.Length / blockAlign;
        var packetTime = qpcPosition > 0 ? qpcPosition / 10000d : NowMs - frameCount * 1000d / _sampleRate;
        for (var i = 0; i < frameCount; i++)
        {
            // Keep the strongest channel so opposite-polarity stereo cannot cancel a tap.
            float strongest = 0;
            for (var channel = 0; channel < _channels; channel++)
            {
                var bytes = buffer.Slice(i * blockAlign + channel * _bytesPerSample, _bytesPerSample);
                var sample = _floatFormat ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes))
                    : _bytesPerSample switch
                    {
                        1 => (bytes[0] - 128) / 128f,
                        2 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
                        3 => ((bytes[0] | bytes[1] << 8 | bytes[2] << 16) << 8 >> 8) / 8388608f,
                        _ => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648f
                    };
                if (float.IsFinite(sample) && Math.Abs(sample) > Math.Abs(strongest)) strongest = sample;
            }
            if (_frameOffset == 0) _frameStart = (long)(packetTime + i * 1000d / _sampleRate);
            _frame[_frameOffset++] = strongest;
            if (_frameOffset == _frame.Length)
            {
                FrameAvailable?.Invoke(_frame, _frameStart, _frame.Length * 1000d / _sampleRate);
                _frameOffset = 0;
            }
        }
    }

    public async Task StopAsync()
    {
        _running = false;
        var recorder = _recorder;
        _recorder = null;
        if (recorder is not null)
        {
            recorder.DataAvailable -= OnData;
            await recorder.DisposeAsync();
        }
        _device?.Dispose();
        _device = null;
        StreamReset?.Invoke();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
