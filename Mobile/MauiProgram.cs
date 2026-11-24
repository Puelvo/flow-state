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
				fonts.AddFont("Nunito_600SemiBold.ttf", "N6");
				fonts.AddFont("Nunito_700Bold.ttf", "N7");
				fonts.AddFont("Nunito_800ExtraBold.ttf", "N8");
				fonts.AddFont("Nunito_900Black.ttf", "N9");
			});

#if ANDROID
		builder.ConfigureMauiHandlers(h =>
		{
			h.AddHandler<PixelView, PixelViewHandler>();
			h.AddHandler<SceneView, SceneViewHandler>();
		});

		// Entry: alt çizgi yok (kutusunu çevresindeki yuvarlak Border çizer)
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("RoundedEntry", (handler, view) =>
		{
			handler.PlatformView.Background = null;
		});
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
