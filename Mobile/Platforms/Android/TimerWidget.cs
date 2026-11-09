using System.Globalization;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Widget;
using AG = Android.Graphics;
using Microsoft.Maui.Storage;

namespace FlowState.Mobile;

/// <summary>Ana ekran widget'ı: geçişli halka, canlı süre, ders adı, bugünkü toplam ve başlat/duraklat/bitir düğmeleri.</summary>
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

			AG.Color accent;
			try { accent = AG.Color.ParseColor(data.WidgetColor); } catch { accent = AG.Color.ParseColor("#5B8CFF"); }

			var v = new RemoteViews(ctx.PackageName, Layout("widget_timer"));
			v.SetImageViewBitmap(Id("w_ring"), RingBitmap(active ? elapsed % 3600 / 3600.0 : 0, accent));
			long baseMs = SystemClock.ElapsedRealtime() - (long)(elapsed * 1000);
			v.SetChronometer(Id("w_time"), baseMs, null, running);
			v.SetTextViewText(Id("w_course"), course?.Name ?? "Ders ekle");
			v.SetTextViewText(Id("w_status"), !active ? "Başlamaya hazır" : running ? "Odaklanıyorsun" : "Duraklatıldı");
			v.SetTextViewText(Id("w_today"), $"Bugün {Fmt(todaySec)}");

			v.SetImageViewBitmap(Id("w_toggle"), ButtonBitmap(active && running ? 1 : 0, accent));
			v.SetImageViewBitmap(Id("w_stop"), ButtonBitmap(2, accent));
			v.SetViewVisibility(Id("w_stop"), active ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone);
			v.SetOnClickPendingIntent(Id("w_toggle"), Broadcast(TimerNotifier.ActToggle, 21));
			v.SetOnClickPendingIntent(Id("w_stop"), Broadcast(TimerNotifier.ActFinish, 22));

			var open = new Intent(ctx, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
			v.SetOnClickPendingIntent(Id("w_root"), PendingIntent.GetActivity(ctx, 20, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
			v.SetOnClickPendingIntent(Id("w_ring"), PendingIntent.GetActivity(ctx, 20, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));

			mgr.UpdateAppWidget(ids, v);

			// Çalışırken halkanın ilerlemesi için yarım dakikada bir yenile.
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

	static AG.Color Grad(AG.Color a, double t)
	{
		var (r, g, b) = RingGradient.At((byte)a.R, (byte)a.G, (byte)a.B, t);
		return new AG.Color(r, g, b);
	}

	static AG.Bitmap RingBitmap(double frac, AG.Color accent)
	{
		const int size = 280;
		var bmp = AG.Bitmap.CreateBitmap(size, size, AG.Bitmap.Config.Argb8888!)!;
		var c = new AG.Canvas(bmp);
		float cx = size / 2f, stroke = size * 0.095f, rad = size / 2f - stroke / 2 - 3;
		var paint = new AG.Paint(AG.PaintFlags.AntiAlias) { StrokeWidth = stroke };
		paint.SetStyle(AG.Paint.Style.Stroke);
		paint.Color = new AG.Color(255, 255, 255, 46);
		c.DrawCircle(cx, cx, rad, paint);
		if (frac < 0.002) return bmp;

		double sweep = Math.Min(frac, 0.9999) * 360;
		int n = (int)Math.Ceiling(sweep);
		(float x, float y) P(double deg) { double a = deg * Math.PI / 180; return (cx + rad * (float)Math.Sin(a), cx - rad * (float)Math.Cos(a)); }
		for (int i = 0; i < n; i++)
		{
			var (x0, y0) = P(i); var (x1, y1) = P(Math.Min(i + 1.4, sweep));
			paint.Color = Grad(accent, (i + 0.7) / 360.0);
			c.DrawLine(x0, y0, x1, y1, paint);
		}
		paint.SetStyle(AG.Paint.Style.Fill);
		var (sx, sy) = P(0); var (ex, ey) = P(sweep);
		paint.Color = Grad(accent, 0); c.DrawCircle(sx, sy, stroke / 2, paint);
		paint.Color = Grad(accent, sweep / 360.0); c.DrawCircle(ex, ey, stroke / 2, paint);
		return bmp;
	}

	// 0 oynat, 1 duraklat, 2 bitir
	static AG.Bitmap ButtonBitmap(int kind, AG.Color accent)
	{
		const int size = 120;
		var bmp = AG.Bitmap.CreateBitmap(size, size, AG.Bitmap.Config.Argb8888!)!;
		var c = new AG.Canvas(bmp);
		var paint = new AG.Paint(AG.PaintFlags.AntiAlias);
		paint.SetStyle(AG.Paint.Style.Fill);
		bool filled = kind != 2;
		paint.Color = filled ? accent : new AG.Color(255, 255, 255, 40);
		c.DrawCircle(size / 2f, size / 2f, size / 2f - 2, paint);

		paint.Color = filled ? new AG.Color(10, 14, 26) : new AG.Color(238, 241, 255);
		if (kind == 0)
		{
			var path = new AG.Path();
			path.MoveTo(size * 0.39f, size * 0.30f); path.LineTo(size * 0.71f, size * 0.50f); path.LineTo(size * 0.39f, size * 0.70f); path.Close();
			paint.StrokeJoin = AG.Paint.Join.Round;
			c.DrawPath(path, paint);
		}
		else if (kind == 1)
		{
			c.DrawRoundRect(new AG.RectF(size * 0.34f, size * 0.30f, size * 0.46f, size * 0.70f), 6, 6, paint);
			c.DrawRoundRect(new AG.RectF(size * 0.54f, size * 0.30f, size * 0.66f, size * 0.70f), 6, 6, paint);
		}
		else c.DrawRoundRect(new AG.RectF(size * 0.34f, size * 0.34f, size * 0.66f, size * 0.66f), 8, 8, paint);
		return bmp;
	}
}


