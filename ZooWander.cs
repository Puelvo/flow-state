namespace FlowState;

public enum WanderState { Idle, Walk, Greet }

/// <summary>Bahçede hayvanların gidebileceği yerler.</summary>
public enum ZooSpotKind { None, Pond, Shade }

/// <summary>Bahçenin havası: oturuma göre. Çalışırken sakin, molada oyunbaz.</summary>
public enum ZooMood { Free, Calm, Play }

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
    public Wanderer? Follow;              // yavru: annesinin/babasının peşinden gider
    public bool Welcoming;                // gezgini karşılamaya gidiyor (varınca selamlar)
    public ZooSpotKind Intent;            // gittiği yer: gölet (su içer) ya da ağaç gölgesi (dinlenir)

    /// <summary>Yürümüyorken gösterilecek poz.</summary>
    public ZooPose Shown => HappyTimer > 0 && Has[(int)ZooPose.Happy] ? ZooPose.Happy : Has[(int)Pose] ? Pose : ZooPose.Stand;

    /// <summary>Zıplamanın o anki dikey kayması (yukarı negatif).</summary>
    public double HopOffset => HopT < 0 ? 0 : -Math.Sin(Math.PI * HopT) * ZooWorld.HopHeight;
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

    public readonly List<Wanderer> Animals = new();

    /// <summary>Bahçede hep olan gezgin (kullanıcı). Hayvan listesinde değildir.</summary>
    public readonly Wanderer Keeper = new() { Id = "explorer" };
    public ZooMood Mood = ZooMood.Free;
    public bool Night;                     // gece: hayvanlar uykucu
    public bool KeeperWalking;             // çalışırken gezgin çimende dolaşır
    public double Celebrating;             // > 0: hayvanlar gezginin etrafında (oturum bitti)
    public const double KeeperSpeed = 34, CelebrateSeconds = 11;
    public double Width, Height;

    /// <summary>Hayvanların durmadığı alan (ör. çimendeki düğmeler); boşsa yok. Sahne pikseli, ayak noktası.</summary>
    public (double L, double T, double R, double B)? Avoid;

    /// <summary>İki hayvanın arasında kalması gereken yatay boşluk (yavrular küçük, daha yakın durabilir).</summary>
    static double Space(Wanderer a, Wanderer b) => a.Follow != null || b.Follow != null ? 46 : 68;
    const double SpaceY = 26;

    /// <summary>Bir hayvanın durduğu ya da gitmekte olduğu nokta.</summary>
    static (double X, double Y) Spot(Wanderer o) => o.State == WanderState.Walk ? (o.TargetX, o.TargetY) : (o.X, o.Y);

    /// <summary>Bu noktada başkasıyla iç içe geçer mi? Anne-yavru ve gezgin de hesaba katılır.</summary>
    double Crowd(double x, double y, Wanderer self)
    {
        double worst = double.MaxValue;   // en yakın komşuya kalan pay (negatif: iç içe)
        foreach (var o in Animals)
        {
            if (o == self || double.IsNaN(o.X) || o.Arriving) continue;
            var (ox, oy) = Spot(o);
            if (Math.Abs(y - oy) >= SpaceY) continue;
            worst = Math.Min(worst, Math.Abs(x - ox) - Space(self, o));
        }
        if (!double.IsNaN(Keeper.X) && Math.Abs(y - Keeper.Y) < SpaceY)
            worst = Math.Min(worst, Math.Abs(x - Keeper.X) - 56);
        return worst;
    }

    /// <summary>Üreticiden en fazla 10 aday alır; ilk boş olanı, yoksa en ferahını hedef yapar.</summary>
    void Aim(Wanderer w, Func<(double X, double Y)> candidate)
    {
        double bestGap = double.MinValue; (double X, double Y) best = (w.X, w.Y);
        for (int i = 0; i < 10; i++)
        {
            var (cx, cy) = candidate();
            cy = Math.Clamp(cy, MinY, MaxY);
            cx = Clear(Math.Clamp(cx, MinX, MaxX), cy);
            double gap = Crowd(cx, cy, w);
            if (gap >= 0) { best = (cx, cy); break; }
            if (gap > bestGap) { bestGap = gap; best = (cx, cy); }
        }
        w.TargetX = best.X; w.TargetY = best.Y;
    }

    /// <summary>Hedef yasak alandaysa yatayda en yakın kenarın dışına iter.</summary>
    double Clear(double x, double y)
    {
        // Düğme alanı ve engeller (gölet suyu, ağaç gövdesi, kayalar, çalılar): birkaç kez it, iç içe engellerde de çıkar.
        for (int pass = 0; pass < 3; pass++)
        {
            bool moved = false;
            foreach (var a in Avoid is { } av ? Blocks.Append(av) : Blocks)
            {
                if (y < a.T || y > a.B || x < a.L || x > a.R) continue;
                double left = a.L - 12, right = a.R + 12;
                bool goLeft = (x - a.L < a.R - x && left > MinX) || right > MaxX;
                x = Math.Clamp(goLeft ? left : right, MinX, MaxX);
                moved = true;
            }
            if (!moved) break;
        }
        return x;
    }

    /// <summary>Hayvanların ayak basmadığı yerler (sahne kurar): gölet, ağaç gövdesi, kaya, çalı.</summary>
    public readonly List<(double L, double T, double R, double B)> Blocks = new();
    /// <summary>Gölet (ortası ve yatay yarıçapı) ve ağaç gölgesi (gövdenin dibi). Sahne kurar; yoksa null.</summary>
    public (double X, double Y, double Rx)? Pond;
    public (double X, double Y)? Shade;
    /// <summary>Gezginin çalışırken yürüdüğü patika: x → y. Sahne kurar.</summary>
    public Func<double, double>? PathY;
    public bool Noon;                     // öğle sıcağı: gölgeye daha çok giderler
    public double Top => MinY;
    public double Bottom => MaxY;
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

    /// <summary>Oturum bitti: herkes gezginin etrafına toplanıp onu selamlar; gezgin sevinçle zıplar.</summary>
    public void Celebrate()
    {
        var k = Keeper;
        if (double.IsNaN(k.X) || Width <= 0) return;
        k.State = WanderState.Idle; k.Timer = CelebrateSeconds; k.HopT = 0; k.HappyTimer = 2.5;
        Celebrating = CelebrateSeconds;
        CallAnimals(k);
    }

    void UpdateKeeper(double dt)
    {
        var k = Keeper;
        if (double.IsNaN(k.X)) { k.Y = (MinY + MaxY) / 2; k.X = Clear(Width * 0.5, k.Y); k.FacingRight = true; k.Timer = 2; }
        if (k.HopT >= 0) { k.HopT += dt / HopSeconds; if (k.HopT >= 1) k.HopT = -1; }
        if (k.HappyTimer > 0) k.HappyTimer -= dt;
        if (Celebrating > 0)
        {
            Celebrating -= dt;
            if (Celebrating <= 0) foreach (var w in Animals) w.Welcoming = false;
            k.State = WanderState.Idle;
            return;
        }
        switch (k.State)
        {
            case WanderState.Walk:
                double dx = k.TargetX - k.X, dy = k.TargetY - k.Y, dist = Math.Sqrt(dx * dx + dy * dy), step = KeeperSpeed * dt;
                if (Math.Abs(dx) > 1) k.FacingRight = dx > 0;
                k.WalkTime += dt;
                if (dist > step) { k.X += dx / dist * step; k.Y += dy / dist * step; }
                else { k.X = k.TargetX; k.Y = k.TargetY; k.State = WanderState.Idle; k.Timer = KeeperWalking ? Rand(0.6, 1.6) : Rand(4, 9); }
                break;
            default:
                k.Timer -= dt;
                if (k.Timer > 0) break;
                // Çalışırken çimende uzun uzun yürür; boşta ara sıra birkaç adım atar.
                if (KeeperWalking || rng.NextDouble() < 0.35)
                {
                    double reach = KeeperWalking ? Rand(120, 320) : Rand(40, 110);
                    double dir = k.X > Width * 0.7 ? -1 : k.X < Width * 0.3 ? 1 : (rng.Next(2) == 0 ? -1 : 1);
                    double tx = Math.Clamp(k.X + dir * reach, MinX + 30, MaxX - 30);
                    k.TargetY = KeeperWalking && PathY != null ? Math.Clamp(PathY(tx), MinY, MaxY) : Math.Clamp(k.Y + Rand(-30, 30), MinY + 10, MaxY);
                    k.TargetX = KeeperWalking && PathY != null ? tx : Clear(tx, k.TargetY);
                    k.State = WanderState.Walk;
                }
                else k.Timer = Rand(3, 7);
                break;
        }
        k.X = Math.Clamp(k.X, MinX, MaxX); k.Y = Math.Clamp(k.Y, MinY, MaxY);
    }

    /// <summary>Herkes (uyuyanlar uyanır) gezginin çevresine, iki yanına yayılarak gelir.</summary>
    void CallAnimals(Wanderer v)
    {
        var list = Animals.Where(w => !double.IsNaN(w.X)).OrderBy(w => w.X).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            double side = i % 2 == 0 ? -1 : 1, ring = 1 + i / 2;
            w.Welcoming = true; w.Arriving = false;
            w.Pose = ZooPose.Stand; w.State = WanderState.Walk;
            double row = (i / 2) % 2 == 0 ? -14 : 16;          // önlü arkalı iki sıra
            double baseX = v.X + side * (40 + ring * 74);
            Aim(w, () => (baseX + Rand(-8, 8), v.Y + row + Rand(-4, 4)));
        }
    }

    public void Update(double dt)
    {
        if (Width <= 0 || Height <= 0) return;
        dt = Math.Min(dt, 0.1);
        UpdateKeeper(dt);

        foreach (var w in Animals)
        {
            if (double.IsNaN(w.X)) Place(w);

            if (w.NameTimer > 0) w.NameTimer -= dt;
            if (w.GreetCooldown > 0) w.GreetCooldown -= dt;
            if (w.HappyTimer > 0) w.HappyTimer -= dt;
            if (w.HopT >= 0) { w.HopT += dt / HopSeconds; if (w.HopT >= 1) w.HopT = -1; }

            switch (w.State)
            {
                case WanderState.Idle:
                    w.Timer -= dt;
                    if (Mood == ZooMood.Play && w.HopT < 0 && w.Pose != ZooPose.Sleep && rng.NextDouble() < dt * 0.35) w.HopT = 0; // molada zıp zıp
                    if (w.Timer <= 0) { PickTarget(w); w.State = WanderState.Walk; w.Pose = ZooPose.Stand; }
                    break;

                case WanderState.Greet:
                    w.Timer -= dt;
                    if (w.Timer <= 0) { w.State = WanderState.Idle; w.Timer = Rand(0.4, 1.4); }
                    break;

                case WanderState.Walk:
                    double dx = w.TargetX - w.X, dy = w.TargetY - w.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy), step = Speed * (w.Welcoming ? 2.4 : 1) * dt;   // gezgine koşar adım
                    if (Math.Abs(dx) > 1) w.FacingRight = dx > 0;
                    if (dist <= step)
                    {
                        w.X = w.TargetX; w.Y = w.TargetY;
                        w.State = WanderState.Idle; w.Arriving = false;
                        if (w.Welcoming && Celebrating > 0)
                        {
                            // Gezgine döner, sevinçle zıplar; gezgin de karşılık verir.
                            w.Welcoming = false;
                            w.State = WanderState.Greet; w.Timer = Rand(3.5, 5); w.Pose = ZooPose.Stand;
                            w.HappyTimer = w.Timer; w.HopT = 0; w.GreetCooldown = Rand(10, 16);
                            w.FacingRight = w.X < Keeper.X;
                            Keeper.HopT = 0; Keeper.HappyTimer = 1.5;
                        }
                        else Rest(w);
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
        if (w.Intent == ZooSpotKind.Pond && Pond is { } p)
        {
            w.Intent = ZooSpotKind.None;
            w.FacingRight = w.X < p.X;
            w.Pose = w.Has[(int)ZooPose.Eat] ? ZooPose.Eat : ZooPose.Stand;          // başı eğik: su içiyor
            w.Timer = Rand(4, 8);
            return;
        }
        if (w.Intent == ZooSpotKind.Shade)
        {
            w.Intent = ZooSpotKind.None;
            var shadePose = rng.NextDouble() < 0.6 ? ZooPose.Sit : ZooPose.Sleep;
            w.Pose = w.Has[(int)shadePose] ? shadePose : ZooPose.Stand;
            w.Timer = Rand(10, 22);
            return;
        }
        double r = rng.NextDouble();
        var (pose, min, max) = Mood == ZooMood.Play
                ? (r < 0.75 ? (ZooPose.Stand, 0.6, 2.0) : (ZooPose.Happy, 1.0, 2.0))          // molada: kıpır kıpır
            : Mood == ZooMood.Calm || Night
                ? (r < (Night ? 0.55 : 0.30) ? (ZooPose.Sleep, 20.0, 40.0)                     // çalışırken / gece: sakin
                  : r < 0.70 ? (ZooPose.Sit, 10.0, 22.0)
                  : r < 0.88 ? (ZooPose.Eat, 6.0, 12.0)
                  : (ZooPose.Stand, 3.0, 8.0))
            : r < 0.40 ? (ZooPose.Stand, 2.0, 6.0)
            : r < 0.62 ? (ZooPose.Sit, 6.0, 12.0)
            : r < 0.82 ? (ZooPose.Eat, 4.0, 8.0)
            : (ZooPose.Sleep, 14.0, 28.0);
        if (pose == ZooPose.Happy) { w.HappyTimer = max; w.HopT = 0; pose = ZooPose.Stand; }
        if (!w.Has[(int)pose]) (pose, min, max) = (ZooPose.Stand, 2.0, 6.0);
        w.Pose = pose;
        w.Timer = Rand(min, max);
        if (w.Follow != null && pose != ZooPose.Sleep) w.Timer *= 0.5;       // yavru yerinde duramaz
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
            w.X = Clear(Rand(MinX, MaxX), w.Y);
            for (int i = 0; i < 10 && Crowd(w.X, w.Y, w) < 0; i++) { w.Y = Rand(MinY, MaxY); w.X = Clear(Rand(MinX, MaxX), w.Y); }
            w.FacingRight = rng.Next(2) == 0;
            w.Timer = Rand(0.3, 4);
            w.GreetCooldown = Rand(2, 8);
        }
    }

    void PickTarget(Wanderer w)
    {
        if (Celebrating > 0)
        {
            // Kutlama sürerken gezginin yakınında dolaşırlar.
            Aim(w, () => (Keeper.X + Rand(-170, 170), Keeper.Y + Rand(-34, 34)));
            return;
        }
        if (w.Follow is { } parent && !double.IsNaN(parent.X) && rng.NextDouble() < 0.8)
        {
            // Yavru: annesinin arkasında, biraz yanında durur.
            double behind = parent.FacingRight ? -1 : 1;
            Aim(w, () => (parent.X + behind * Rand(48, 72), parent.Y + Rand(4, 18)));
            return;
        }
        // Su içmeye göle, dinlenmeye ağaç gölgesine gider.
        w.Intent = ZooSpotKind.None;
        if (Mood != ZooMood.Play && w.Follow == null)
        {
            double r = rng.NextDouble();
            if (Pond is { } p && r < 0.16)
            {
                double side = w.X < p.X ? -1 : 1;
                w.Intent = ZooSpotKind.Pond;
                Aim(w, () => (p.X + side * (p.Rx + Rand(20, 34)), p.Y + Rand(-2, 10)));
                return;
            }
            if (Shade is { } sh && r < 0.16 + (Noon ? 0.30 : 0.12))
            {
                w.Intent = ZooSpotKind.Shade;
                Aim(w, () => (sh.X + Rand(-90, 90), sh.Y + Rand(4, 22)));
                return;
            }
        }
        // Ara sıra bir arkadaşının yanına gider; böylece karşılaşmalar kendiliğinden olur.
        var others = Animals.Where(o => o != w && !o.Arriving && !double.IsNaN(o.X)).ToList();
        if (others.Count > 0 && w.GreetCooldown <= 0 && rng.NextDouble() < 0.35)
        {
            var o = others[rng.Next(others.Count)];
            double side = w.X < o.X ? -1 : 1;
            Aim(w, () => (o.X + side * Rand(74, 82), o.Y + Rand(-6, 6)));
            return;
        }
        double ang = Rand(0, Math.PI * 2), d = Mood == ZooMood.Calm ? Rand(30, 100) : Mood == ZooMood.Play ? Rand(90, 280) : Rand(70, 240);
        Aim(w, () => { double a = Rand(0, Math.PI * 2); return (w.X + Math.Cos(a) * d, w.Y + Math.Sin(a) * d * 0.6); });
    }

    /// <summary>Yan yana gelen iki hayvan selamlaşır; üst üste binenlerden biri uzaklaşır.</summary>
    void Meet()
    {
        for (int i = 0; i < Animals.Count; i++)
        for (int j = i + 1; j < Animals.Count; j++)
        {
            var a = Animals[i]; var b = Animals[j];
            if (a.Arriving || b.Arriving || double.IsNaN(a.X) || double.IsNaN(b.X)) continue;
            double dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);

            if (a.Follow == b || b.Follow == a) continue;          // anne-yavru yan yana durabilir
            if (dx < Space(a, b) - 6 && dy < SpaceY - 4)           // iç içe: biri yürüyüp boş bir yere gitsin
            {
                if (a.State == WanderState.Walk || b.State == WanderState.Walk) continue;   // yoldan geçen geçip gider
                var mover = b.Pose != ZooPose.Sleep ? b : a.Pose != ZooPose.Sleep ? a : b;
                if (mover.State == WanderState.Idle) { mover.Pose = ZooPose.Stand; mover.Timer = 0; }
                continue;
            }
            if (a.State == WanderState.Greet || b.State == WanderState.Greet) continue;
            if (a.Welcoming || b.Welcoming) continue;                              // gezgine gidenler yolda oyalanmaz
            if (a.GreetCooldown > 0 || b.GreetCooldown > 0) continue;
            if (a.Pose == ZooPose.Sleep || b.Pose == ZooPose.Sleep) continue;        // uyuyan rahatsız edilmez
            if (dx > 92 || dy > 22) continue;

            foreach (var w in new[] { a, b })
            {
                w.State = WanderState.Greet; w.Timer = 2.4;
                w.Pose = ZooPose.Stand; w.HappyTimer = 2.4;
                w.HopT = 0;
                w.GreetCooldown = Mood == ZooMood.Play ? Rand(5, 10) : Rand(18, 30);
            }
            a.FacingRight = a.X < b.X; b.FacingRight = b.X < a.X;
        }
    }
}
