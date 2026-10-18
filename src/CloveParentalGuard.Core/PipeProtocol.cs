using System.Text.Json;

namespace CloveParentalGuard.Core;

public static class PipeProtocol
{
    public static string SerializeRequest(PipeRequest r) => JsonSerializer.Serialize(r);
    public static PipeRequest DeserializeRequest(string s) =>
        JsonSerializer.Deserialize<PipeRequest>(s) ?? throw new FormatException("bad request");

    public static string SerializeResponse(PipeResponse r) => JsonSerializer.Serialize(r);
    public static PipeResponse DeserializeResponse(string s) =>
        JsonSerializer.Deserialize<PipeResponse>(s) ?? throw new FormatException("bad response");
}
