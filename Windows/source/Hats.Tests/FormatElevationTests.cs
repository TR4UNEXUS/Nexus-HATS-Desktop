using System.Diagnostics;
using Hats.Core;

static class FormatElevationTests
{
    static readonly SdVolume Fixture = new("Z:\\", new(1234, "TEST ONLY", "FAT32"), 4L << 30, 3L << 30);
    static void Assert(bool value) { if (!value) throw new Exception("Format handoff assertion failed"); }
    static Process Launch(string token, int parent, string mode)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--format-worker-test", token, parent.ToString(), mode }) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }
    public static Task<int> RunWorker(string[] args) => FormatElevation.RunHelperAsync(args[1], int.Parse(args[2]), (volume, progress) =>
    {
        Assert(volume == Fixture);
        if (args[3] == "progress-disconnect") for (int i = 0; i < 200; i++) progress(i / 2d);
        Fat32Formatter.Format(new FakeDisk { Failure = args[3] == "flush-failure" ? "flush" : "" }, progress);
    }, () => args[3] != "denied");

    public static void Register(Action<string, Action> test)
    {
        test("Format worker handoff streams progress and confirms success using a fake card", () => {
            var progress = new List<double>(); int validations = 0;
            FormatElevation.RunWorkerAsync(Fixture, progress.Add, (token, parent) => Launch(token, parent, "success"),
                volume => { Assert(volume == Fixture); validations++; }).GetAwaiter().GetResult();
            Assert(validations == 2 && progress.Count > 3 && progress.Last() == 100);
        });
        test("Format worker returns final flush errors without reporting success", () => {
            var progress = new List<double>();
            try {
                FormatElevation.RunWorkerAsync(Fixture, progress.Add, (token, parent) => Launch(token, parent, "flush-failure"), _ => { }).GetAwaiter().GetResult();
                throw new Exception("Expected failure");
            } catch (IOException e) { Assert(e.Message.Contains("device is not ready") && !progress.Contains(100)); }
        });
        test("Progress-channel failure lets the worker finish without blocking on a full pipe", () => {
            try {
                FormatElevation.RunWorkerAsync(Fixture, _ => throw new InvalidOperationException("View disconnected"),
                    (token, parent) => Launch(token, parent, "progress-disconnect"), _ => { }).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                throw new Exception("Expected progress failure");
            } catch (InvalidOperationException e) { Assert(e.Message == "View disconnected"); }
        });
        test("Format worker refuses missing administrator permission before receiving a card", () => {
            try {
                FormatElevation.RunWorkerAsync(Fixture, _ => throw new Exception("Unexpected progress"), (token, parent) => Launch(token, parent, "denied"), _ => { }).GetAwaiter().GetResult();
                throw new Exception("Expected failure");
            } catch (IOException e) { Assert(e.Message.Contains("No format was performed")); }
        });
        test("Format worker rejects a different parent process before accepting a request", () => {
            try {
                FormatElevation.RunWorkerAsync(Fixture, _ => throw new Exception("Unexpected progress"), (token, parent) => Launch(token, parent + 1, "success"), _ => { }).GetAwaiter().GetResult();
                throw new Exception("Expected failure");
            } catch (IOException) { }
        });
        test("Card identity is checked again after permission approval and before worker writes", () => {
            int validations = 0;
            try {
                FormatElevation.RunWorkerAsync(Fixture, _ => throw new Exception("Unexpected progress"), (token, parent) => Launch(token, parent, "success"),
                    _ => { if (++validations == 2) throw new IOException("Card changed"); }).GetAwaiter().GetResult();
                throw new Exception("Expected failure");
            } catch (IOException e) { Assert(e.Message == "Card changed" && validations == 2); }
        });
        test("Declining elevation cancels without sending a format request", () => {
            try {
                FormatElevation.RunWorkerAsync(Fixture, _ => throw new Exception("Unexpected progress"), (_, _) => throw new OperationCanceledException(), _ => { }).GetAwaiter().GetResult();
                throw new Exception("Expected cancellation");
            } catch (OperationCanceledException) { }
        });
        test("Invalid helper token is rejected without connecting or writing", () => {
            Assert(FormatElevation.RunHelperAsync("invalid/name", Environment.ProcessId,
                (_, _) => throw new Exception("Unexpected format"), () => true).GetAwaiter().GetResult() == 1);
        });
    }
}
