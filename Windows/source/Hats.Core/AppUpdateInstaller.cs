using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace Hats.Core;

[SupportedOSPlatform("windows")]
public static class AppUpdateInstaller
{
    public static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusHATSDesktop.WinUI", "updates");
    public sealed record Request(string Target, string Version, string OldHash, string NewHash, int ParentId, long ParentStartedUtcTicks);
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

    public static string CreateWorkspace(string target, long downloadSize)
    {
        target = Paths.Root(target);
        var directory = Path.GetDirectoryName(target)!;
        if (!File.Exists(target) || !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Run the installer executable from a folder on your computer before updating it.");
        try
        {
            using var probe = new FileStream(Path.Combine(directory, ".hats-update-check-" + Guid.NewGuid().ToString("N")),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (UnauthorizedAccessException e) { throw new IOException("The app folder is not writable. Move the installer to a folder such as Desktop or Downloads, then check for updates again.", e); }
        Directory.CreateDirectory(Paths.Root(CacheRoot));
        long currentSize = new FileInfo(target).Length;
        if (Paths.FreeSpace(CacheRoot) < downloadSize + currentSize + 20L * 1048576 || Paths.FreeSpace(target) < downloadSize + currentSize + 20L * 1048576)
            throw new IOException("There is not enough free space for the app update and its previous-version copy.");
        var stage = Paths.Checked(CacheRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage); return stage;
    }

    public static void VerifyExecutable(string file, Version expected)
    {
        using (var input = File.OpenRead(file))
        {
            var header = new byte[64]; input.ReadExactly(header);
            if (header[0] != 'M' || header[1] != 'Z') throw new InvalidDataException("The app download is not a Windows executable.");
            var offset = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(60));
            if (offset < 64 || offset > input.Length - 6) throw new InvalidDataException("The app executable header is invalid.");
            input.Position = offset; var pe = new byte[6]; input.ReadExactly(pe);
            if (!pe.AsSpan(0, 4).SequenceEqual("PE\0\0"u8) || BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(4)) != 0x8664)
                throw new InvalidDataException("The app download is not a Windows x64 executable.");
        }
        var details = FileVersionInfo.GetVersionInfo(file);
        var version = new Version(details.FileMajorPart, details.FileMinorPart, details.FileBuildPart, details.FilePrivatePart);
        if (details.ProductName != "Nexus HATS Installer" || details.OriginalFilename != "Nexus-HATS-Desktop.x64.dll" || version != expected)
            throw new InvalidDataException("The downloaded app identity or version does not match the published release.");
    }

    public static string Prepare(string stage, string target, AppRelease release)
    {
        ValidateStage(stage); target = Paths.Root(target);
        var downloaded = Paths.Checked(stage, "download.exe");
        VerifyExecutable(downloaded, release.Version);
        var newHash = Hash(downloaded);
        if (!newHash.Equals(release.Asset.Digest![7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The staged app checksum changed.");
        var oldVersion = FileVersionInfo.GetVersionInfo(target);
        if (release.Version <= new Version(oldVersion.FileMajorPart, oldVersion.FileMinorPart, oldVersion.FileBuildPart, oldVersion.FilePrivatePart))
            throw new IOException("The app update must be newer than the installed app.");
        var oldHash = Hash(target);
        File.Copy(target, Paths.Checked(stage, "updater.exe"), false);
        using var parent = Process.GetCurrentProcess();
        var request = new Request(target, release.Version.ToString(), oldHash, newHash, parent.Id, parent.StartTime.ToUniversalTime().Ticks);
        var requestPath = Paths.Checked(stage, "request.json");
        using var output = new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, request); output.Flush(true); return requestPath;
    }

    static void ValidateStage(string stage)
    {
        stage = Paths.Root(stage);
        if (!string.Equals(Path.GetDirectoryName(stage), Paths.Root(CacheRoot), StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(stage), "N", out _))
            throw new IOException("Invalid app update workspace.");
    }

    public static Process StartWorker(string requestPath)
    {
        var stage = Path.GetDirectoryName(Path.GetFullPath(requestPath))!; ValidateStage(stage);
        var start = new ProcessStartInfo(Paths.Checked(stage, "updater.exe")) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--apply-app-update"); start.ArgumentList.Add(requestPath);
        return Process.Start(start) ?? throw new IOException("The app updater could not start.");
    }

    public static async Task ApplyAsync(string requestPath, Action<string> log)
    {
        requestPath = Paths.Root(requestPath);
        var stage = Path.GetDirectoryName(requestPath)!; ValidateStage(stage);
        if (Path.GetFileName(requestPath) != "request.json" || new FileInfo(requestPath).Length > 16384 ||
            !string.Equals(Environment.ProcessPath, Paths.Checked(stage, "updater.exe"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid app updater request.");
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestPath)) ?? throw new IOException("Empty app update request.");
        var target = Paths.Root(request.Target); var source = Paths.Checked(stage, "download.exe");
        if (!Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) || Paths.Overlap(stage, target) ||
            Hash(Environment.ProcessPath!) != request.OldHash || Hash(source) != request.NewHash || Hash(target) != request.OldHash)
            throw new IOException("The app or update files changed. Nothing was replaced.");
        var nextVersion = AppUpdateService.ParseVersion(request.Version);
        VerifyExecutable(source, nextVersion);
        var current = FileVersionInfo.GetVersionInfo(target);
        if (nextVersion <= new Version(current.FileMajorPart, current.FileMinorPart, current.FileBuildPart, current.FilePrivatePart))
            throw new IOException("The app update must be newer than the installed version.");
        Process? parent = null;
        try { parent = Process.GetProcessById(request.ParentId); } catch (ArgumentException) { }
        if (parent != null)
        {
            using (parent)
            {
                if (!parent.HasExited)
                {
                    try
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartedUtcTicks ||
                            !string.Equals(parent.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("The original app process could not be verified.");
                    }
                    catch (InvalidOperationException) when (parent.HasExited) { }
                    catch (System.ComponentModel.Win32Exception) when (parent.HasExited) { }
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    await parent.WaitForExitAsync(deadline.Token);
                }
            }
        }
        var backup = target + ".previous-" + Path.GetFileName(stage) + ".exe";
        log("Replacing app with " + request.Version + ". Previous app: " + backup);
        ApplyFiles(source, target, backup, request.OldHash, request.NewHash, path =>
        {
            using var restarted = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! })
                ?? throw new IOException("The updated app could not restart.");
        });
        log("App update installed and restarted. Previous version: " + backup);
    }

    internal static void ApplyFiles(string source, string target, string backup, string oldHash, string newHash, Action<string> launch)
    {
        source = Paths.Root(source); target = Paths.Root(target); backup = Paths.Root(backup);
        if (Paths.Overlap(source, target) || Paths.Overlap(backup, target) || File.Exists(backup) || Hash(source) != newHash || Hash(target) != oldHash)
            throw new IOException("App update files changed or overlap. Nothing was replaced.");
        var adjacent = target + ".update-" + Guid.NewGuid().ToString("N") + ".tmp";
        bool replaced = false, copied = false;
        try
        {
            File.Copy(source, adjacent, false); copied = true;
            if (Hash(adjacent) != newHash) throw new IOException("The staged app copy failed verification.");
            File.Replace(adjacent, target, backup); replaced = true;
            if (Hash(target) != newHash || Hash(backup) != oldHash) throw new IOException("The replacement app could not be verified.");
            launch(target);
        }
        catch (Exception failure)
        {
            if (replaced)
            {
                var failed = target + ".failed-" + Guid.NewGuid().ToString("N");
                File.Replace(backup, target, failed);
                try { launch(target); } catch { }
                throw new IOException("The app update failed. The previous app was restored. " + failure.Message, failure);
            }
            throw;
        }
        finally { if (copied && File.Exists(adjacent)) File.Delete(adjacent); }
    }
}
