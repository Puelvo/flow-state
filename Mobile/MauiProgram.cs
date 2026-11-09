using Microsoft.Extensions.Logging;

namespace FlowState.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if ANDROID
		// Entry: alt çizgiyi kaldır, yuvarlatılmış köşeli kutu yap
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("RoundedEntry", (handler, view) =>
		{
			var d = handler.PlatformView.Context!.Resources!.DisplayMetrics!.Density;
			var bg = new Android.Graphics.Drawables.GradientDrawable();
			bg.SetColor(Android.Graphics.Color.Argb(179, 28, 36, 71));
			bg.SetCornerRadius(14 * d);
			handler.PlatformView.Background = bg;
			handler.PlatformView.SetPadding((int)(14 * d), (int)(10 * d), (int)(14 * d), (int)(10 * d));
		});
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
