using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace FlowState.Mobile;

/// <summary>Çalışma sürerken bildirim çubuğunda kalan bildirim: ders adı, canlı süre, Duraklat/Bitir düğmeleri.</summary>
public static class TimerNotifier
{
	const string ChannelId = "timer";
	const int NotifId = 1;
	public const string ActToggle = "com.puelvo.flowstate.TOGGLE";
	public const string ActFinish = "com.puelvo.flowstate.FINISH";

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

	public static void Show(string course, bool running, double elapsedSec)
	{
		try
		{
			EnsureChannel();
			var ctx = Ctx;
			var openIntent = new Intent(ctx, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
			var open = PendingIntent.GetActivity(ctx, 0, openIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

			var b = new NotificationCompat.Builder(ctx, ChannelId)
				.SetSmallIcon(ctx.ApplicationInfo!.Icon)
				.SetContentTitle(string.IsNullOrEmpty(course) ? "Flow State" : course)
				.SetOngoing(true)
				.SetOnlyAlertOnce(true)
				.SetVisibility(NotificationCompat.VisibilityPublic)
				.SetContentIntent(open);

			if (running)
			{
				long startMs = Java.Lang.JavaSystem.CurrentTimeMillis() - (long)(elapsedSec * 1000);
				b.SetContentText("Odaklanıyorsun").SetUsesChronometer(true).SetShowWhen(true).SetWhen(startMs);
			}
			else
			{
				int s = (int)elapsedSec;
				b.SetContentText($"Duraklatıldı · {s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}").SetShowWhen(false);
			}

			b.AddAction(0, running ? "Duraklat" : "Devam et", Broadcast(ActToggle, 1));
			b.AddAction(0, "Bitir", Broadcast(ActFinish, 2));
			NotificationManagerCompat.From(ctx).Notify(NotifId, b.Build()!);
		}
		catch { /* izin verilmediyse ya da cihaz reddederse uygulama çalışmaya devam eder */ }
	}

	public static void Cancel()
	{
		try { NotificationManagerCompat.From(Ctx).Cancel(NotifId); } catch { }
	}
}

/// <summary>Bildirimdeki Duraklat / Bitir düğmeleri.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class TimerActionReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		var finish = intent?.Action == TimerNotifier.ActFinish;
		if (intent?.Action != TimerNotifier.ActFinish && intent?.Action != TimerNotifier.ActToggle) return;
		if (MainPage.Current is { } page) page.ExternalAction(finish);
		else MainPage.Headless(finish); // uygulama süreci kapalıyken doğrudan kayıtlar üzerinde işlem yapar
	}
}
