using System.IO;
using System.IO.Pipes;
using System.Text;
using GameGuard.Core;

namespace GameGuard.Agent;

public class PipeClient
{
    private readonly string _pipeName;
    public PipeClient(string pipeName) => _pipeName = pipeName;

    public PipeResponse Send(PipeRequest request)
    {
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
        client.Connect(3000);
        // leaveOpen: reader and writer share one pipe; disposing the reader first would
        // close it and make the writer's dispose-time flush throw "closed pipe".
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
        writer.WriteLine(PipeProtocol.SerializeRequest(request));
        var line = reader.ReadLine() ?? throw new IOException("no response");
        return PipeProtocol.DeserializeResponse(line);
    }
}
