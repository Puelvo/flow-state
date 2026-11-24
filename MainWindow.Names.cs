using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace FlowState;

// Hayvanların isimleri: her dostun sevimli bir varsayılan adı vardır; çift tıklayarak (ya da Dostlar'da üstüne tıklayarak) değiştirilir.
// İsimler yalnızca bu cihazda saklanır (AppData.AnimalNames).
public partial class MainWindow
{
    const int MaxName = 16;

    /// <param name="key">Bahçedeki kimlik: "zebra" ya da nadir dostta "golden:zebra", "baby:lion".</param>
    string NameOf(string key) => ThemeTable.NameOf(data, key);

    void RenameAnimal(string key)
    {
        string id = key[(key.IndexOf(':') + 1)..];
        var kind = key.StartsWith("golden:") ? SpecialKind.Golden : key.StartsWith("baby:") ? SpecialKind.Baby : (SpecialKind?)null;
        FrameworkElement portrait = kind is { } k ? SpecialImage(id, k, 64, locked: false) : SpriteImage(id, 64, silhouette: false, ZooPose.Happy);
        var name = AskName(NameOf(key), portrait);
        if (name == null) return;
        if (name.Length == 0) data.AnimalNames.Remove(key); else data.AnimalNames[key] = name;
        Store.Save(data);
        zooKey = "";
        UpdateAll();
    }

    /// <summary>İsim penceresi. null: vazgeçildi; boş: varsayılan isme dön.</summary>
    string? AskName(string current, FrameworkElement portrait)
    {
        var box = new TextBox { Text = current, MaxLength = MaxName, FontSize = 18, FontWeight = FontWeights.ExtraBold, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Kaydet", Style = (Style)FindResource("Primary"), Padding = new Thickness(22, 10, 22, 10), Margin = new Thickness(10, 0, 0, 0), IsDefault = true };
        var cancel = new Button { Content = "Vazgeç", Style = (Style)FindResource("Btn"), Padding = new Thickness(20, 10, 20, 10), IsCancel = true };
        var reset = new Button { Content = "İlk adı", Style = (Style)FindResource("Ghost"), ToolTip = "Boş bırakıp kaydetmekle aynı", HorizontalAlignment = HorizontalAlignment.Left };

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(portrait);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        titles.Children.Add(new TextBlock { Text = "Adı ne olsun?", FontSize = 20, FontWeight = FontWeights.Black, Foreground = Res("Text") });
        titles.Children.Add(new TextBlock { Text = $"En fazla {MaxName} harf", Style = (Style)FindResource("PageSub"), FontSize = 13 });
        head.Children.Add(titles);
        var buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
        DockPanel.SetDock(reset, Dock.Left); DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(reset); buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var stack = new StackPanel();
        stack.Children.Add(head); stack.Children.Add(box); stack.Children.Add(buttons);

        var win = new Window
        {
            Owner = this, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = FontFamily, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("Text"),
            Content = new Border { Style = (Style)FindResource("Card"), Effect = (Effect)FindResource("LiftShadow"), Width = 380, Padding = new Thickness(26, 24, 26, 20), Margin = new Thickness(34, 26, 34, 42), Child = stack }
        };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text.Trim(); win.DialogResult = true; };
        reset.Click += (_, _) => { result = ""; win.DialogResult = true; };
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true ? result : null;
    }
}
