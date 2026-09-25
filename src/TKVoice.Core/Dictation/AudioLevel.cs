using System.Runtime.InteropServices;

namespace TKVoice.Core.Dictation;

public static class AudioLevel
{
    private const double FloorDb = -50;
    private const double CeilingDb = -10;

    /// <summary>Maps the RMS loudness of 16-bit PCM to 0..1 on a decibel scale, so quiet speech still moves the meter.</summary>
    public static double Compute(ReadOnlySpan<byte> pcm16)
    {
        var samples = MemoryMarshal.Cast<byte, short>(pcm16);
        if (samples.IsEmpty)
        {
            return 0;
        }

        double sumOfSquares = 0;
        foreach (var sample in samples)
        {
            var normalized = sample / 32768.0;
            sumOfSquares += normalized * normalized;
        }

        var rms = Math.Sqrt(sumOfSquares / samples.Length);
        var db = 20 * Math.Log10(Math.Max(rms, 1e-6));
        return Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0, 1);
    }
}
