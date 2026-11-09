namespace FlowState.Mobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var page = new MainPage();
		var w = new Window(page);
		w.Resumed += (_, _) => page.OnResumed();
		w.Stopped += (_, _) => page.OnStopped();
		return w;
	}
}
