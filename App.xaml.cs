using System.IO;
using System.Windows;

namespace FlowState;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Ekran görüntüsü modunda (CI) beklenmeyen hata pencerede takılı kalmasın: dosyaya yaz ve kapan.
        if (Environment.GetEnvironmentVariable("FLOWSTATE_SHOTS") is { Length: > 0 } dir)
        {
            DispatcherUnhandledException += (_, ev) =>
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "hata.txt"), ev.Exception + "\n\n");
                ev.Handled = true;
                Shutdown(1);
            };
        }
        base.OnStartup(e);
    }
}
