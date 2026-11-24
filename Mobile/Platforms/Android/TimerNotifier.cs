using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using AndroidX.Core.App;

namespace FlowState.Mobile;

/// <summary>Çalışma sürerken bildirim çubuğunda kalan bildirim: ders adı, canlı süre, Duraklat/Bitir düğmeleri.</summary>
public static class TimerNotifier
{
	const string ChannelId = "timer";
	const int NotifId = 1;
	public const string ActToggle = "com.puelvo.flowstate.TOGGLE";
	public const string ActFinish = "com.puelvo.flowstate.FINISH";
	public const string ActTimeUp = "com.puelvo.flowstate.TIMEUP";

	static Context Ctx => Android.App.Application.Context;

	static void EnsureChannel()
	{
		if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
		if (Ctx.GetSystemService(Context.NotificationService) is not NotificationManager nm) return;
		if (nm.GetNotificationChannel(ChannelId) != null) return;
		var ch = new NotificationChannel(ChannelId, "Çalışma zamanlayıcı", NotificationImportance.Low)
		{
			Description = "Çalışırken süreyi gösterir"
		};
		ch.SetShowBadge(false);
		nm.CreateNotificationChannel(ch);
	}

	static PendingIntent Broadcast(string action, int code)
	{
		var i = new Intent(Ctx, typeof(TimerActionReceiver)).SetAction(action);
		return PendingIntent.GetBroadcast(Ctx, code, i, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
	}

	public static void Show(string course, bool running, double elapsedSec, int target = 0)
	{
		try
		{
			EnsureChannel();
			var ctx = Ctx;
			var openIntent = new Intent(ctx, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
			var open = PendingIntent.GetActivity(ctx, 0, openIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

				string title = string.IsNullOrEmpty(course) ? "Flow State" : course;

				Store.Dir = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
				var data = Store.Load();
				uint accent = TimerWidget.Accent(data);
				var palette = DayCycle.At(DateTime.Now, DayCycle.Parse(data.GardenTheme));
				double lit = target > 0 ? Math.Min(1, elapsedSec / target) * 60 : elapsedSec % 3600 / 60;
				uint fg = data.DarkTheme ? 0xFFF5EEE7 : 0xFF3B2C25;

				int Id(string n) => ctx.Resources!.GetIdentifier(n, "id", ctx.PackageName);
				var v = new RemoteViews(ctx.PackageName, ctx.Resources!.GetIdentifier("notif_timer", "layout", ctx.PackageName));
				v.SetInt(Id("n_root"), "setBackgroundResource", ctx.Resources!.GetIdentifier(data.DarkTheme ? "notif_bg_dark" : "notif_bg", "drawable", ctx.PackageName));
				v.SetImageViewBitmap(Id("n_ring"), TimerWidget.SunBitmap(palette, true, lit));
				v.SetTextViewText(Id("n_course"), title);
				v.SetTextColor(Id("n_time"), TimerWidget.Col(fg)); v.SetTextColor(Id("n_course"), TimerWidget.Col(fg));
				if (OperatingSystem.IsAndroidVersionAtLeast(24)) v.SetChronometerCountDown(Id("n_time"), target > 0);
				long baseMs = SystemClock.ElapsedRealtime() + (long)((target > 0 ? target - elapsedSec : -elapsedSec) * 1000);
				v.SetChronometer(Id("n_time"), baseMs, null, running);
				v.SetImageViewBitmap(Id("n_toggle"), TimerWidget.ButtonBitmap(running ? "pause" : "play", accent, TimerWidget.OnAccent(accent), 26, 20));
				v.SetImageViewBitmap(Id("n_stop"), TimerWidget.ButtonBitmap("stop", 0xFFFDE5E7, 0xFFEF5D6C, 22, 20));
				v.SetOnClickPendingIntent(Id("n_toggle"), Broadcast(ActToggle, 1));
				v.SetOnClickPendingIntent(Id("n_stop"), Broadcast(ActFinish, 2));

				var b = new NotificationCompat.Builder(ctx, ChannelId)
					.SetSmallIcon(ctx.ApplicationInfo!.Icon)
					.SetContentTitle(title)
					.SetStyle(new NotificationCompat.DecoratedCustomViewStyle())
					.SetCustomContentView(v)
					.SetCustomBigContentView(v)
					.SetColor(unchecked((int)accent))
					.SetOngoing(true)
					.SetOnlyAlertOnce(true)
					.SetShowWhen(false)
					.SetVisibility(NotificationCompat.VisibilityPublic)
					.SetContentIntent(open);
				NotificationManagerCompat.From(ctx).Notify(NotifId, b.Build()!);
		}
		catch { /* izin verilmediyse ya da cihaz reddederse uygulama çalışmaya devam eder */ }
	}

	/// <summary>Geri sayımın bitişi için uyandırma kurar (saniye &lt; 0: iptal). Uygulama kapalıyken de oturum zamanında biter.</summary>
	public static void ScheduleEnd(double seconds)
	{
		try
		{
			if (Ctx.GetSystemService(Context.AlarmService) is not AlarmManager am) return;
			var pi = Broadcast(ActTimeUp, 3);
			if (seconds < 0) { am.Cancel(pi); return; }
			am.SetAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, SystemClock.ElapsedRealtime() + (long)(seconds * 1000) + 300, pi);
		}
		catch { }
	}

	/// <summary>Geri sayım uygulama arkadayken bitti: sesli, kısa bir bildirim.</summary>
	public static void ShowDone()
	{
		try
		{
			const string ch = "timer_done";
			if (OperatingSystem.IsAndroidVersionAtLeast(26) && Ctx.GetSystemService(Context.NotificationService) is NotificationManager nm && nm.GetNotificationChannel(ch) == null)
				nm.CreateNotificationChannel(new NotificationChannel(ch, "Süre doldu", NotificationImportance.High));
			var open = PendingIntent.GetActivity(Ctx, 4, new Intent(Ctx, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
			var b = new NotificationCompat.Builder(Ctx, ch)
				.SetSmallIcon(Ctx.ApplicationInfo!.Icon)!.SetContentTitle("Süre doldu!")!.SetContentText("Oturumun kaydedildi; bahçende kutlama var.")!
				.SetAutoCancel(true)!.SetPriority(NotificationCompat.PriorityHigh)!.SetContentIntent(open)!;
			NotificationManagerCompat.From(Ctx)!.Notify(NotifId + 1, b.Build()!);
		}
		catch { }
	}

	public static void Cancel()
	{
		try { NotificationManagerCompat.From(Ctx).Cancel(NotifId); } catch { }
	}
}

/// <summary>Bildirimdeki ve widget'taki Duraklat / Bitir düğmeleri; geri sayımın bitişi.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class TimerActionReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		var finish = intent?.Action is TimerNotifier.ActFinish or TimerNotifier.ActTimeUp;
		if (!finish && intent?.Action != TimerNotifier.ActToggle) return;
		if (intent?.Action == TimerNotifier.ActTimeUp && Microsoft.Maui.Storage.Preferences.Get("run", "").Length > 0) TimerNotifier.ShowDone();
		if (MainPage.Current is { } page) page.ExternalAction(finish);
		else MainPage.Headless(finish); // uygulama süreci kapalıyken doğrudan kayıtlar üzerinde işlem yapar
	}
}
