using TKVoice.Core.Dictation;

namespace TKVoice.Tests.Dictation;

public class AudioLevelTests
{
    [Fact]
    public void Silence_is_zero()
    {
        Assert.Equal(0, AudioLevel.Compute(new byte[960]));
    }

    [Fact]
    public void Full_scale_is_one()
    {
        Assert.Equal(1, AudioLevel.Compute(Pcm(short.MaxValue, 480)));
    }

    [Fact]
    public void Normal_speech_level_is_in_the_middle()
    {
        // About -30 dBFS, typical for speech at a normal distance.
        var level = AudioLevel.Compute(Pcm(1036, 480));
        Assert.InRange(level, 0.4, 0.6);
    }

    [Fact]
    public void Empty_buffer_is_zero()
    {
        Assert.Equal(0, AudioLevel.Compute([]));
    }

    private static byte[] Pcm(short amplitude, int samples)
    {
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = i % 2 == 0 ? amplitude : (short)-amplitude;
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), value);
        }

        return bytes;
    }
}
