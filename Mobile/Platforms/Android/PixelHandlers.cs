using Android.Content;
using Android.Graphics;
using Android.Views;
using Microsoft.Maui.Handlers;
using AView = Android.Views.View;
using Paint = Android.Graphics.Paint;
using RectF = Android.Graphics.RectF;
using Rect = Android.Graphics.Rect;
using Color = Android.Graphics.Color;

namespace FlowState.Mobile;

public sealed class PixelViewHandler : ViewHandler<PixelView, PixelNative>
{
	public static readonly PropertyMapper<PixelView, PixelViewHandler> Map = new(ViewMapper)
	{ [nameof(PixelView.Image)] = (h, v) => h.PlatformView.SetImage(v.Image) };

	public PixelViewHandler() : base(Map) { }
	protected override PixelNative CreatePlatformView() => new(Context);
}

/// <summary>Resmi görünüme sığan en büyük tamsayı ölçekte, ortalı ve bulanıklaştırmadan çizer.</summary>
public sealed class PixelNative : AView
{
	readonly Paint paint = new() { FilterBitmap = false, AntiAlias = false };
	Bitmap? bmp;

	public PixelNative(Context c) : base(c) { }

	public void SetImage(PixImg? p)
	{
		bmp = p == null ? null : Sprites.ToBitmap(p.Px, p.W, p.H);
		Invalidate();
	}

	protected override void OnDraw(Canvas canvas)
	{
		if (bmp == null || Width == 0 || Height == 0) return;
		float fit = Math.Min(Width / (float)bmp.Width, Height / (float)bmp.Height), k = fit >= 1 ? (float)Math.Floor(fit) : fit;
		float w = bmp.Width * k, h = bmp.Height * k, x = (float)Math.Round((Width - w) / 2), y = (float)Math.Round((Height - h) / 2);
		canvas.DrawBitmap(bmp, new Rect(0, 0, bmp.Width, bmp.Height), new RectF(x, y, x + w, y + h), paint);
	}
}

public sealed class SceneViewHandler : ViewHandler<SceneView, SceneNative>
{
	public static readonly PropertyMapper<SceneView, SceneViewHandler> Map = new(ViewMapper)
	{ [nameof(SceneView.Playing)] = (h, v) => h.PlatformView.SetPlaying(v.Playing) };

	public SceneViewHandler() : base(Map) { }
	protected override SceneNative CreatePlatformView() => new(Context, VirtualView);
}

/// <summary>Sahneyi ~24 kare/sn çizer; güneş yazıları ve isim etiketleri yazı tipiyle üstüne çizilir. Dokunma: güneş, hayvan (tıkla, çift tıkla, sürükle).</summary>
public sealed class SceneNative : AView
{
	readonly SceneView view;
	readonly Paint pix = new() { FilterBitmap = false, AntiAlias = false };
	readonly Paint fill = new() { AntiAlias = true };
	readonly Paint text = new() { AntiAlias = true, TextAlign = Paint.Align.Center };
	readonly Typeface black = Sprites.Font("Nunito_900Black.ttf"), extra = Sprites.Font("Nunito_800ExtraBold.ttf");
	readonly float density;
	Bitmap? bmp;
	int k = 2;                                  // bir tampon pikseli kaç ekran pikseli
	long last;
	bool playing;

	public SceneNative(Context c, SceneView v) : base(c)
	{
		view = v;
		density = c.Resources!.DisplayMetrics!.Density;
	}

	public void SetPlaying(bool on)
	{
		playing = on;
		last = 0;
		if (on) PostInvalidateOnAnimation();
	}

	protected override void OnDraw(Canvas canvas)
	{
		if (Width == 0 || Height == 0 || view.Scene is not { } s) return;
		// Kısa kenar ~360 tampon pikseli: ekran ne kadar keskin olursa olsun çizim yükü sabit kalır.
		k = Math.Max(1, (int)Math.Round(Math.Min(Width, Height) / 360.0));
		int w = (Width + k - 1) / k, h = (Height + k - 1) / k;
		var (hz, sx, sy) = view.Place(w, h, density / k);
		s.Layout(w, h, hz, sx, sy);
		if (bmp == null || bmp.Width != w || bmp.Height != h) bmp = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!);

		long now = Java.Lang.JavaSystem.NanoTime();
		double dt = last == 0 ? 0 : Math.Min(0.1, (now - last) / 1e9);
		last = now;
		view.BeforeFrame?.Invoke(dt);
		s.Update(dt);
		s.Render();
		bmp!.SetPixels((int[])(object)s.Buf, 0, w, 0, 0, w, h);
		canvas.DrawBitmap(bmp, new Rect(0, 0, w, h), new RectF(0, 0, w * k, h * k), pix);

		DrawSunText(canvas, s);
		DrawLabels(canvas, s);
		if (playing) PostInvalidateDelayed(42);
	}

	static Color C(uint c) => new(unchecked((int)c));

	void DrawSunText(Canvas canvas, PixelScene s)
	{
		float cx = s.SunX * k, cy = s.SunY * k, disc = 78f * s.DialZ * k;      // disk çapı
		const uint cocoa = 0xFF3B2C25;
		text.SetTypeface(black);
		text.TextSize = disc * 0.205f;
		float maxW = disc * 0.86f, tw = text.MeasureText("00:00:00");
		if (tw > maxW) text.TextSize *= maxW / tw;
		text.Color = C(PixelArt.WithAlpha(cocoa, (int)(255 * view.TimeAlpha)));
		float time = text.TextSize;
		canvas.DrawText(view.Time, cx, cy + time * 0.42f, text);

		text.Color = C(cocoa);
		text.TextSize = disc * 0.105f;
		string course = view.Course;
		while (course.Length > 3 && text.MeasureText(course) > disc * 0.7f) course = course[..^2].TrimEnd() + "…";
		canvas.DrawText(course, cx, cy - time * 0.62f, text);

		if (view.Status.Length == 0) return;
		text.SetTypeface(extra);
		text.TextSize = disc * 0.072f;
		float sw = text.MeasureText(view.Status), dot = text.TextSize * 0.5f, pad = text.TextSize * 0.75f, ph = text.TextSize * 1.75f;
		float pw = sw + dot + pad * 2 + text.TextSize * 0.4f, top = cy + time * 0.72f;
		fill.Color = C(view.StatusBg);
		canvas.DrawRoundRect(new RectF(cx - pw / 2, top, cx + pw / 2, top + ph), ph / 2, ph / 2, fill);
		fill.Color = C(view.StatusDot);
		canvas.DrawCircle(cx - pw / 2 + pad + dot / 2, top + ph / 2, dot / 2, fill);
		text.Color = C(view.StatusFg);
		canvas.DrawText(view.Status, cx + (dot + text.TextSize * 0.4f) / 2, top + ph / 2 + text.TextSize * 0.35f, text);
	}

	void DrawLabels(Canvas canvas, PixelScene s)
	{
		text.SetTypeface(extra);
		text.TextSize = 12 * density;
		foreach (var l in s.Labels)
		{
			float x = l.X * k, bottom = l.Y * k, ph = 22 * density, tail = 5 * density;
			float pw = (l.Heart ? 13 * density : text.MeasureText(l.Text)) + 18 * density;
			x = Math.Clamp(x, pw / 2 + 4, Math.Max(pw / 2 + 4, Width - pw / 2 - 4));
			fill.Color = C(l.Accent ? view.LabelAccent : view.LabelBg);
			canvas.DrawRoundRect(new RectF(x - pw / 2, bottom - ph - tail, x + pw / 2, bottom - tail), 11 * density, 11 * density, fill);
			var p = new Android.Graphics.Path();
			p.MoveTo(x - tail, bottom - tail - 1); p.LineTo(x + tail, bottom - tail - 1); p.LineTo(x, bottom); p.Close();
			canvas.DrawPath(p, fill);
			if (l.Heart)
			{
				var heart = PixelArt.Icon("heart", 0xFFEF5D6C);
				using var hb = Sprites.ToBitmap(heart.Px, heart.W, heart.H);
				float hs = 14 * density;
				canvas.DrawBitmap(hb, new Rect(0, 0, hb.Width, hb.Height), new RectF(x - hs / 2, bottom - tail - ph / 2 - hs / 2, x + hs / 2, bottom - tail - ph / 2 + hs / 2), pix);
			}
			else
			{
				text.Color = C(l.Accent ? view.LabelOnAccent : view.LabelFg);
				canvas.DrawText(l.Text, x, bottom - tail - ph / 2 + text.TextSize * 0.35f, text);
			}
		}
	}

	// ---------- dokunma ----------

	Wanderer? held;
	bool onSun, dragging;
	float downX, downY;
	(double X, double Y) grab;
	long lastTap;
	string lastTapId = "";

	public override bool OnTouchEvent(MotionEvent? e)
	{
		if (e == null || view.Scene is not { } s) return false;
		float x = e.GetX(), y = e.GetY();
		switch (e.ActionMasked)
		{
			case MotionEventActions.Down:
				held = s.HitAnimal(x / k, y / k);
				double dx = x / k - s.SunX, dy = y / k - s.SunY;
				onSun = held == null && Math.Sqrt(dx * dx + dy * dy) < 46 * s.DialZ;
				(downX, downY, dragging) = (x, y, false);
				if (held == null && !onSun) return false;
				Parent?.RequestDisallowInterceptTouchEvent(true);
				return true;
			case MotionEventActions.Move:
				if (held == null) return true;
				var p = s.ToWorld(x / k, y / k);
				if (!dragging)
				{
					if (Math.Abs(x - downX) + Math.Abs(y - downY) < 9 * density) return true;
					var p0 = s.ToWorld(downX / k, downY / k);
					grab = (held.X - p0.X, held.Y - p0.Y);
					dragging = true;
					s.World.Grab(held);
					PerformHapticFeedback(FeedbackConstants.LongPress);
				}
				s.World.DragTo(held, p.X + grab.X, p.Y + grab.Y);
				return true;
			case MotionEventActions.Up:
			case MotionEventActions.Cancel:
				bool up = e.ActionMasked == MotionEventActions.Up;
				if (held is { } w)
				{
					if (dragging) s.World.Drop(w);
					else if (up)
					{
						long t = Java.Lang.JavaSystem.CurrentTimeMillis();
						if (w.Id == lastTapId && t - lastTap < 350) { lastTapId = ""; view.AnimalDoubleTapped?.Invoke(w.Id); }
						else { (lastTapId, lastTap) = (w.Id, t); s.World.Poke(w); }
					}
				}
				else if (onSun && up && Math.Abs(x - downX) + Math.Abs(y - downY) < 24 * density) view.SunTapped?.Invoke();
				held = null; onSun = dragging = false;
				return true;
		}
		return base.OnTouchEvent(e);
	}
}
