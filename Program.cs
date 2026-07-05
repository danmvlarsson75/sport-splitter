using Velopack;

namespace SportSplitter;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Must run before anything else: handles Velopack install/update hooks
        // (the exe is re-invoked with special arguments during those phases).
        VelopackApp.Build().Run();

        using var mutex = new System.Threading.Mutex(true, "SportSplitter_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Sport Splitter is already running.\nCheck the system tray.", "Sport Splitter");
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
