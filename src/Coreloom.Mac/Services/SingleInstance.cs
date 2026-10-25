using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Coreloom.Core.Diagnostics;

namespace Coreloom.Mac.Services;

/// <summary>
/// One Coreloom per user. macOS already brings the running copy forward when the app is opened again from Finder, the Dock
/// or Spotlight (Coreloom shows its window on that "reopen"), so this only matters when a second copy is started anyway
/// (another copy of the app, <c>open -n</c>, Terminal): the first holds an exclusive lock on a file in Application Support,
/// and a second asks it over a socket in the same folder (only the user can reach it) to show its window, then quits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string ShowMessage = "show";

    private readonly string _lockPath;
    private readonly string _socketPath;
    private FileStream? _lock;
    private Socket? _listener;
    private CancellationTokenSource? _listening;

    public SingleInstance(string directory)
    {
        Directory.CreateDirectory(directory);
        _lockPath = Path.Combine(directory, ".instance.lock");
        _socketPath = Path.Combine(directory, ".instance.sock");
    }

    /// <summary>True when this is the only Coreloom running (the lock is held until <see cref="Dispose"/>).</summary>
    public bool TryAcquire()
    {
        try
        {
            // FileShare.None takes an exclusive flock(2) on Unix: released by the system even if Coreloom crashes.
            _lock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _lock.SetLength(0);
            byte[] pid = Encoding.ASCII.GetBytes(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _lock.Write(pid);
            _lock.Flush();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException e)
        {
            Log.Warn("App", "The single-instance lock couldn't be taken; continuing.", e);
            return true;
        }
    }

    /// <summary>Asks the running copy to show its window.</summary>
    public void SignalPrimary()
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(_socketPath));
            socket.Send(Encoding.ASCII.GetBytes(ShowMessage));
        }
        catch (SocketException e)
        {
            Log.Warn("App", "Asking the running Coreloom to show its window failed.", e);
        }
    }

    /// <summary>Calls <paramref name="show"/> (from a pool thread) whenever another launch asks for the window.</summary>
    public void ListenForActivation(Action show)
    {
        try
        {
            File.Delete(_socketPath);
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            File.SetUnixFileMode(_socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _listener.Listen(4);
            _listening = new CancellationTokenSource();
            var token = _listening.Token;
            _ = Task.Run(async () =>
            {
                var buffer = new byte[16];
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var client = await _listener.AcceptAsync(token).ConfigureAwait(false);
                        int read = await client.ReceiveAsync(buffer, SocketFlags.None, token).ConfigureAwait(false);
                        if (Encoding.ASCII.GetString(buffer, 0, read) == ShowMessage)
                        {
                            show();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (SocketException e)
                    {
                        Log.WarnThrottled("instance-socket", TimeSpan.FromMinutes(10), "App", "The activation socket failed.", e);
                        Thread.Sleep(1000);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                }
            }, token);
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("App", "Listening for other launches failed; reopening from the Dock still works.", e);
        }
    }

    /// <summary>Waits for a process (the copy that started this one) to quit, up to <paramref name="timeout"/>.</summary>
    public static void WaitForProcessExit(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(timeout);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // already gone
        }
    }

    public void Dispose()
    {
        _listening?.Cancel();
        _listener?.Dispose();
        _listener = null;
        if (_lock is not null)
        {
            try
            {
                File.Delete(_socketPath);
            }
            catch (IOException)
            {
            }

            _lock.Dispose();
            _lock = null;
        }
    }
}
