using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace FlowState.Mobile;

// Zamanlayıcı, canlı seans paylaşımı ve bildirim.
public partial class MainPage
{
	bool keepOnApplied;
	void UpdateKeepOn()
	{
		bool want = Preferences.Get("keepOn", false) && Active && Running;
		if (want == keepOnApplied) return;
		keepOnApplied = want;
		try { DeviceDisplay.Current.KeepScreenOn = want; } catch { }
	}

	// ---------- zamanlayıcı ----------
	void ToggleTimer()
	{
		if (TimeUp) return;                     // seçim bekleniyor (Akışta Kal / Bitir)
		Buzz();
		if (!Active)
		{
			var c = CurrentCourse; if (c == null) return;
			runCourseId = c.Id; runStart = DateTime.Now; accumulated = 0; runningSince = DateTime.UtcNow; pickedCourseId = c.Id; runId = Guid.NewGuid().ToString("N");
			data.CountdownRunId = data.Countdown ? runId : "";
			data.CountdownSeconds = data.Countdown ? Math.Clamp(data.CountdownMinutes, 5, 120) * 60 : 0;
			Preferences.Set("lastCourse", c.Id);
			_ = AskNotifyPermission();
		}
		else if (Running) { accumulated = Elapsed; runningSince = null; }
		else runningSince = DateTime.UtcNow;
		PersistTimer(); PublishLive(); SyncNotification(); RefreshWork();
	}

	/// <summary>Geri sayım hedefi (sn); kronometrede 0.</summary>
	int Target => Active && runId.Length > 0 && runId == data.CountdownRunId ? data.CountdownSeconds : 0;

	/// <summary>Geri sayım doldu, seçim bekleniyor. Sayaç durmaz; cevap gelmezse FlowRules.GraceSeconds sonra hedef süreyle biter.</summary>
	bool TimeUp => Target > 0 && Elapsed >= Target;

	bool foreground = true;

	/// <summary>Bu oturum için "süre doldu" sorusunun gösterilmesinden beri geçen saniye; hiç gösterilmediyse -1.
	/// Bekleme süresi buradan sayılır: Android uyandırmayı geciktirse de kullanıcıya seçmek için zaman kalır.</summary>
	static double AskedAgo(string run)
	{
		var p = Preferences.Get("timeUp", "").Split('|');
		return p.Length == 2 && p[0] == run && long.TryParse(p[1], out var t) ? (DateTime.UtcNow - new DateTime(t, DateTimeKind.Utc)).TotalSeconds : -1;
	}

	static void MarkAsked(string run) => Preferences.Set("timeUp", $"{run}|{DateTime.UtcNow.Ticks}");

	/// <summary>Oturum kesilmeden kronometreye döner; artı süre aynı oturuma eklenir.</summary>
	void Stay()
	{
		if (!TimeUp) return;
		Buzz();
		data.CountdownRunId = "";
		Store.Save(data);
#if ANDROID
		TimerNotifier.CancelChoice();
#endif
		SyncNotification(); RefreshWork();
	}

	/// <summary>Süre yeni dolduysa sakince haber verir; bekleme süresi geçtiyse bitirir.</summary>
	bool CheckTimeUp()
	{
		if (!TimeUp || !Running) return false;
		double ago = AskedAgo(runId);
		if (ago >= FlowRules.GraceSeconds || ago < 0 && Elapsed - Target > FlowRules.MaxLateSeconds) { Finish(timeUp: true); return true; }
		if (ago >= 0 || !foreground) return false;
		MarkAsked(runId);
		try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); } catch { }
#if ANDROID
		TimerNotifier.Chime();
#endif
		SyncNotification();
		return false;
	}

	void Commit() => Finish(false);

	/// <summary>Oturumu bitirir, kaydeder ve kazanılan jetonu gösterir.</summary>
	void Finish(bool timeUp)
	{
		if (!Active) return;
		Buzz();
		int s = (int)Elapsed, target = Target;
		if (target > 0) s = Math.Min(s, target);                 // geri sayım hedefi aşılmaz
		Session? done = null;
		if (s >= 5) data.Sessions.Add(done = new Session { Id = RunIdOrNew(), CourseId = runCourseId!, Start = runStart, Seconds = s });
		var endedCourse = runCourseId!;
		runCourseId = null; runningSince = null; accumulated = 0;
		data.CountdownRunId = "";
#if ANDROID
		TimerNotifier.CancelChoice();
#endif
		data.SetLive(false, RunIdOrNew(), endedCourse, runStart, 0, null);
		PersistTimer();
		Special? special = null;
		if (done != null)
		{
			special = ZooSpecials.From(data, done);
			if (special != null) arriving = special.Key;
			scene.Celebrate = true;
		}
		Changed();
		sync.RequestLive();
		SyncNotification();
		if (done != null) ShowReward(done, timeUp, special);
	}

	void PersistTimer()
	{
		if (!Active) { Preferences.Remove("run"); return; }
		Preferences.Set("run", RunString(runCourseId!, runStart, accumulated, runningSince, runId));
	}

	static string RunString(string courseId, DateTime start, double acc, DateTime? since, string runId = "") =>
		$"{courseId}|{start.Ticks}|{acc.ToString(CultureInfo.InvariantCulture)}|{since?.Ticks ?? 0}|{runId}";

	// ---------- canlı seans: cihazlar arası paylaşım ----------
	string RunIdOrNew() { if (runId.Length == 0) runId = Guid.NewGuid().ToString("N"); return runId; }

	/// <summary>Yerel zamanlayıcı durumunu paylaşılan canlı duruma yazıp hemen gönderir.</summary>
	void PublishLive()
	{
		data.SetLive(Active, RunIdOrNew(), runCourseId ?? "", runStart, accumulated, runningSince);
		Store.Save(data);
		sync.RequestLive();
	}

	// UI thread: başka cihazın canlı seansı geldi. Uygulandıysa true.
	bool OnRemoteLive(LiveState r)
	{
		if (r.Active)
		{
			if (!data.Courses.Any(c => c.Id == r.CourseId)) return false; // ders henüz gelmedi; sonra denenir
			if (Active && runId != r.RunId)
			{
				// Bu cihazdaki ayrı seansın emeği kaybolmasın: kaydet, sonra paylaşılana geç.
				int s = (int)Elapsed;
				if (s >= 5) data.Sessions.Add(new Session { Id = RunIdOrNew(), CourseId = runCourseId!, Start = runStart, Seconds = s });
				Store.Save(data); sync.MarkDirty();
			}
			runCourseId = r.CourseId; runId = r.RunId; runStart = r.Start; accumulated = r.Accumulated; runningSince = r.RunningSince;
			pickedCourseId = r.CourseId;
		}
		else if (Active && runId != r.RunId)
		{
			// Uzaktaki bitiş başka bir seansa ait; bizimki sürüyor. Durumumuzu yenileyip tekrar yayınlarız.
			data.SetLive(true, runId, runCourseId!, runStart, accumulated, runningSince);
			Store.Save(data);
			return false;
		}
		else
		{
			runCourseId = null; runningSince = null; accumulated = 0; runId = r.RunId;
		}
		data.Live = r;
		Store.Save(data);
		PersistTimer(); SyncNotification(); RefreshAll();
		return true;
	}

	// ---------- bildirim (çalışırken) ----------
	public static MainPage? Current;

	void SyncNotification()
	{
#if ANDROID
		if (!Active) TimerNotifier.Cancel();
		else TimerNotifier.Show(CurrentCourse?.Name ?? "", Running, Elapsed, Target);
		double asked = TimeUp ? AskedAgo(runId) : -1;
		if (!Active || !Running || Target == 0) TimerNotifier.ScheduleEnd(null);
		else if (asked >= 0) TimerNotifier.ScheduleEnd(null, FlowRules.GraceSeconds - asked);
		else TimerNotifier.ScheduleEnd(Math.Max(0, Target - Elapsed));
		TimerWidget.Refresh();
#endif
	}

	static async Task AskNotifyPermission()
	{
#if ANDROID
		try
		{
			if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() != PermissionStatus.Granted)
				await Permissions.RequestAsync<Permissions.PostNotifications>();
		}
		catch { }
#else
		await Task.CompletedTask;
#endif
	}

	/// <summary>Bildirimdeki düğmeler (uygulama açıkken).</summary>
	public void ExternalAction(string action) => MainThread.BeginInvokeOnMainThread(() =>
	{
		switch (action)
		{
			case TimerNotifier.ActToggle: ToggleTimer(); break;
			case TimerNotifier.ActStay: Stay(); break;
			case TimerNotifier.ActFinish: Finish(TimeUp); break;
			case TimerNotifier.ActTimeUp:
				if (!TimeUp || !Running) break;
				if (foreground) CheckTimeUp();
				else if (AskedAgo(runId) < 0) { MarkAsked(runId); TimerNotifier.ShowChoice(); SyncNotification(); }
				break;
			case TimerNotifier.ActAutoEnd:
				if (!TimeUp || !Running || AskedAgo(runId) < FlowRules.GraceSeconds - 5) break;
				if (!foreground) TimerNotifier.ShowDone();
				Finish(timeUp: true);
				break;
		}
	});

	/// <summary>Bildirimdeki düğmeler (uygulama süreci kapalıyken): kayıtlı zamanlayıcıyı doğrudan günceller.</summary>
	public static void Headless(string action)
	{
		HeadlessCore(action);
		TimerWidget.Refresh();
	}

	static void HeadlessCore(string action)
	{
		bool finish = action == TimerNotifier.ActFinish;
		try
		{
			Store.Dir = FileSystem.AppDataDirectory;
			var p = Preferences.Get("run", "").Split('|');
			if (p.Length < 4)
			{
				// Çalışan seans yok: widget'taki oynat düğmesi son seçilen dersle yeni seans başlatır.
				if (action != TimerNotifier.ActToggle) { TimerNotifier.Cancel(); TimerNotifier.CancelChoice(); return; }
				var d0 = Store.Load();
				var c0 = d0.Courses.FirstOrDefault(c => c.Id == Preferences.Get("lastCourse", "")) ?? d0.Courses.FirstOrDefault();
				if (c0 == null) return;
				var rid = Guid.NewGuid().ToString("N"); var st = DateTime.Now; var sn = DateTime.UtcNow;
				Preferences.Set("run", RunString(c0.Id, st, 0, sn, rid));
				int tg = d0.Countdown ? Math.Clamp(d0.CountdownMinutes, 5, 120) * 60 : 0;
				d0.CountdownRunId = tg > 0 ? rid : ""; d0.CountdownSeconds = tg;
				d0.SetLive(true, rid, c0.Id, st, 0, sn); Store.Save(d0);
				TimerNotifier.Show(c0.Name, true, 0, tg);
				TimerNotifier.ScheduleEnd(tg > 0 ? tg : null);
				return;
			}
			var data = Store.Load();
			var course = data.Courses.FirstOrDefault(c => c.Id == p[0]);
			if (course == null) { Preferences.Remove("run"); TimerNotifier.Cancel(); return; }
			var start = new DateTime(long.Parse(p[1]));
			var acc = double.Parse(p[2], CultureInfo.InvariantCulture);
			var rid2 = p.Length > 4 ? p[4] : "";
			if (rid2.Length == 0) rid2 = Guid.NewGuid().ToString("N");
			var t = long.Parse(p[3]);
			DateTime? since = t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
			double el = acc + (since is { } r ? (DateTime.UtcNow - r).TotalSeconds : 0);
			int target = rid2 == data.CountdownRunId ? data.CountdownSeconds : 0;
			bool up = target > 0 && el >= target;
			if (action == TimerNotifier.ActTimeUp)
			{
				if (!up || since == null || AskedAgo(rid2) >= 0) return;
				if (el - target > FlowRules.MaxLateSeconds) { finish = true; TimerNotifier.ShowDone(); }
				else
				{
					MarkAsked(rid2);
					TimerNotifier.Show(course.Name, true, el, target);
					TimerNotifier.ShowChoice();
					TimerNotifier.ScheduleEnd(null, FlowRules.GraceSeconds);
					return;
				}
			}
			if (action == TimerNotifier.ActAutoEnd)
			{
				if (!up || since == null || AskedAgo(rid2) < FlowRules.GraceSeconds - 5) return;
				finish = true;
				TimerNotifier.ShowDone();
			}
			if (up && action is TimerNotifier.ActStay or TimerNotifier.ActToggle && up)
			{
				// Akışta kal: kronometreye döner (dolmuşken duraklat düğmesi de bunu yapar).
				data.CountdownRunId = ""; Store.Save(data);
				TimerNotifier.CancelChoice(); TimerNotifier.ScheduleEnd(null);
				TimerNotifier.Show(course.Name, since != null, el);
				return;
			}
			if (action == TimerNotifier.ActStay) return;
			if (finish)
			{
				TimerNotifier.ScheduleEnd(null); TimerNotifier.CancelChoice();
				if (target > 0) el = Math.Min(el, target);
				data.CountdownRunId = "";
				if (el >= 5) { data.Sessions.Add(new Session { Id = rid2, CourseId = course.Id, Start = start, Seconds = (int)el }); }
				data.SetLive(false, rid2, course.Id, start, 0, null); Store.Save(data);
				Preferences.Remove("run");
				TimerNotifier.Cancel();
				return;
			}
			if (since != null) { acc = el; since = null; } else since = DateTime.UtcNow;
			Preferences.Set("run", RunString(course.Id, start, acc, since, rid2));
			data.SetLive(true, rid2, course.Id, start, acc, since); Store.Save(data);
			TimerNotifier.Show(course.Name, since != null, el, target);
			TimerNotifier.ScheduleEnd(since != null && target > 0 ? target - el : null);
		}
		catch { }
	}

	void RestoreTimer()
	{
		try
		{
			var p = Preferences.Get("run", "").Split('|');
			if (p.Length < 4 || !data.Courses.Any(c => c.Id == p[0])) { Preferences.Remove("run"); return; }
			runId = p.Length > 4 ? p[4] : ""; runCourseId = p[0]; runStart = new DateTime(long.Parse(p[1])); accumulated = double.Parse(p[2], CultureInfo.InvariantCulture);
			var t = long.Parse(p[3]); runningSince = t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
		}
		catch { runCourseId = null; runningSince = null; accumulated = 0; }
	}

	void Tick()
	{
		RefreshCoins();
		if (!Active) return;
		if (CheckTimeUp()) return;
		RefreshTimer();
		if (tab == 1) RefreshStats();
		if (++persistCounter >= 15) { persistCounter = 0; PersistTimer(); }
	}

	public void OnResumed()
	{
		foreground = true;
#if ANDROID
		TimerNotifier.CancelChoice();
#endif
		CheckTimeUp();
		sceneView.Playing = tab == 0;
		RefreshAll(); _ = sync.SyncAsync(); _ = CheckPending();
	}
	public void OnStopped() { foreground = false; sceneView.Playing = false; PersistTimer(); Store.Save(data); }
}
