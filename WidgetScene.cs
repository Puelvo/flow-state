namespace FlowState;

/// <summary>Android ana ekran widget'ının resmi: küçük ölçekli bahçe; solda güneş saat, patikada jeep. Yazılar ve düğmeler üstüne ayrıca konur.</summary>
public static class WidgetScene
{
    /// <param name="w">Genişlik (dp = tampon pikseli).</param>
    /// <param name="advance">Son çizimden beri geçen süre; hayvanlar bu kadar ilerletilir (en çok 40 sn).</param>
    public static void Compose(PixelScene s, int w, int h, AppData data, SkyPalette palette, bool active, bool running, double lit, double advance)
    {
        bool small = h < 92;                         // tek satır: güneş sığmaz
        s.Palette = palette;
        s.Session = active; s.Running = running; s.Lit = lit;
        s.ShowSun = !small && w >= 200;
        s.DialZ = 1;
        s.PathAt = small ? 0.8 : 0.7;
        int horizon = small ? (int)(h * 0.52) : Math.Max(h - Math.Clamp((int)(h * 0.42), 54, 150), 62);
        int sun = PixelArt.DialN / 2;
        int sunY = Math.Max(sun + 1, horizon - sun + 12);
        s.Layout(w, h, horizon, 12 + sun, sunY);
        s.SetAvoid(w - 128, h - 48, w - 8);
        s.SetGarden(ZooCatalog.Animals.Where(a => Zoo.Owns(data, a.Id)).Select(a => a.Id), ZooSpecials.All(data).Take(3),
            ZooCatalog.Decors.Where(x => Zoo.Owns(data, x.Key) && x.Id is not ("sign" or "fence" or "swing")).Select(x => x.Id), 5, false, null);
        for (double t = Math.Clamp(advance, 0.1, 40); t > 0; t -= 0.08) s.Update(0.08);
        s.Render();
        Round(s, Math.Min(22, h / 3));
    }

    /// <summary>Piksel basamaklı yuvarlak köşeler (saydam).</summary>
    static void Round(PixelScene s, int r)
    {
        for (int y = 0; y < r; y++)
        for (int x = 0; x < r; x++)
        {
            double dx = r - x - 0.5, dy = r - y - 0.5;
            if (dx * dx + dy * dy <= r * r) continue;
            s.Buf[y * s.W + x] = s.Buf[y * s.W + s.W - 1 - x] = s.Buf[(s.H - 1 - y) * s.W + x] = s.Buf[(s.H - 1 - y) * s.W + s.W - 1 - x] = 0;
        }
    }
}
