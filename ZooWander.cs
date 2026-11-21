namespace FlowState;

public enum WanderState { Idle, Walk, Greet, Held }

/// <summary>Dururken alınan poz. Sıra, poz şeridindeki kare sırasıyla aynıdır (&lt;id&gt;_poses.png).</summary>
public enum ZooPose { Stand, Sit, Sleep, Eat, Happy }

/// <summary>Bahçede dolaşan bir hayvan. Konum, ayaklarının bastığı noktadır (sahne pikseli).</summary>
public sealed class Wanderer
{
    public string Id = "";
    public double X = double.NaN, Y;      // NaN: henüz yerleştirilmedi
    public bool FacingRight = true;
    public WanderState State = WanderState.Idle;
    public double TargetX, TargetY;
    public double Timer;                  // Idle/Greet durumunda kalan süre (sn)
    public double WalkTime;               // yürüyüş karesini seçmek için
    public double HopT = -1;              // 0..1 zıplama ilerlemesi; < 0 ise zıplamıyor
    public double NameTimer;              // tıklanınca adının görüneceği süre
    public double GreetCooldown;          // tekrar selamlaşabilmesi için bekleme
    public bool Arriving;                 // bahçeye ilk kez giriyor (soldan yürür)
    public ZooPose Pose;                  // dururken: ayakta, oturuyor, uyuyor, yiyor
    public double HappyTimer;             // > 0 iken sevinç pozu (selamlaşma, tıklama)
    public bool[] Has = { true, true, true, true, true };   // bu hayvanda hangi pozlar var (çizen taraf doldurur)

    /// <summary>Yürümüyorken gösterilecek poz.</summary>
    public ZooPose Shown => HappyTimer > 0 && Has[(int)ZooPose.Happy] ? ZooPose.Happy : Has[(int)Pose] ? Pose : ZooPose.Stand;

    /// <summary>Fareyle tutulup taşınıyor.</summary>
    public bool Held => State == WanderState.Held;

    /// <summary>Zıplamanın o anki dikey kayması (yukarı negatif). Tutulan hayvan biraz havada durur.</summary>
    public double HopOffset => Held ? -ZooWorld.HoldLift : HopT < 0 ? 0 : -Math.Sin(Math.PI * HopT) * ZooWorld.HopHeight;
}

/// <summary>
/// Bahçedeki hayvanların davranışı: sakin sakin dolaşır, ara sıra birbirlerinin yanına gider,
/// karşılaşınca selamlaşır (durur, birbirine döner, sevinçle zıplar), tıklanınca zıplar.
/// Arayüzden bağımsızdır; masaüstü ve mobil aynı sınıfı kullanıp yalnızca çizer.
/// </summary>
public sealed class ZooWorld
{
    public const double Sprite = 96;       // ekrandaki hayvan boyutu (32 px × 3)
    public const double HopHeight = 26, HopSeconds = 0.42;
    public const double Speed = 26;        // piksel/sn — sakin yürüyüş
    public const double HoldLift = 14;     // tutulan hayvanın yerden yüksekliği

    public readonly List<Wanderer> Animals = new();
    public double Width, Height;
    readonly Random rng = new();

    double MinX => Sprite * 0.5;
    double MaxX => Math.Max(MinX, Width - Sprite * 0.5);
    double MinY => Sprite + 6;
    double MaxY => Math.Max(MinY, Height - 10);
    double Rand(double a, double b) => a + rng.NextDouble() * (b - a);

    public Wanderer Add(string id, bool arriving)
    {
        var w = new Wanderer { Id = id, Arriving = arriving };
        Animals.Add(w);
        return w;
    }

    /// <summary>Tıklama: zıplar, adı görünür, yürüyorsa kısa bir an durur.</summary>
    public void Poke(Wanderer w)
    {
        if (w.HopT < 0) w.HopT = 0;
        w.NameTimer = 1.8;
        w.HappyTimer = 0.9;
        if (w.Pose == ZooPose.Sleep) { w.Pose = ZooPose.Stand; w.Timer = 1.5; }      // uyanır
        if (w.State == WanderState.Walk && !w.Arriving) { w.State = WanderState.Idle; w.Timer = 1.0; }
    }

    /// <summary>Taşıma başlar: hayvan ne yapıyorsa bırakır, havaya kalkar.</summary>
    public void Grab(Wanderer w)
    {
        if (double.IsNaN(w.X)) return;
        w.State = WanderState.Held;
        w.Arriving = false;
        w.HopT = -1;
        w.Pose = ZooPose.Stand;
        w.NameTimer = 0;
    }

    /// <summary>Tutulan hayvanı ayaklarının basacağı noktaya taşır (sahne sınırları içinde).</summary>
    public void DragTo(Wanderer w, double x, double y)
    {
        if (!w.Held) return;
        x = Math.Clamp(x, MinX, MaxX); y = Math.Clamp(y, MinY, MaxY);
        if (Math.Abs(x - w.X) > 2) w.FacingRight = x > w.X;
        w.X = x; w.Y = y;
    }

    /// <summary>Bırakılan hayvan sevinir ve bir süre yeni yerinde kalır, sonra dolaşmaya devam eder.</summary>
    public void Drop(Wanderer w)
    {
        if (!w.Held) return;
        w.State = WanderState.Idle;
        w.Pose = ZooPose.Stand;
        w.HappyTimer = 0.8;
        w.Timer = Rand(5, 9);
        w.GreetCooldown = Math.Max(w.GreetCooldown, 3);
    }

    public void Update(double dt)
    {
        if (Width <= 0 || Height <= 0) return;
        dt = Math.Min(dt, 0.1);

        foreach (var w in Animals)
        {
            if (double.IsNaN(w.X)) Place(w);

            if (w.NameTimer > 0) w.NameTimer -= dt;
            if (w.GreetCooldown > 0) w.GreetCooldown -= dt;
            if (w.HappyTimer > 0) w.HappyTimer -= dt;
            if (w.HopT >= 0) { w.HopT += dt / HopSeconds; if (w.HopT >= 1) w.HopT = -1; }

            switch (w.State)
            {
                case WanderState.Held:
                    break;                                     // konumu fare belirler

                case WanderState.Idle:
                    w.Timer -= dt;
                    if (w.Timer <= 0) { PickTarget(w); w.State = WanderState.Walk; w.Pose = ZooPose.Stand; }
                    break;

                case WanderState.Greet:
                    w.Timer -= dt;
                    if (w.Timer <= 0) { w.State = WanderState.Idle; w.Timer = Rand(0.4, 1.4); }
                    break;

                case WanderState.Walk:
                    double dx = w.TargetX - w.X, dy = w.TargetY - w.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy), step = Speed * dt;
                    if (Math.Abs(dx) > 1) w.FacingRight = dx > 0;
                    if (dist <= step)
                    {
                        w.X = w.TargetX; w.Y = w.TargetY;
                        w.State = WanderState.Idle; w.Arriving = false;
                        Rest(w);
                    }
                    else { w.X += dx / dist * step; w.Y += dy / dist * step; w.WalkTime += dt; }
                    break;
            }

            if (!w.Arriving) { w.X = Math.Clamp(w.X, MinX, MaxX); w.Y = Math.Clamp(w.Y, MinY, MaxY); }
        }

        Meet();
    }

    /// <summary>Yürüyüş bitince ne yapacağını seçer: ayakta bekler, oturur, bir şeyler yer ya da uyur.</summary>
    void Rest(Wanderer w)
    {
        double r = rng.NextDouble();
        var (pose, min, max) = r < 0.40 ? (ZooPose.Stand, 2.0, 6.0)
                             : r < 0.62 ? (ZooPose.Sit, 6.0, 12.0)
                             : r < 0.82 ? (ZooPose.Eat, 4.0, 8.0)
                             : (ZooPose.Sleep, 14.0, 28.0);
        if (!w.Has[(int)pose]) (pose, min, max) = (ZooPose.Stand, 2.0, 6.0);
        w.Pose = pose;
        w.Timer = Rand(min, max);
    }

    void Place(Wanderer w)
    {
        w.Y = Rand(MinY, MaxY);
        if (w.Arriving)
        {
            w.X = -Sprite * 0.6;                                   // sahnenin solundan girer
            w.TargetX = Rand(MinX + 20, Math.Max(MinX + 20, Width * 0.6));
            w.TargetY = w.Y;
            w.State = WanderState.Walk; w.FacingRight = true;
            w.GreetCooldown = 6;
        }
        else
        {
            w.X = Rand(MinX, MaxX);
            w.FacingRight = rng.Next(2) == 0;
            w.Timer = Rand(0.3, 4);
            w.GreetCooldown = Rand(2, 8);
        }
    }

    void PickTarget(Wanderer w)
    {
        // Ara sıra bir arkadaşının yanına gider; böylece karşılaşmalar kendiliğinden olur.
        var others = Animals.Where(o => o != w && !o.Arriving && !double.IsNaN(o.X)).ToList();
        if (others.Count > 0 && w.GreetCooldown <= 0 && rng.NextDouble() < 0.35)
        {
            var o = others[rng.Next(others.Count)];
            double side = w.X < o.X ? -1 : 1;
            w.TargetX = Math.Clamp(o.X + side * 58, MinX, MaxX);
            w.TargetY = Math.Clamp(o.Y + Rand(-6, 6), MinY, MaxY);
            return;
        }
        double ang = Rand(0, Math.PI * 2), d = Rand(70, 240);
        w.TargetX = Math.Clamp(w.X + Math.Cos(ang) * d, MinX, MaxX);
        w.TargetY = Math.Clamp(w.Y + Math.Sin(ang) * d * 0.6, MinY, MaxY);
    }

    /// <summary>Yan yana gelen iki hayvan selamlaşır; üst üste binenlerden biri uzaklaşır.</summary>
    void Meet()
    {
        for (int i = 0; i < Animals.Count; i++)
        for (int j = i + 1; j < Animals.Count; j++)
        {
            var a = Animals[i]; var b = Animals[j];
            if (a.Arriving || b.Arriving || a.Held || b.Held || double.IsNaN(a.X) || double.IsNaN(b.X)) continue;
            double dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);

            if (dx < 34 && dy < 14)                                // üst üste: biri yürüyüp gitsin
            {
                var mover = b.Pose != ZooPose.Sleep ? b : a;
                if (mover.State == WanderState.Idle && mover.Pose != ZooPose.Sleep) mover.Timer = 0;
                continue;
            }
            if (a.State == WanderState.Greet || b.State == WanderState.Greet) continue;
            if (a.GreetCooldown > 0 || b.GreetCooldown > 0) continue;
            if (a.Pose == ZooPose.Sleep || b.Pose == ZooPose.Sleep) continue;        // uyuyan rahatsız edilmez
            if (dx > 76 || dy > 22) continue;

            foreach (var w in new[] { a, b })
            {
                w.State = WanderState.Greet; w.Timer = 2.4;
                w.Pose = ZooPose.Stand; w.HappyTimer = 2.4;
                w.HopT = 0;
                w.GreetCooldown = Rand(18, 30);
            }
            a.FacingRight = a.X < b.X; b.FacingRight = b.X < a.X;
        }
    }
}
