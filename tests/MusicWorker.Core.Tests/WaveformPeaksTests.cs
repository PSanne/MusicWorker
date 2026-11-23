using MusicWorker.Core.Audio;
using MusicWorker.Core.Visualization;

namespace MusicWorker.Core.Tests;

public class WaveformPeaksTests
{
    [Fact]
    public void Peaks_cover_all_channels_of_each_column()
    {
        // Stereo: Frames (L,R) = (0.1,-0.2) (0.5,0) | (-0.9,0.3) (0,0)
        var buffer = new AudioBuffer([0.1f, -0.2f, 0.5f, 0f, -0.9f, 0.3f, 0f, 0f], 1000, 2);

        var peaks = WaveformPeaks.Compute(buffer, 0, 4, columns: 2);

        Assert.Equal(new PeakColumn(-0.2f, 0.5f), peaks[0]);
        Assert.Equal(new PeakColumn(-0.9f, 0.3f), peaks[1]);
    }

    [Fact]
    public void Zoomed_in_columns_use_the_nearest_frame()
    {
        var buffer = new AudioBuffer([0.1f, 0.2f], 1000, 1);

        var peaks = WaveformPeaks.Compute(buffer, 0, 2, columns: 4);

        Assert.Equal([0.1f, 0.1f, 0.2f, 0.2f], peaks.Select(p => p.Max).ToArray());
    }

    [Fact]
    public void Columns_beyond_the_end_stay_empty()
    {
        var buffer = new AudioBuffer([0.5f, 0.5f], 1000, 1);

        var peaks = WaveformPeaks.Compute(buffer, 1, 4, columns: 4);

        Assert.Equal(new PeakColumn(0.5f, 0.5f), peaks[0]);
        Assert.Equal(default, peaks[3]);
    }
}
