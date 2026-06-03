using System.Security.Cryptography;
using System.Text;

namespace Blitztext.Core.Security;

public sealed class ApiKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Blitztext.Windows.OpenAIAPIKey.v1");
    private readonly string path;

    public ApiKeyStore(string? keyPath = null)
    {
        path = keyPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Blitztext",
            "openai.key");
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Load());

    public string DisplayValue
    {
        get
        {
            var value = Load();
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value.Length > 8 ? $"{value[..4]} ********" : "********";
        }
    }

    public string? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, protectedBytes);
    }

    public void Delete()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
