using System.Buffers.Binary;
using System.Net;
using System.Text;
using Hats.Core;

public static class NexusTests
{
    public static void Register(Action<string, Action> test)
    {
        test("Nexus uses the shared list when its optional GitHub file is absent", () => {
            using var service = new ExclusionService(new Missing());
            var snapshot = service.LatestAsync().GetAwaiter().GetResult();
            Require(snapshot.BlobSha.StartsWith("bundled:") && snapshot.Repository == "TR4UNEXUS/Nexus-USB-HATS");
            foreach (var path in NexusProtection.MandatoryPaths) Require(snapshot.PreservePaths.Contains(path, StringComparer.OrdinalIgnoreCase));
            Require(!snapshot.PreservePaths.Any(p => Paths.Under("atmosphere/contents/00ff0000636c6bff", Paths.Key(p))));
            Require(!snapshot.PreservePaths.Any(p => Paths.Under("atmosphere/kips/hoc.kip", Paths.Key(p))));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { service.LatestAsync(cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
        });
        test("Nexus pack discovery accepts new names and excludes desktop/app tools", () => {
            foreach (var name in new[] { "Nexus-1.12.2-N1-HOC.zip", "Nexus-1.12.2-N1-No-HOC.zip", "Nexus-USB-HATS.Full.zip", "Future-Resource-Pack.zip" }) Require(NexusProtection.IsPackAsset(name));
            foreach (var name in new[] { "Nexus-HATS-Desktop-Windows.zip", "Nexus-HATS-Installer-1.0.0.zip", "Nexus-USB-Formatter-Windows.zip", "Nexus-Manager-0.3.zip", "Nexus-source.zip" }) Require(!NexusProtection.IsPackAsset(name));
        });
        test("New system modules are identified from NPDM; applications and corrupt files stay protected", () => {
            var root = Scratch(); const string id = "4200000000abcd01"; var folder = Path.Combine(root, id); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "exefs.nsp");
            File.WriteAllBytes(path, Nsp(Convert.ToUInt64(id, 16), 0)); Require(SysmoduleIdentity.IsModule(folder, id));
            File.WriteAllBytes(path, Nsp(Convert.ToUInt64(id, 16), 1)); Require(!SysmoduleIdentity.IsModule(folder, id));
            File.WriteAllBytes(path, Nsp(0x4200000000abcd02, 0)); Require(!SysmoduleIdentity.IsModule(folder, id));
            var corrupt = Nsp(Convert.ToUInt64(id, 16), 0); BinaryPrimitives.WriteUInt64LittleEndian(corrupt.AsSpan(40), ulong.MaxValue);
            File.WriteAllBytes(path, corrupt); Require(!SysmoduleIdentity.IsModule(folder, id));
            File.WriteAllBytes(path, Nsp(0x0100ABCDEF000000, 0)); Require(!SysmoduleIdentity.IsModule(folder, "0100abcdef000000"));
        });
        test("HOC and new sysmodule resources update; USB indexes, Manager settings and games survive", () => {
            var root = Scratch(); var source = Path.Combine(root, "pack"); var sd = Path.Combine(root, "sd"); var backup = Path.Combine(root, "backup");
            Directory.CreateDirectory(Path.Combine(source, "switch")); Directory.CreateDirectory(Path.Combine(source, "bootloader")); Directory.CreateDirectory(sd);
            Write(source, "atmosphere/package3", "new firmware"); Write(sd, "atmosphere/package3", "old firmware");
            const string hoc = "atmosphere/contents/00ff0000636c6bff"; const string extra = "atmosphere/contents/4200000000abcd01";
            foreach (var relative in new[] { hoc + "/exefs.nsp", hoc + "/romfs/config.dat", "atmosphere/kips/hoc.kip", extra + "/romfs/resources.bin" }) { Write(source, relative, "new module"); Write(sd, relative, "old module"); }
            var nsp = Path.Combine(source, extra, "exefs.nsp"); File.WriteAllBytes(nsp, Nsp(0x4200000000abcd01, 0));
            Write(sd, extra + "/exefs.nsp", "old program");
            var protectedFiles = new[] { "atmosphere/usb_content/index.bin", "config/nexusmanager/network.ini", "atmosphere/contents/0100abcdef000000/romfs/mod.bin", "atmosphere/contents/0100abcdef000000/cheats/user.txt" };
            foreach (var relative in protectedFiles) { Write(source, relative, "incoming stale data"); Write(sd, relative, "keep user data"); }
            var plan = new Installer().Install(source, sd, backup, NexusProtection.BundledSnapshot().PreservePaths);
            Require(plan.Sysmodules.Contains("00ff0000636c6bff") && plan.Sysmodules.Contains("4200000000abcd01"));
            foreach (var relative in protectedFiles) { Require(File.ReadAllText(Path.Combine(sd, relative)) == "keep user data"); Require(!File.Exists(Path.Combine(backup, relative))); }
            foreach (var relative in new[] { hoc + "/exefs.nsp", hoc + "/romfs/config.dat", "atmosphere/kips/hoc.kip", extra + "/romfs/resources.bin" }) { Require(File.ReadAllText(Path.Combine(sd, relative)) == "new module"); Require(File.ReadAllText(Path.Combine(backup, relative)) == "old module"); }
        });
        test("USB maps, HOME badges and recovery journals survive upgrades even with an empty remote list", () => LibraryUpgrade(false));
        test("Failed upgrades restore firmware without touching USB maps or their repair state", () => LibraryUpgrade(true));
        test("A fresh SD card never receives someone else's USB library data from a HATS pack", () => {
            var root = Scratch(); var source = Path.Combine(root, "pack"); var sd = Path.Combine(root, "sd");
            Directory.CreateDirectory(Path.Combine(source, "switch")); Directory.CreateDirectory(Path.Combine(source, "bootloader")); Directory.CreateDirectory(sd);
            Write(source, "atmosphere/package3", "new firmware");
            foreach (var p in LibraryFiles) Write(source, p, "foreign library");
            new Installer().Install(source, sd, Path.Combine(root, "backup"), []);
            foreach (var p in LibraryFiles) Require(!File.Exists(Path.Combine(sd, p)));
            Require(File.ReadAllText(Path.Combine(sd, "atmosphere/package3")) == "new firmware");
        });
    }
    static readonly string[] LibraryFiles = [
        "atmosphere/usb_content/title_map.bin", "atmosphere/usb_content/title_content_map.bin",
        "atmosphere/usb_content/content_locations.bin", "atmosphere/usb_content/title_map.bin.bak",
        "atmosphere/usb_content/title_content_map.bin.tmp", "atmosphere/usb_content/future/nested.bin",
        "atmosphere/usb_badges/0100000000010000-0.ubg", "atmosphere/usb_badges/disabled",
        "atmosphere/usb_content-repair.pending", "atmosphere/usb_content-repair-0123456789abcdef/journal.bin",
        "atmosphere/usb_content-repair-0123456789abcdef/before/title_map.bin",
        "atmosphere/usb_content-recovery-0123456789abcdef/quarantined.bin"
    ];
    static void LibraryUpgrade(bool fail)
    {
        var root = Scratch(); var source = Path.Combine(root, "pack"); var sd = Path.Combine(root, "sd"); var backup = Path.Combine(root, "backup");
        Directory.CreateDirectory(Path.Combine(source, "switch")); Directory.CreateDirectory(Path.Combine(source, "bootloader"));
        Write(source, "atmosphere/package3", "new firmware"); Write(sd, "atmosphere/package3", "old firmware");
        foreach (var p in LibraryFiles) { Write(source, p, "stale pack data"); Write(sd, p, "current USB library\0\u00ff"); }
        const string absent = "atmosphere/usb_content-recovery-ffffffffffffffff/new.bin";
        Write(source, absent, "foreign recovery");
        var plan = InstallPolicy.Plan(source, sd, []);
        foreach (var p in LibraryFiles.Append(absent))
            Require(!plan.Copy.Contains(p) && !plan.Replace.Any(r => Paths.Under(Paths.Key(p), Paths.Key(r))));
        var installer = new Installer { CopyFile = (from, to) => { VerifiedFiles.Copy(from, to); if (fail) throw new IOException("Injected failure after write"); } };
        bool failed = false;
        try { installer.Install(source, sd, backup, []); }
        catch (IOException e) when (fail && e.Message.Contains("previous files were restored")) { failed = true; }
        Require(failed == fail);
        foreach (var p in LibraryFiles) { Require(File.ReadAllText(Path.Combine(sd, p)) == "current USB library\0\u00ff"); Require(!File.Exists(Path.Combine(backup, p))); }
        Require(!File.Exists(Path.Combine(sd, absent)));
        Require(File.ReadAllText(Path.Combine(sd, "atmosphere/package3")) == (fail ? "old firmware" : "new firmware"));
    }
    static void Require(bool ok) { if (!ok) throw new Exception("Nexus regression check failed"); }
    static string Scratch() { var root = Environment.GetEnvironmentVariable("HATS_TEST_ROOT") ?? Path.GetTempPath(); var path = Path.Combine(root, "nexus-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    static void Write(string root, string relative, string data) { var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, data); }
    sealed class Missing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancel) { cancel.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
    }
    static byte[] Nsp(ulong id, uint type)
    {
        var npdm = new byte[0xc4]; "META"u8.CopyTo(npdm); Put32(npdm, 0x70, 0x80); Put32(npdm, 0x74, 0x44);
        "ACI0"u8.CopyTo(npdm.AsSpan(0x80)); BinaryPrimitives.WriteUInt64LittleEndian(npdm.AsSpan(0x90), id);
        Put32(npdm, 0xb0, 0x40); Put32(npdm, 0xb4, 4); Put32(npdm, 0xc0, 0x1fff | (type << 14));
        var names = Encoding.ASCII.GetBytes("main\0main.npdm\0"); int start = 16 + 48 + names.Length; var pfs = new byte[start + 0x100 + npdm.Length];
        "PFS0"u8.CopyTo(pfs); Put32(pfs, 4, 2); Put32(pfs, 8, (uint)names.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(pfs.AsSpan(24), 0x100);
        BinaryPrimitives.WriteUInt64LittleEndian(pfs.AsSpan(40), 0x100); BinaryPrimitives.WriteUInt64LittleEndian(pfs.AsSpan(48), (ulong)npdm.Length); Put32(pfs, 56, 5);
        names.CopyTo(pfs, 64); "NSO0"u8.CopyTo(pfs.AsSpan(start)); npdm.CopyTo(pfs, start + 0x100); return pfs;
    }
    static void Put32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
}
