using NAudio.Wave;

namespace TKVoice.Infrastructure.Audio;

/// <summary>Lists recording devices and resolves a saved device name to the current device number.</summary>
public static class MicrophoneCatalog
{
    /// <summary>Uses the Windows default recording device.</summary>
    public const int DefaultDevice = -1;

    public static IReadOnlyList<string> ListDeviceNames()
    {
        var names = new List<string>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            names.Add(WaveIn.GetCapabilities(i).ProductName);
        }

        return names;
    }

    /// <summary>
    /// Device numbers change when devices are plugged in or out, so the name is resolved anew for
    /// every dictation. Unknown or empty names fall back to the Windows default device.
    /// </summary>
    public static int Resolve(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return DefaultDevice;
        }

        var names = ListDeviceNames();
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return DefaultDevice;
    }
}
