using System.Security.Cryptography;
using System.Text;

namespace AsmodayCat.Shared.Ipc;

// Named pipe between the app and its background service. Pipe names are machine-wide, so the
// name includes a hash of the Windows account: two users on one PC each get their own service.
public static class IpcPipe
{
    public static string Name { get; } = Build();

    private static string Build()
    {
        var account = $"{Environment.UserDomainName}\\{Environment.UserName}".ToLowerInvariant();
        var hash    = SHA256.HashData(Encoding.UTF8.GetBytes(account));
        return "AsmodayCat.Service." + Convert.ToHexString(hash, 0, 8);
    }
}
