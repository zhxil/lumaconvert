using Microsoft.UI.Xaml;

namespace LightConvert.WinUI;

public partial class App : Application
{
    private Window? window;
    public App()
    {
        InitializeComponent();
        if (Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
            DebugSettings.XamlResourceReferenceFailed += (_, args) =>
            {
                try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "xaml-diagnostics.log"), args.Message + "\n"); } catch { }
            };
        }
        UnhandledException += (_, e) =>
        {
            var details = $"{DateTimeOffset.Now:O}\n{e.Exception}\n";
            foreach (System.Collections.DictionaryEntry entry in e.Exception.Data) details += $"{entry.Key}: {entry.Value}\n";
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightConvert");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "crash.log"), details);
            }
            catch
            {
                try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), details); }
                catch { }
            }
            if (Environment.GetCommandLineArgs().Contains("--smoke-test"))
            {
                e.Handled = true;
                Environment.Exit(1);
            }
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
}
