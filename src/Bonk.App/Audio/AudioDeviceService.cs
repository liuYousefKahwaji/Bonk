using NAudio.CoreAudioApi;

namespace Bonk.App.Audio;

public sealed record AudioDeviceInfo(string? Id, string Name);

public sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public IReadOnlyList<AudioDeviceInfo> GetDevices()
    {
        var result = new List<AudioDeviceInfo> { new(null, "Windows default microphone") };
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device) result.Add(new(device.ID, device.FriendlyName));
        }
        return result;
    }

    public MMDevice Open(string? id) => string.IsNullOrEmpty(id)
        ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
        : _enumerator.GetDevice(id);

    public void Dispose() => _enumerator.Dispose();
}
