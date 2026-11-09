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

				string title = string.IsNullOrEmpty(course) ? "Flow State" : course;

				Store.Dir = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
				Android.Graphics.Color accent;
				try { accent = Android.Graphics.Color.ParseColor(Store.Load().WidgetColor); } catch { accent = Android.Graphics.Color.ParseColor("#5B8CFF"); }

				int Id(string n) => ctx.Resources!.GetIdentifier(n, "id", ctx.PackageName);
				var v = new RemoteViews(ctx.PackageName, ctx.Resources!.GetIdentifier("notif_timer", "layout", ctx.PackageName));
				v.SetImageViewBitmap(Id("n_ring"), TimerWidget.RingBitmap(elapsedSec % 3600 / 3600.0, accent));
				v.SetTextViewText(Id("n_course"), title);
				v.SetTextColor(Id("n_time"), accent);
				long baseMs = SystemClock.ElapsedRealtime() - (long)(elapsedSec * 1000);
				v.SetChronometer(Id("n_time"), baseMs, null, running);
				v.SetImageViewBitmap(Id("n_toggle"), TimerWidget.ButtonBitmap(running ? 1 : 0, accent));
				v.SetImageViewBitmap(Id("n_stop"), TimerWidget.ButtonBitmap(2, accent));
				v.SetOnClickPendingIntent(Id("n_toggle"), Broadcast(ActToggle, 1));
				v.SetOnClickPendingIntent(Id("n_stop"), Broadcast(ActFinish, 2));

				var b = new NotificationCompat.Builder(ctx, ChannelId)
					.SetSmallIcon(ctx.ApplicationInfo!.Icon)
					.SetContentTitle(title)
					.SetStyle(new NotificationCompat.DecoratedCustomViewStyle())
					.SetCustomContentView(v)
					.SetCustomBigContentView(v)
					.SetColor(accent.ToArgb())
					.SetOngoing(true)
					.SetOnlyAlertOnce(true)
					.SetShowWhen(false)
					.SetVisibility(NotificationCompat.VisibilityPublic)
					.SetContentIntent(open);
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
