namespace CloveParentalGuard.Core.Tests;

public class PipeProtocolTests
{
    [Fact]
    public void Request_Roundtrips()
    {
        var r = new PipeRequest("unlock", "code1", 60);
        var back = PipeProtocol.DeserializeRequest(PipeProtocol.SerializeRequest(r));
        Assert.Equal("unlock", back.Type);
        Assert.Equal("code1", back.Code);
        Assert.Equal(60, back.DurationMinutes);
    }

    [Fact]
    public void Response_Roundtrips()
    {
        var r = new PipeResponse(true, false, 3600, null);
        var back = PipeProtocol.DeserializeResponse(PipeProtocol.SerializeResponse(r));
        Assert.True(back.Success);
        Assert.False(back.IsLocked);
        Assert.Equal(3600, back.RemainingSeconds);
    }
}
