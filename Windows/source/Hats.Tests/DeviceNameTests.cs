using System.Buffers.Binary;
using System.Text;
using Hats.Core;

public static class DeviceNameTests
{
    public static void Register(Action<string, Action> test, string[] args)
    {
        test("Hekate USB descriptor identifies Switch and preserves a named volume", () => {
            var info = StorageDeviceInfo.ParseDescriptor(Descriptor("hekate  ", "SD RAW", 7));
            Check(info is { IsHekateUsb: true, Product: "SD RAW" }, "Hekate descriptor not recognised");
            var drive = new SdVolume("D:\\", new(1, "MY SWITCH", "FAT32"), 128L << 30, 100L << 30, info);
            Check(drive.ToString().Contains("Nintendo Switch (Hekate USB)") && drive.ToString().Contains("MY SWITCH"), "Device type or volume label missing");
        });
        test("Named cards keep their real label and show the reader hardware separately", () => {
            var reader = StorageDeviceInfo.ParseDescriptor(Descriptor("Generic", "SD Card Reader", 7));
            var drive = new SdVolume("E:\\", new(2, "MY SD CARD", "exFAT"), 64L << 30, 50L << 30, reader);
            Check(drive.DisplayName == "MY SD CARD", "Reader name replaced volume label");
            Check(drive.DeviceDescription.Contains("Generic SD Card Reader"), "Reader identity missing");
            Check((drive with { Identity = drive.Identity with { Label = "" } }).DisplayName == "Generic SD Card Reader", "Unnamed card did not fall back to device name");
        });
        test("Volume labels and unrelated hardware cannot masquerade as Hekate", () => {
            var drive = new SdVolume("E:\\", new(2, "hekate", "FAT32"), 64L << 30, 50L << 30, new("Generic", "USB Drive", 7));
            Check(drive.Device?.IsHekateUsb == false && !drive.ToString().Contains("Nintendo Switch"), "Volume label caused false Hekate identification");
            Check(!new StorageDeviceInfo("hekate", "SD RAW", 11).IsHekateUsb, "Non-USB device identified as USB Hekate");
        });
        test("Missing hardware metadata retains labels and gives an honest unnamed fallback", () => {
            var drive = new SdVolume("E:\\", new(2, "HOLIDAY", "exFAT"), 64L << 30, 50L << 30);
            Check(drive.DisplayName == "HOLIDAY", "Missing descriptor hid the label");
            Check((drive with { Identity = drive.Identity with { Label = "" } }).DisplayName == "Removable drive (no label)", "Incorrect missing-label fallback");
        });
        test("Malformed storage descriptors cannot overrun or inject control characters", () => {
            Check(StorageDeviceInfo.ParseDescriptor(new byte[35]) == null, "Truncated header accepted");
            var truncated = Descriptor("hekate", "SD RAW", 7);
            Check(StorageDeviceInfo.ParseDescriptor(truncated.AsSpan(0, truncated.Length - 1)) == null, "Truncated body accepted");
            foreach (var offset in new uint[] { 1, 35, uint.MaxValue }) {
                var invalid = Descriptor("hekate", "SD RAW", 7); BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(12), offset);
                Check(StorageDeviceInfo.ParseDescriptor(invalid) == null, "Invalid string offset accepted");
            }
            var unterminated = Descriptor("hekate", "SD RAW", 7); unterminated[^1] = (byte)'x';
            Check(StorageDeviceInfo.ParseDescriptor(unterminated) == null, "Unterminated product accepted");
            Check(StorageDeviceInfo.ParseDescriptor(Descriptor("hekate\nERROR", "SD RAW", 7)) == null, "Control character accepted");
        });
        int expected = Array.IndexOf(args, "--expect-hekate");
        if (expected >= 0 && expected + 1 < args.Length)
            test("Connected Hekate card is identified from read-only device metadata", () => {
                var drive = WindowsVolumes.Scan().Single(v => v.Root.Equals(args[expected + 1], StringComparison.OrdinalIgnoreCase));
                if (drive.Device is not { IsHekateUsb: true } device) throw new Exception("Connected Hekate device was not identified");
                Console.WriteLine("  Verified Hekate device: " + drive + "; hardware=" + device.HardwareName);
            });
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static byte[] Descriptor(string vendor, string product, uint bus)
    {
        var a = Encoding.ASCII.GetBytes(vendor + "\0"); var b = Encoding.ASCII.GetBytes(product + "\0");
        var bytes = new byte[36 + a.Length + b.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 36);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)(36 + a.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), bus);
        a.CopyTo(bytes, 36); b.CopyTo(bytes, 36 + a.Length);
        return bytes;
    }
}
