using System.Runtime.InteropServices;
using Coreloom.Hardware.Mac.Native;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.Mac.Native;

/// <summary>Who's signed in: the account's full name ("Jerry Ubah") and this Mac's name ("Jerry's MacBook Pro").</summary>
internal static partial class UserAccount
{
    static UserAccount() => NativeLibrary.TryLoad("/System/Library/Frameworks/Foundation.framework/Foundation", out _);

    public static AccountInfo Current()
    {
        string? fullName = null, computer = null;
        try
        {
            using var pool = new ObjC.Pool();
            fullName = ObjC.ToStringValue(NSFullUserName());
            // The name in System Settings › General › About, not the network host name.
            nint host = ObjC.Send(ObjC.Class("NSHost"), ObjC.Sel("currentHost"));
            computer = ObjC.ToStringValue(ObjC.Send(host, ObjC.Sel("localizedName")));
        }
        catch (Exception)
        {
            // Foundation not answering: Environment's names do.
        }

        return new AccountInfo(Environment.UserName, string.IsNullOrWhiteSpace(computer) ? Environment.MachineName : computer, fullName);
    }

    [LibraryImport("/System/Library/Frameworks/Foundation.framework/Foundation")]
    private static partial nint NSFullUserName();
}
