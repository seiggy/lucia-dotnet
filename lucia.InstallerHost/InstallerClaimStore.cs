using System.Security.Cryptography;
using System.Text;

namespace lucia.InstallerHost;

internal sealed class InstallerClaimStore(string claimPath)
{
    public const string CookieName = "lucia-installer-session";

    public bool IsClaimed => File.Exists(claimPath);

    public bool IsValid(string? token)
    {
        if (string.IsNullOrEmpty(token) || !File.Exists(claimPath))
        {
            return false;
        }

        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(File.ReadAllText(claimPath).Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("The installer claim file is invalid.");
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }

    public string? TryClaim()
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        var directory = Path.GetDirectoryName(claimPath)
            ?? throw new InvalidOperationException("The installer claim path has no directory.");
        Directory.CreateDirectory(directory);

        try
        {
            using var stream = new FileStream(
                claimPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    claimPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            using var writer = new StreamWriter(stream);
            writer.WriteLine(hash);
            writer.Flush();
            stream.Flush(flushToDisk: true);
            return token;
        }
        catch (IOException) when (File.Exists(claimPath))
        {
            return null;
        }
    }
}
