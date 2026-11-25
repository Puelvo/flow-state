using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FlowState;

// Ayarlar → Senkronizasyon: oturum açıkken şifre değiştirme. Eski şifre gerekmez (oturum zaten kullanıcının);
// Supabase ek doğrulama isterse e-postaya gelen kod sorulur. Şifre hiçbir yere kaydedilmez.
public partial class MainWindow
{
    const int MinPassword = 8;

    void SyncPasswordChange_Click(object sender, RoutedEventArgs e) => BuildPasswordDialog()?.ShowDialog();

    Window? BuildPasswordDialog()
    {
        if (sync is not { SignedIn: true }) return null;
        var pw = new PasswordBox();
        var again = new PasswordBox();
        var code = new TextBox { MaxLength = 10 };
        TextBlock Label(string t) => new() { Text = t, FontSize = 13, FontWeight = FontWeights.ExtraBold, Foreground = Res("Muted"), Margin = new Thickness(4, 12, 0, 6) };
        var codeBox = new StackPanel { Visibility = Visibility.Collapsed };
        codeBox.Children.Add(Label("E-postana gelen kod"));
        codeBox.Children.Add(code);
        var msg = new TextBlock { FontSize = 13, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 12, 0, 0), Visibility = Visibility.Collapsed };
        var ok = new Button { Content = "Kaydet", Style = (Style)FindResource("Primary"), Padding = new Thickness(22, 10, 22, 10), Margin = new Thickness(10, 0, 0, 0), IsDefault = true };
        var cancel = new Button { Content = "Vazgeç", Style = (Style)FindResource("Btn"), Padding = new Thickness(20, 10, 20, 10), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Şifre değiştir", FontSize = 20, FontWeight = FontWeights.Black, Foreground = Res("Text") });
        stack.Children.Add(new TextBlock { Text = sync.Email, Style = (Style)FindResource("PageSub"), FontSize = 13 });
        stack.Children.Add(Label($"Yeni şifre (en az {MinPassword} karakter)")); stack.Children.Add(pw);
        stack.Children.Add(Label("Yeni şifre (tekrar)")); stack.Children.Add(again);
        stack.Children.Add(codeBox); stack.Children.Add(msg); stack.Children.Add(buttons);
        var win = new Window
        {
            Owner = this, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = FontFamily, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("Text"),
            Content = new Border { Style = (Style)FindResource("Card"), Effect = (Effect)FindResource("LiftShadow"), Width = 400, Padding = new Thickness(26, 24, 26, 20), Margin = new Thickness(34, 26, 34, 42), Child = stack }
        };
        void Say(string t, string brush) { msg.Text = t; msg.Foreground = Res(brush); msg.Visibility = Visibility.Visible; }
        bool done = false;
        ok.Click += async (_, _) =>
        {
            if (done) { win.Close(); return; }
            if (pw.Password.Length < MinPassword) { Say($"Şifre en az {MinPassword} karakter olmalı.", "Danger"); return; }
            if (pw.Password != again.Password) { Say("İki şifre aynı değil.", "Danger"); return; }
            ok.IsEnabled = cancel.IsEnabled = false;
            Say("Kaydediliyor…", "Muted");
            var (err, needsCode) = await sync.ChangePasswordAsync(pw.Password, code.Text.Trim());
            ok.IsEnabled = cancel.IsEnabled = true;
            if (needsCode) { codeBox.Visibility = Visibility.Visible; code.Focus(); }
            if (err != null) { Say(err, needsCode ? "Muted" : "Danger"); return; }
            done = true;
            pw.Clear(); again.Clear(); code.Clear();
            pw.IsEnabled = again.IsEnabled = code.IsEnabled = false;
            cancel.Visibility = Visibility.Collapsed;
            ok.Content = "Tamam";
            Say("Şifren değişti. Telefonda yeni şifrenle giriş yapabilirsin.", "Good");
        };
        win.Loaded += (_, _) => pw.Focus();
        return win;
    }
}
