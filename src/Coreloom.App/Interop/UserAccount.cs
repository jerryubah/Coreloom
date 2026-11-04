using System.Runtime.InteropServices;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.App.Interop;

/// <summary>Who's signed in: Windows' display name for the account when it has one ("Jerry Ubah"), else the sign-in name.</summary>
internal static partial class UserAccount
{
    private const int NameDisplay = 3;

    public static AccountInfo Current()
    {
        string? display = null;
        try
        {
            var buffer = new char[256];
            uint size = (uint)buffer.Length;
            // Local accounts without a full name answer with nothing; a domain or Microsoft account with "First Last".
            unsafe
            {
                fixed (char* name = buffer)
                {
                    if (GetUserNameExW(NameDisplay, name, ref size) && size > 1)
                    {
                        display = new string(buffer, 0, (int)(size - 1));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Not available: the sign-in name does.
        }

        return new AccountInfo(Environment.UserName, Environment.MachineName, display);
    }

    [LibraryImport("secur32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserNameExW(int nameFormat, char* name, ref uint size);
}
