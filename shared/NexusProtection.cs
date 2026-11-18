using System.Security.Cryptography;
using System.Text;

namespace Hats.Core;

public static class NexusProtection
{
    public static readonly string[] MandatoryPaths = ["atmosphere/usb_content", "config/nexusmanager", "config/nexus-manager"];
    public static ExclusionSnapshot BundledSnapshot()
    {
        using var stream = typeof(NexusProtection).Assembly.GetManifestResourceStream("Hats.Core.hats-exclusions.json")
            ?? throw new IOException("The included Nexus protection list is missing. Reinstall the desktop app.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var json = reader.ReadToEnd();
        var paths = ExclusionService.Parse(json);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new(ReleaseService.Repository, ExclusionService.FileName, "bundled:" + digest, digest, DateTime.UtcNow, paths, json);
    }
    public static bool IsPackAsset(string name)
    {
        var lower = name.ToLowerInvariant();
        return !lower.StartsWith("nexus-hats-desktop") && !lower.StartsWith("nexus-hats-installer") &&
            !lower.StartsWith("nexus-usb-formatter") && !lower.StartsWith("nexus-manager") &&
            !lower.StartsWith("source.") && !lower.Contains("-source.") && !lower.Contains("-source-") &&
            !lower.EndsWith(".symbols.zip");
    }
}
