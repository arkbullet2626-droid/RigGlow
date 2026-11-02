namespace RigGlow;

static class Program
{
    const string MutexName = "RigGlow_SingleInstance_8F2A";
    const string ShowEventName = "RigGlow_Show_8F2A";
    const string ExitEventName = "RigGlow_Exit_8F2A";

    static HardwareMonitor _hw;

    [STAThread]
    static int Main(string[] args)
    {
        bool wantExit = args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));
        bool hiddenArg = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, MutexName, out bool firstInstance);
        if (!firstInstance)
        {
            // Already running: ask that copy to show itself (or to exit, for the installer).
            Signal(wantExit ? ExitEventName : ShowEventName);
            if (wantExit) WaitForExit(mutex);
            return 0;
        }
        if (wantExit) return 0;   // nothing to close

        ApplicationConfiguration.Initialize();
        Log.Cleanup();
        Log.Info($"==== {AppInfo.Name} {AppInfo.VersionText} starting (admin: {SetupCheck.IsAdmin()}) ====");

        var settings = AppSettings.Load();
        bool firstRun = !settings.AcceptedNotice;

        if (firstRun)
        {
            using var notice = new NoticeForm();
            if (notice.ShowDialog() != DialogResult.OK) return 0;
            settings.AcceptedNotice = true;
            settings.LastPage = "Setup check";
            settings.Save();
        }

        _hw = new HardwareMonitor(settings);
        var engine = new LightingEngine(settings);

        // Whatever happens, give the fans back to the motherboard when we go away.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _hw.RestoreDefaults();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception", e.ExceptionObject as Exception ?? new Exception("unknown"));
            _hw.RestoreDefaults();
        };
        Application.ThreadException += (_, e) =>
        {
            Log.Error("UI exception", e.Exception);
            MessageBox.Show("Something went wrong: " + e.Exception.Message + "\n\nDetails were saved to the log (Settings > Open log folder).",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };

        _hw.Start();
        engine.Start();

        var form = new MainForm(settings, _hw, engine, hiddenArg && !firstRun);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        var listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { showEvent, exitEvent };
            while (true)
            {
                int which;
                try { which = WaitHandle.WaitAny(handles); } catch { return; }
                try
                {
                    if (which == 0) form.BeginInvoke(new Action(form.ShowFromTray));
                    else { form.BeginInvoke(new Action(form.ExitApp)); return; }
                }
                catch { return; }
            }
        }) { IsBackground = true, Name = "RigGlow instance listener" };
        listener.Start();

        Application.Run(form);

        engine.Dispose();
        _hw.Dispose();
        settings.Save();
        Log.Info("Exited cleanly, fans handed back to the motherboard.");
        return 0;
    }

    static void Signal(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name);
            ev.Set();
        }
        catch { }
    }

    static void WaitForExit(Mutex mutex)
    {
        // The running copy releases the mutex when it exits; wait up to 10 seconds.
        try
        {
            if (mutex.WaitOne(TimeSpan.FromSeconds(10))) mutex.ReleaseMutex();
        }
        catch (AbandonedMutexException) { }
        catch { }
    }
}
