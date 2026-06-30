using Debate.Core;

namespace Debate.Tests;

public class SessionConfigTests
{
    [Fact]
    public void MaxRounds_defaults_when_not_positive()
    {
        Assert.Equal(SessionConfig.DefaultMaxRounds, new SessionConfig("default", 0.3f, 0.9f, 0.3f, true, 0).MaxRounds);
        Assert.Equal(SessionConfig.DefaultMaxRounds, new SessionConfig("default", 0.3f, 0.9f, 0.3f, true, -4).MaxRounds);
    }

    [Fact]
    public void MaxRounds_keeps_a_positive_value()
    {
        Assert.Equal(5, new SessionConfig("default", 0.3f, 0.9f, 0.3f, true, 5).MaxRounds);
    }

    [Fact]
    public void TemperatureFor_maps_each_role()
    {
        var config = new SessionConfig("default", 0.1f, 0.2f, 0.3f, true);
        Assert.Equal(0.1f, config.TemperatureFor(DebateRole.Answerer));
        Assert.Equal(0.2f, config.TemperatureFor(DebateRole.Critic));
        Assert.Equal(0.3f, config.TemperatureFor(DebateRole.Judge));
    }
}
