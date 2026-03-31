namespace SportSplitter;

static class Program
{
    [STAThread]
    static void Main()
    {
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
