namespace FlowState.Mobile;

/// <summary>Piksel resmi keskin kenarlı (bulanıklaştırmadan) gösteren görünüm.</summary>
public sealed class PixelView : View
{
	PixImg? image;
	public PixImg? Image { get => image; set { image = value; Handler?.UpdateValue(nameof(Image)); } }
}

/// <summary>Canlı bahçe sahnesi (Çalış). Her karede BeforeFrame çağrılır; güneşe ve hayvanlara dokunma olaylarını bildirir.</summary>
public sealed class SceneView : View
{
	public PixelScene Scene = null!;
	/// <summary>(genişlik, yükseklik, dp başına tampon pikseli) → (ufuk, güneş x, güneş y).</summary>
	public Func<int, int, double, (int Horizon, int X, int Y)> Place = (w, h, _) => (h / 2, w / 2, h / 4);
	public Action<double>? BeforeFrame;
	public Action? SunTapped;
	public Action<string>? AnimalDoubleTapped;

	// Güneşin içindeki yazılar
	public string Course = "", Time = "", Status = "";
	public uint StatusBg = 0xFFFFFFFF, StatusFg = 0xFF000000, StatusDot = 0xFF000000;
	public float TimeAlpha = 1;
	public uint LabelBg = 0xFFFFFFFF, LabelFg = 0xFF3B2C25, LabelAccent = 0xFF5B8CFF, LabelOnAccent = 0xFFFFFFFF;

	bool playing;
	/// <summary>Sahne yalnızca görünürken ilerler.</summary>
	public bool Playing { get => playing; set { playing = value; Handler?.UpdateValue(nameof(Playing)); } }
}

#if ANDROID
/// <summary>Uygulama paketindeki sprite'lar (assets/pixel/*.png). Widget da kullandığı için arayüz gerektirmez.</summary>
public static class Sprites
{
	static readonly Dictionary<string, PixImg> cache = new();

	public static PixImg Load(string name)
	{
		lock (cache)
		{
			if (cache.TryGetValue(name, out var p)) return p;
			using var s = Android.App.Application.Context.Assets!.Open($"pixel/{name}.png");
			using var opt = new Android.Graphics.BitmapFactory.Options { InScaled = false, InPremultiplied = false };
			using var bmp = Android.Graphics.BitmapFactory.DecodeStream(s, null, opt)!;
			int w = bmp.Width, h = bmp.Height;
			var px = new int[w * h];
			bmp.GetPixels(px, 0, w, 0, 0, w, h);
			return cache[name] = new PixImg(px.Select(v => unchecked((uint)v) >> 24 == 0 ? 0 : unchecked((uint)v)).ToArray(), w, h);
		}
	}

	/// <summary>Piksel resmi Android bitmap'ine çevirir (isteğe bağlı tamsayı büyütme: sistem küçültürken keskin kalsın).</summary>
	public static Android.Graphics.Bitmap ToBitmap(uint[] src, int w, int h, int k = 1)
	{
		var px = new int[w * k * h * k];
		for (int y = 0; y < h * k; y++)
		for (int x = 0; x < w * k; x++) px[y * w * k + x] = unchecked((int)src[y / k * w + x / k]);
		return Android.Graphics.Bitmap.CreateBitmap(px, w * k, h * k, Android.Graphics.Bitmap.Config.Argb8888!)!;
	}

	public static Android.Graphics.Typeface Font(string file)
	{
		try { return Android.Graphics.Typeface.CreateFromAsset(Android.App.Application.Context.Assets, file)!; }
		catch { return Android.Graphics.Typeface.DefaultBold!; }
	}
}
#endif
