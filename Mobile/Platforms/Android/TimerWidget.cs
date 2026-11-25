using System.Globalization;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Widget;
using AG = Android.Graphics;
using Microsoft.Maui.Storage;

namespace FlowState.Mobile;

/// <summary>Ana ekran widget'ı: uygulamadaki piksel bahçe (güneş saat, hayvanlar, jeep), canlı süre, ders adı ve başlat/duraklat/bitir düğmeleri.</summary>
[BroadcastReceiver(Label = "Flow State", Exported = true)]
[IntentFilter(new[] { "android.appwidget.action.APPWIDGET_UPDATE" })]
[MetaData("android.appwidget.provider", Resource = "@xml/timer_widget_info")]
public class TimerWidgetProvider : AppWidgetProvider
{
	public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds) => TimerWidget.Refresh();

	public override void OnReceive(Context? context, Intent? intent)
	{
		base.OnReceive(context, intent);
		if (intent?.Action == TimerWidget.ActTick) TimerWidget.Refresh();
	}

	public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, Bundle? newOptions) => TimerWidget.Refresh();

	public override void OnDeleted(Context? context, int[]? appWidgetIds)
	{
		base.OnDeleted(context, appWidgetIds);
		TimerWidget.Forget(appWidgetIds);
	}

	public override void OnDisabled(Context? context)
	{
		base.OnDisabled(context);
		TimerWidget.CancelTick();
	}
}

public static class TimerWidget
{
	public const string ActTick = "com.puelvo.flowstate.WIDGET_TICK";
	const long TickMs = 30_000;

	static Context Ctx => Android.App.Application.Context;

	static int Id(string name) => Ctx.Resources!.GetIdentifier(name, "id", Ctx.PackageName);
	static int Layout(string name) => Ctx.Resources!.GetIdentifier(name, "layout", Ctx.PackageName);

	static PendingIntent Broadcast(string action, int code)
	{
		var i = new Intent(Ctx, typeof(TimerActionReceiver)).SetAction(action);
		return PendingIntent.GetBroadcast(Ctx, code, i, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
	}

	static PendingIntent TickIntent()
	{
		var i = new Intent(Ctx, typeof(TimerWidgetProvider)).SetAction(ActTick);
		return PendingIntent.GetBroadcast(Ctx, 10, i, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
	}

	public static void CancelTick()
	{
		try { (Ctx.GetSystemService(Context.AlarmService) as AlarmManager)?.Cancel(TickIntent()); } catch { }
	}

	/// <summary>Kayıtlı zamanlayıcı ve verilerden widget'ı yeniden çizer. Uygulama kapalıyken de çağrılabilir.</summary>
	public static void Refresh()
	{
		try
		{
			var ctx = Ctx;
			var mgr = AppWidgetManager.GetInstance(ctx)!;
			var ids = mgr.GetAppWidgetIds(new ComponentName(ctx, Java.Lang.Class.FromType(typeof(TimerWidgetProvider))));
			if (ids == null || ids.Length == 0) { CancelTick(); return; }

			Store.Dir = FileSystem.AppDataDirectory;
			var data = Store.Load();

			// Zamanlayıcı durumu (MainPage.PersistTimer ile aynı biçim)
			Course? course = null; bool active = false, running = false; double elapsed = 0;
			var p = Preferences.Get("run", "").Split('|');
			if (p.Length >= 4 && data.Courses.FirstOrDefault(c => c.Id == p[0]) is { } rc)
			{
				course = rc; active = true;
				var acc = double.Parse(p[2], CultureInfo.InvariantCulture);
				var t = long.Parse(p[3]);
				running = t != 0;
				elapsed = acc + (running ? (DateTime.UtcNow - new DateTime(t, DateTimeKind.Utc)).TotalSeconds : 0);
			}
			course ??= data.Courses.FirstOrDefault(c => c.Id == Preferences.Get("lastCourse", "")) ?? data.Courses.FirstOrDefault();

			double todaySec = data.Sessions.Where(s => s.Start >= DateTime.Today).Sum(s => (double)s.Seconds);
			if (active && new DateTime(long.Parse(p[1])) >= DateTime.Today) todaySec += elapsed;

			uint accent = Accent(data);
			int target = active && p.Length > 4 && p[4].Length > 0 && p[4] == data.CountdownRunId ? data.CountdownSeconds : 0;
			var palette = DayCycle.At(DateTime.Now, DayCycle.Parse(data.GardenTheme));
			double lit = !active ? 0 : target > 0 ? Math.Min(1, elapsed / target) * 60 : elapsed % 3600 / 60;
			bool nightSky = DayCycle.IsNight(palette);
			bool up = target > 0 && elapsed >= target;                    // geri sayım doldu, seçim bekleniyor

			foreach (var id in ids)
			{
				// Yazılar sığsın diye yaklaşık boyut: dikey ekranda genişlik en az, yükseklik en çok.
				var opt = mgr.GetAppWidgetOptions(id);
				int wDp = Math.Clamp(opt?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth, 300) ?? 300, 120, 460);
				int hDp = Math.Clamp(opt?.GetInt(AppWidgetManager.OptionAppwidgetMaxHeight, 130) ?? 130, 48, 420);
				bool small = hDp < 92;

				var v = new RemoteViews(ctx.PackageName, Layout(small ? "widget_timer_small" : "widget_timer"));
				v.SetImageViewBitmap(Id("w_scene"), SceneBitmap(id, wDp, hDp, data, palette, active, running, lit));

				uint fg = nightSky ? 0xFFFFFFFF : 0xFF3B2C25, sub = nightSky ? 0xFFC9D3FF : 0xFF8A756A;
				v.SetInt(Id("w_info"), "setBackgroundResource", Drawable(nightSky ? "widget_glass_dark" : "widget_glass"));
				v.SetTextColor(Id("w_course"), Col(fg)); v.SetTextColor(Id("w_time"), Col(fg)); v.SetTextColor(Id("w_status"), Col(sub));
				if (OperatingSystem.IsAndroidVersionAtLeast(24)) v.SetChronometerCountDown(Id("w_time"), target > 0);
				long now = SystemClock.ElapsedRealtime();
				v.SetChronometer(Id("w_time"), up ? now : target > 0 ? now + (long)((target - elapsed) * 1000) : now - (long)(elapsed * 1000), null, running && !up);
				v.SetTextViewText(Id("w_course"), course?.Name ?? "Ders ekle");
				v.SetTextViewText(Id("w_status"), !active ? $"Bugün {Fmt(todaySec)}" : up ? "Süre doldu · devam mı?" : running ? "Odaklanıyorsun" : "Mola");

				v.SetImageViewBitmap(Id("w_toggle"), ButtonBitmap(active && running && !up ? "pause" : "play", accent, OnAccent(accent), 28, 20));
				v.SetImageViewBitmap(Id("w_stop"), ButtonBitmap("stop", 0xFFFDE5E7, 0xFFEF5D6C, 22, 20));
				v.SetViewVisibility(Id("w_stop"), active ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone);
				v.SetOnClickPendingIntent(Id("w_toggle"), up ? Broadcast(TimerNotifier.ActStay, 23) : Broadcast(TimerNotifier.ActToggle, 21));
				v.SetOnClickPendingIntent(Id("w_stop"), Broadcast(TimerNotifier.ActFinish, 22));
				var open = new Intent(ctx, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
				v.SetOnClickPendingIntent(Id("w_root"), PendingIntent.GetActivity(ctx, 20, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
				mgr.UpdateAppWidget(id, v);
			}

			// Çalışırken ışınlar ve jeep ilerlesin diye yarım dakikada bir yenile.
			var am = ctx.GetSystemService(Context.AlarmService) as AlarmManager;
			if (running) am?.SetAndAllowWhileIdle(AlarmType.ElapsedRealtime, SystemClock.ElapsedRealtime() + TickMs, TickIntent());
			else CancelTick();
		}
		catch { /* widget hatası uygulamayı etkilemesin */ }
	}

	static string Fmt(double sec)
	{
		int s = (int)sec, h = s / 3600, m = s % 3600 / 60;
		return h > 0 ? $"{h} sa {m:00} dk" : m > 0 ? $"{m} dk" : $"{s} sn";
	}

	// ---------- çizimler ----------

	static int Drawable(string name) => Ctx.Resources!.GetIdentifier(name, "drawable", Ctx.PackageName);
	internal static AG.Color Col(uint c) => new(unchecked((int)c));

	internal static uint Accent(AppData data)
	{
		try { return unchecked((uint)AG.Color.ParseColor(data.WidgetColor).ToArgb()); } catch { return 0xFF5B8CFF; }
	}

	internal static uint OnAccent(uint a) => (0.299 * (a >> 16 & 255) + 0.587 * (a >> 8 & 255) + 0.114 * (a & 255)) / 255 > 0.72 ? 0xFF3B2C25 : 0xFFFFFFFF;

	// Widget başına bir sahne: süreç yaşadıkça hayvanlar ve jeep kaldıkları yerden sürer.
	static readonly Dictionary<int, (PixelScene Scene, DateTime At)> scenes = new();

	internal static void Forget(int[]? ids) { foreach (var id in ids ?? Array.Empty<int>()) scenes.Remove(id); }

	static AG.Bitmap SceneBitmap(int id, int w, int h, AppData data, SkyPalette palette, bool active, bool running, double lit)
	{
		bool fresh = !scenes.TryGetValue(id, out var e);
		var s = fresh ? new PixelScene(Sprites.Load) { Z = 1, CloudZ = 1 } : e.Scene;
		WidgetScene.Compose(s, w, h, data, palette, active, running, lit, fresh ? 20 : (DateTime.UtcNow - e.At).TotalSeconds);
		scenes[id] = (s, DateTime.UtcNow);
		return Sprites.ToBitmap(s.Buf, s.W, s.H, w * h > 60000 ? 2 : 3);
	}

	/// <summary>Güneş saat (bildirimde): disk ve ışınlar.</summary>
	internal static AG.Bitmap SunBitmap(SkyPalette p, bool active, double lit)
	{
		var rays = PixelArt.SunRays(p, active, lit); var disc = PixelArt.SunDisc(p);
		var px = (uint[])rays.Px.Clone();
		for (int i = 0; i < px.Length; i++) if (disc.Px[i] != 0) px[i] = disc.Px[i];
		return Sprites.ToBitmap(px, rays.W, rays.H, 2);
	}

	internal static AG.Bitmap ButtonBitmap(string icon, uint face, uint fg, int w, int h)
	{
		var b = PixelArt.Button(w, h, face, PixelArt.Icon(icon, fg));
		return Sprites.ToBitmap(b.Px, b.W, b.H, 6);
	}
}
