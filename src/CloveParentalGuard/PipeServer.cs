using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

[SupportedOSPlatform("windows")]
public class PipeServer
{
    private const int MaxServerInstances = 4;
    private static readonly TimeSpan ClientReadTimeout = TimeSpan.FromSeconds(5);

    private readonly UnlockHandler _handler;
    private readonly string _pipeName;

    public PipeServer(UnlockHandler handler, string pipeName)
    {
        _handler = handler;
        _pipeName = pipeName;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Claim the name immediately and keep at least one instance alive at all
        // times so a malicious local process cannot squat the pipe name.
        var server = CreateServer(firstInstance: true);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await server.WaitForConnectionAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    server.Dispose();
                    server = CreateServer(firstInstance: false);
                    continue;
                }

                // Reserve a replacement listener BEFORE tearing down the connected
                // one, so the name is owned continuously (no squatting window).
                var next = CreateServer(firstInstance: false);
                await HandleClientAsync(server);
                server.Dispose();
                server = next;
            }
        }
        finally
        {
            server.Dispose();
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(ClientReadTimeout);
            using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true };

            var line = await reader.ReadLineAsync(timeoutCts.Token);
            if (line is null) return;

            PipeResponse response;
            try
            {
                var request = PipeProtocol.DeserializeRequest(line);
                response = _handler.Handle(request);
            }
            catch (Exception ex)
            {
                response = new PipeResponse(false, true, 0, "Bad request: " + ex.Message);
            }
            await writer.WriteLineAsync(PipeProtocol.SerializeResponse(response));
        }
        catch (OperationCanceledException) { /* client too slow — drop it */ }
        catch { /* drop misbehaving client, keep serving */ }
    }

    private NamedPipeServerStream CreateServer(bool firstInstance)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        var options = PipeOptions.Asynchronous;
        if (firstInstance) options |= PipeOptions.FirstPipeInstance;

        return NamedPipeServerStreamAcl.Create(
            _pipeName, PipeDirection.InOut, MaxServerInstances,
            PipeTransmissionMode.Byte, options,
            0, 0, security);
    }
}
