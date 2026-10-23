using Xunit;

namespace Neo.Input.Tests;

public class KeyboardFlightTests
{
    private const float Step = 1f / 60;

    [Theory]
    [InlineData("W", -1f, 0f, 0f)]
    [InlineData("S", 1f, 0f, 0f)]
    [InlineData("A", 0f, -1f, 0f)]
    [InlineData("D", 0f, 1f, 0f)]
    [InlineData("Q", 0f, 0f, -1f)]
    [InlineData("E", 0f, 0f, 1f)]
    public void MapsEachFlightKey(string key, float pitch, float roll, float yaw)
    {
        var state = KeyboardFlight.Map(Keys(key), invertPitch: false);
        Assert.Equal(pitch, state.Pitch);
        Assert.Equal(roll, state.Roll);
        Assert.Equal(yaw, state.Yaw);
        Assert.Equal(0f, state.ThrottleUp);
        Assert.Equal(0f, state.ThrottleDown);
        Assert.False(state.Fire);
    }

    [Fact]
    public void InvertPitchSwapsWAndS()
    {
        Assert.Equal(1f, KeyboardFlight.Map(Keys("W"), invertPitch: true).Pitch);
        Assert.Equal(-1f, KeyboardFlight.Map(Keys("S"), invertPitch: true).Pitch);
        Assert.Equal(0f, KeyboardFlight.Map(Keys("A"), invertPitch: true).Pitch);
    }

    [Fact]
    public void ShiftAndCtrlAreThrottle()
    {
        var up = KeyboardFlight.Map(Keys("Shift"), false);
        Assert.Equal(1f, up.ThrottleUp);
        Assert.Equal(0f, up.ThrottleDown);
        var down = KeyboardFlight.Map(Keys("Ctrl"), false);
        Assert.Equal(0f, down.ThrottleUp);
        Assert.Equal(1f, down.ThrottleDown);
        Assert.Equal(0f, down.Pitch);
    }

    [Fact]
    public void LeftMouseOrSpaceFires()
    {
        Assert.True(KeyboardFlight.Map(Keys("LeftMouse"), false).Fire);
        Assert.True(KeyboardFlight.Map(Keys("Space"), false).Fire);
        Assert.False(KeyboardFlight.Map(default, false).Fire);
    }

    [Fact]
    public void OppositeKeysCancel()
    {
        var state = KeyboardFlight.Map(new KeyState { W = true, S = true, A = true, D = true, Q = true, E = true }, false);
        Assert.Equal(0f, state.Pitch);
        Assert.Equal(0f, state.Roll);
        Assert.Equal(0f, state.Yaw);
    }

    [Fact]
    public void HeldKeyReachesFullDeflectionInAbout0Point15Seconds()
    {
        var flight = new KeyboardFlight();
        var keys = Keys("D");
        float previous = 0;
        for (int i = 0; i < 8; i++) // 0.133 s: still ramping
        {
            float roll = flight.Update(keys, false, Step).Roll;
            Assert.True(roll > previous && roll < 1f, $"step {i}: {roll}");
            previous = roll;
        }
        Assert.Equal(8f / 60 / KeyboardFlight.RampSeconds, previous, 4);
        flight.Update(keys, false, Step);
        flight.Update(keys, false, Step); // 0.167 s
        Assert.Equal(1f, flight.Roll);
        Assert.Equal(0.15f, KeyboardFlight.RampSeconds);
    }

    [Fact]
    public void ReleasedKeyReturnsToCenterFaster()
    {
        Assert.True(KeyboardFlight.ReturnSeconds < KeyboardFlight.RampSeconds);
        var flight = new KeyboardFlight();
        for (int i = 0; i < 30; i++)
            flight.Update(Keys("S"), false, Step);
        Assert.Equal(1f, flight.Pitch);
        int steps = 0;
        while (flight.Pitch > 0)
        {
            flight.Update(default, false, Step);
            steps++;
        }
        Assert.Equal(0f, flight.Pitch);
        Assert.InRange(steps, 1, 4); // ~0.05 s, versus ~9 steps out
    }

    [Fact]
    public void TapMakesASmallCorrection()
    {
        var flight = new KeyboardFlight();
        float yaw = flight.Update(Keys("E"), false, Step).Yaw;
        Assert.InRange(yaw, 0.1f, 0.12f);
        flight.Update(default, false, Step);
        Assert.Equal(0f, flight.Yaw);
    }

    [Fact]
    public void ReversingPassesThroughCenter()
    {
        var flight = new KeyboardFlight();
        for (int i = 0; i < 30; i++)
            flight.Update(Keys("D"), false, Step);
        flight.Update(Keys("A"), false, Step);
        Assert.InRange(flight.Roll, 0f, 0.7f); // heading back fast, never jumps across
        for (int i = 0; i < 30; i++)
            flight.Update(Keys("A"), false, Step);
        Assert.Equal(-1f, flight.Roll);
    }

    [Fact]
    public void ThrottleAndFireAreNotSmoothed()
    {
        var flight = new KeyboardFlight();
        var state = flight.Update(new KeyState { Shift = true, LeftMouse = true }, false, Step);
        Assert.Equal(1f, state.ThrottleUp);
        Assert.True(state.Fire);
    }

    [Fact]
    public void ResetCenters()
    {
        var flight = new KeyboardFlight();
        for (int i = 0; i < 30; i++)
            flight.Update(new KeyState { W = true, D = true, E = true }, false, Step);
        flight.Reset();
        Assert.Equal(0f, flight.Pitch);
        Assert.Equal(0f, flight.Roll);
        Assert.Equal(0f, flight.Yaw);
    }

    private static KeyState Keys(string key) => key switch
    {
        "W" => new KeyState { W = true },
        "S" => new KeyState { S = true },
        "A" => new KeyState { A = true },
        "D" => new KeyState { D = true },
        "Q" => new KeyState { Q = true },
        "E" => new KeyState { E = true },
        "Shift" => new KeyState { Shift = true },
        "Ctrl" => new KeyState { Ctrl = true },
        "Space" => new KeyState { Space = true },
        "LeftMouse" => new KeyState { LeftMouse = true },
        _ => default,
    };
}
