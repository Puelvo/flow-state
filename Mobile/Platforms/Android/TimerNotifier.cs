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
	const int NotifId = 1, ChoiceId = 3;
	public const string ActToggle = "com.puelvo.flowstate.TOGGLE";
	public const string ActFinish = "com.puelvo.flowstate.FINISH";
	public const string ActTimeUp = "com.puelvo.flowstate.TIMEUP";
	public const string ActStay = "com.puelvo.flowstate.STAY";
	public const string ActAutoEnd = "com.puelvo.flowstate.AUTOEND";

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

				bool up = target > 0 && elapsedSec >= target;                // geri sayım doldu, seçim bekleniyor
				string title = (string.IsNullOrEmpty(course) ? "Flow State" : course) + (up ? " · süre doldu" : "");

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
				long baseMs = SystemClock.ElapsedRealtime() + (long)((up ? 0 : target > 0 ? target - elapsedSec : -elapsedSec) * 1000);
				v.SetChronometer(Id("n_time"), baseMs, null, running && !up);
				v.SetImageViewBitmap(Id("n_toggle"), TimerWidget.ButtonBitmap(running && !up ? "pause" : "play", accent, TimerWidget.OnAccent(accent), 26, 20));
				v.SetImageViewBitmap(Id("n_stop"), TimerWidget.ButtonBitmap("stop", 0xFFFDE5E7, 0xFFEF5D6C, 22, 20));
				v.SetOnClickPendingIntent(Id("n_toggle"), up ? Broadcast(ActStay, 5) : Broadcast(ActToggle, 1));
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

	/// <summary>
	/// Geri sayım uyandırmaları: remaining süre dolunca (seçim sorulur), autoEnd soru gösterildikten sonra (kendiliğinden biter).
	/// null olan iptal edilir. Uygulama kapalıyken de çalışır.
	/// </summary>
	public static void ScheduleEnd(double? remaining, double? autoEnd = null)
	{
		try
		{
			if (Ctx.GetSystemService(Context.AlarmService) is not AlarmManager am) return;
			Set(Broadcast(ActTimeUp, 3), remaining);
			Set(Broadcast(ActAutoEnd, 6), autoEnd);

			void Set(PendingIntent pi, double? sec)
			{
				if (sec is not { } s) { am.Cancel(pi); return; }
				s = Math.Max(0, s);
				am.SetAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, SystemClock.ElapsedRealtime() + (long)(s * 1000) + 300, pi);
			}
		}
		catch { }
	}

	/// <summary>Uygulama açıkken: cihazın bildirim sesi, kısa.</summary>
	public static void Chime()
	{
		try { Android.Media.RingtoneManager.GetRingtone(Ctx, Android.Media.RingtoneManager.GetDefaultUri(Android.Media.RingtoneType.Notification))?.Play(); } catch { }
	}

	const string DoneChannel = "timer_done";

	static void EnsureDoneChannel()
	{
		if (OperatingSystem.IsAndroidVersionAtLeast(26) && Ctx.GetSystemService(Context.NotificationService) is NotificationManager nm && nm.GetNotificationChannel(DoneChannel) == null)
			nm.CreateNotificationChannel(new NotificationChannel(DoneChannel, "Süre doldu", NotificationImportance.High));
	}

	static PendingIntent? OpenApp(int code) =>
		PendingIntent.GetActivity(Ctx, code, new Intent(Ctx, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

	/// <summary>Süre doldu, uygulama arkada: "Akışta Kal" ve "Bitir ve Kutla" düğmeli bildirim.</summary>
	public static void ShowChoice()
	{
		try
		{
			EnsureDoneChannel();
			var b = new NotificationCompat.Builder(Ctx, DoneChannel)
				.SetSmallIcon(Ctx.ApplicationInfo!.Icon)!.SetContentTitle("Süre doldu")!.SetContentText("Akıştaysan bölme; süren aynı oturuma eklenir.")!
				.AddAction(0, "Akışta Kal", Broadcast(ActStay, 7))!.AddAction(0, "Bitir ve Kutla", Broadcast(ActFinish, 8))!
				.SetAutoCancel(true)!.SetPriority(NotificationCompat.PriorityHigh)!.SetTimeoutAfter(FlowRules.GraceSeconds * 1000L)!.SetContentIntent(OpenApp(9))!;
			NotificationManagerCompat.From(Ctx)!.Notify(ChoiceId, b.Build()!);
		}
		catch { }
	}

	public static void CancelChoice()
	{
		try { NotificationManagerCompat.From(Ctx)!.Cancel(ChoiceId); } catch { }
	}

	/// <summary>Seçim yapılmadı, oturum hedef süreyle kendiliğinden bitti.</summary>
	public static void ShowDone()
	{
		try
		{
			EnsureDoneChannel();
			var b = new NotificationCompat.Builder(Ctx, DoneChannel)
				.SetSmallIcon(Ctx.ApplicationInfo!.Icon)!.SetContentTitle("Oturumun kaydedildi")!.SetContentText("Süre dolmuştu; bahçende kutlama var.")!
				.SetAutoCancel(true)!.SetPriority(NotificationCompat.PriorityHigh)!.SetContentIntent(OpenApp(4))!;
			NotificationManagerCompat.From(Ctx)!.Notify(NotifId + 1, b.Build()!);
		}
		catch { }
	}

	public static void Cancel()
	{
		try { NotificationManagerCompat.From(Ctx).Cancel(NotifId); } catch { }
	}
}

/// <summary>Bildirimdeki ve widget'taki düğmeler (duraklat, bitir, akışta kal) ve geri sayım uyandırmaları.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class TimerActionReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		if (intent?.Action is not (TimerNotifier.ActToggle or TimerNotifier.ActFinish or TimerNotifier.ActStay or TimerNotifier.ActTimeUp or TimerNotifier.ActAutoEnd)) return;
		if (MainPage.Current is { } page) page.ExternalAction(intent.Action);
		else MainPage.Headless(intent.Action); // uygulama süreci kapalıyken doğrudan kayıtlar üzerinde işlem yapar
	}
}
