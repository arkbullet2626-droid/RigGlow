namespace RigGlow;

/// <summary>Tiny file logger: %AppData%\RigGlow\logs\rigglow-YYYYMMDD.log, kept for 7 days.</summary>
public static class Log
{
    static readonly object Gate = new();
    public static string Folder => Path.Combine(AppSettings.Folder, "logs");

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Warn(string msg) => Write("WARN ", msg);
    public static void Error(string msg) => Write("ERROR", msg);
    public static void Error(string msg, Exception ex) => Write("ERROR", msg + ": " + ex);

    static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, $"rigglow-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} {level} {msg}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }

    public static void Cleanup()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var f in Directory.GetFiles(Folder, "rigglow-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)) File.Delete(f);
        }
        catch { }
    }
}
