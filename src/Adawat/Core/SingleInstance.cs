using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Adawat.Core;

/// <summary>
/// Makes sure only one Adawat runs per user session. A second launch forwards its
/// command line (for example "--ocr image.png" from the Explorer menu) to the first one.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Id = $"Adawat-{Environment.UserName}";
    private static string MutexName => $"Local\\{Id}";
    private static string PipeName => $"{Id}-pipe";

    private readonly Mutex? _mutex;
    private CancellationTokenSource? _cts;

    public bool IsFirst { get; }

    private SingleInstance(Mutex? mutex, bool isFirst)
    {
        _mutex = mutex;
        IsFirst = isFirst;
    }

    public static SingleInstance Acquire()
    {
        try
        {
            var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (createdNew) return new SingleInstance(mutex, true);
            mutex.Dispose();
            return new SingleInstance(null, false);
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists but belongs to an elevated instance: it is running.
            return new SingleInstance(null, false);
        }
    }

    /// <summary>Sends arguments to the running instance. Returns false if it could not be reached.</summary>
    public static bool Forward(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            var payload = Encoding.UTF8.GetBytes(string.Join("\n", args));
            client.Write(payload, 0, payload.Length);
            client.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not reach the running instance", ex);
            return false;
        }
    }

    /// <summary>Starts listening for forwarded command lines. <paramref name="onArgs"/> runs on a background thread.</summary>
    public void Listen(Action<string[]> onArgs)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = CreateServer();
                    await server.WaitForConnectionAsync(token);
                    using var ms = new MemoryStream();
                    await server.CopyToAsync(ms, token);
                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    onArgs(text.Length == 0 ? Array.Empty<string>() : text.Split('\n'));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error("Instance pipe failed", ex);
                    await Task.Delay(1000, CancellationToken.None);
                }
            }
        }, token);
    }

    private static NamedPipeServerStream CreateServer()
    {
        // Allow the current user even when this instance runs elevated and the sender does not.
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User;
        if (user != null)
        {
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        }
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }
    }
}
