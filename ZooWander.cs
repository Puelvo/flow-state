namespace FlowState;

public enum WanderState { Idle, Walk, Greet, Held }

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
    public double Companion;              // > 0: gezginin peşinde dolaşıyor (selamlaştılar)
    public double Dodge;                  // > 0: jeep'e yol veriyor
    public ZooSpotKind Intent;            // gittiği yer: gölet (su içer) ya da ağaç gölgesi (dinlenir)

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
    public const double HoldLift = 16;     // tutulan hayvanın yerden yüksekliği

    public readonly List<Wanderer> Animals = new();

    /// <summary>Bahçede hep olan gezgin (kullanıcı). Hayvan listesinde değildir.</summary>
    public readonly Wanderer Keeper = new() { Id = "explorer" };
    public ZooMood Mood = ZooMood.Free;
    public bool Night;                     // gece: hayvanlar uykucu
    public bool SessionOn;                 // oturum var (mola dahil): gezgin jeep'tedir
    public bool KeeperWalking;             // oturum çalışıyor: jeep patikada gezer
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
        if (!double.IsNaN(JeepX) && Math.Abs(y - JeepY) < 30) worst = Math.Min(worst, Math.Abs(x - JeepX) - 112);
        if (!Riding && !double.IsNaN(Keeper.X) && Math.Abs(y - Keeper.Y) < SpaceY)
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

    /// <summary>Taşıma başlar: hayvan ne yapıyorsa bırakır, havaya kalkar.</summary>
    public void Grab(Wanderer w)
    {
        if (double.IsNaN(w.X)) return;
        w.State = WanderState.Held;
        w.Arriving = w.Welcoming = false;
        w.Intent = ZooSpotKind.None;
        w.Pose = ZooPose.Stand;
        w.HopT = -1; w.NameTimer = 0;
    }

    /// <summary>Tutulan hayvanı ayaklarının basacağı noktaya taşır (sahne sınırları içinde).</summary>
    public void DragTo(Wanderer w, double x, double y)
    {
        if (!w.Held) return;
        x = Math.Clamp(x, MinX, MaxX); y = Math.Clamp(y, MinY, MaxY);
        if (Math.Abs(x - w.X) > 2) w.FacingRight = x > w.X;
        w.X = x; w.Y = y;
    }

    /// <summary>
    /// Bırakılan hayvan sevinir ve bir süre yeni yerinde kalır, sonra dolaşmaya devam eder.
    /// Gölete, ağaca, düğmelerin önüne ya da başka birinin üstüne bırakılırsa hemen yanındaki boş yere yürür.
    /// </summary>
    public void Drop(Wanderer w)
    {
        if (!w.Held) return;
        w.State = WanderState.Idle;
        w.HappyTimer = 0.8;
        w.Timer = Rand(6, 10);
        w.GreetCooldown = Math.Max(w.GreetCooldown, 3);
        if (Math.Abs(Clear(w.X, w.Y) - w.X) < 1 && Crowd(w.X, w.Y, w) >= 0) return;
        double x = w.X, y = w.Y, reach = 0;
        Aim(w, () => { reach += 14; return (x + Rand(-reach, reach), y + Rand(-reach, reach) * 0.5); });
        w.State = WanderState.Walk;
    }

    /// <summary>Oturum bitti: herkes gezginin etrafına toplanıp onu selamlar; gezgin sevinçle zıplar.</summary>
    public void Celebrate()
    {
        var k = Keeper;
        if (double.IsNaN(k.X) || Width <= 0) return;
        if (Riding && !JeepInside) { celebrateWaiting = true; return; }      // jeep ekranın dışında: önce geri girsin
        if (Riding) StepOut();
        celebrateWaiting = false;
        visiting = null;
        k.State = WanderState.Idle; k.Timer = CelebrateSeconds; k.HopT = 0; k.HappyTimer = 2.5;
        Celebrating = CelebrateSeconds;
        CallAnimals(k);
    }

    void UpdateKeeper(double dt)
    {
        var k = Keeper;
        bool fresh = double.IsNaN(k.X);
        if (fresh) { k.Y = (MinY + MaxY) / 2; k.X = Clear(Width * 0.5, k.Y); k.FacingRight = true; k.Timer = 2; }
        if (k.HopT >= 0) { k.HopT += dt / HopSeconds; if (k.HopT >= 1) k.HopT = -1; }
        if (k.HappyTimer > 0) k.HappyTimer -= dt;

        if (SessionOn && !double.IsNaN(JeepX))
        {
            // Oturum başladı: (kutlama sürüyorsa biter) gezgin jeep'ine koşar ve biner.
            if (Celebrating > 0) { Celebrating = 0; foreach (var w in Animals) w.Welcoming = false; }
            visiting = null; celebrateWaiting = false;
            if (fresh) Riding = true;                              // uygulama süren oturumla açıldı
            if (!Riding)
            {
                (k.TargetX, k.TargetY) = (JeepX, JeepY + 2);
                if (StepTo(k, KeeperSpeed * 3.2, dt)) { Riding = true; k.State = WanderState.Idle; }
            }
            if (Riding) { k.X = JeepX; k.Y = JeepY; k.FacingRight = JeepRight; }
            return;
        }
        if (Riding)
        {
            if (!JeepInside) { k.X = JeepX; k.Y = JeepY; return; }       // ekranın dışındaysa girene kadar sürer
            StepOut();
            if (celebrateWaiting) Celebrate();
        }

        if (Celebrating > 0)
        {
            Celebrating -= dt;
            if (Celebrating <= 0) foreach (var w in Animals) w.Welcoming = false;
            k.State = WanderState.Idle;
            return;
        }

        // Yaya: hayvanların arasında gezer, birinin yanına gidip selamlaşır; selamlaşan bir süre peşinden gelir.
        if (visiting is { } v && (v.Held || v.Pose == ZooPose.Sleep || !Animals.Contains(v) || (visitTime -= dt) < 0)) { visiting = null; k.State = WanderState.Idle; k.Timer = 1; }
        switch (k.State)
        {
            case WanderState.Walk:
                if (visiting is { } a)
                {
                    if (Math.Abs(a.X - k.X) < 150 && a.State == WanderState.Walk && !a.Arriving) { a.State = WanderState.Idle; a.Timer = 4; }   // onu bekler
                    k.TargetX = Math.Clamp(a.X + (k.X < a.X ? -58 : 58), MinX, MaxX);
                    k.TargetY = Math.Clamp(a.Y + 2, MinY, MaxY);
                }
                if (!StepTo(k, KeeperSpeed, dt)) break;
                k.State = WanderState.Idle; k.Timer = Rand(1.5, 4);
                if (visiting is { } m)
                {
                    visiting = null;
                    m.State = WanderState.Greet; m.Timer = Rand(2.6, 3.6); m.Pose = ZooPose.Stand; m.HappyTimer = m.Timer; m.HopT = 0;
                    m.GreetCooldown = Rand(12, 20); m.Companion = Rand(16, 28);
                    m.FacingRight = m.X < k.X; k.FacingRight = k.X < m.X;
                    k.HopT = 0; k.HappyTimer = 1.5; k.Timer = m.Timer + Rand(0.4, 1.4);
                }
                break;
            default:
                k.Timer -= dt;
                if (k.Timer > 0) break;
                var awake = Animals.Where(w => !double.IsNaN(w.X) && !w.Held && !w.Arriving && w.Pose != ZooPose.Sleep && w.State != WanderState.Greet && w.Companion <= 0).ToList();
                if (awake.Count > 0 && rng.NextDouble() < 0.5) { visiting = awake[rng.Next(awake.Count)]; visitTime = 14; }
                else
                {
                    double dir = k.X > Width * 0.7 ? -1 : k.X < Width * 0.3 ? 1 : (rng.Next(2) == 0 ? -1 : 1);
                    k.TargetY = Math.Clamp(k.Y + Rand(-60, 60), MinY + 10, MaxY);
                    k.TargetX = Clear(Math.Clamp(k.X + dir * Rand(110, 280), MinX + 30, MaxX - 30), k.TargetY);
                    // Yanındaki dostlar da yürümeye başlar.
                    foreach (var w in Animals) if (w.Companion > 0 && w.State == WanderState.Idle && w.Pose != ZooPose.Sleep) w.Timer = Math.Min(w.Timer, Rand(0.2, 1));
                }
                k.State = WanderState.Walk;
                break;
        }
        k.X = Math.Clamp(k.X, MinX, MaxX); k.Y = Math.Clamp(k.Y, MinY, MaxY);
    }

    Wanderer? visiting;                    // gezginin yanına gittiği hayvan
    double visitTime;

    /// <summary>Hedefe bir adım atar; vardıysa true.</summary>
    static bool StepTo(Wanderer k, double speed, double dt)
    {
        double dx = k.TargetX - k.X, dy = k.TargetY - k.Y, dist = Math.Sqrt(dx * dx + dy * dy), step = speed * dt;
        if (Math.Abs(dx) > 1) k.FacingRight = dx > 0;
        k.State = WanderState.Walk;
        k.WalkTime += dt * Math.Max(1, speed / KeeperSpeed * 0.6);
        if (dist <= step) { k.X = k.TargetX; k.Y = k.TargetY; return true; }
        k.X += dx / dist * step; k.Y += dy / dist * step;
        return false;
    }

    // ---------- jeep ----------

    /// <summary>Gezgin jeep'te mi. Oturum başlayınca biner, bitince iner; molada jeep durur ama içinde kalır.</summary>
    public bool Riding;
    public double JeepX = double.NaN, JeepY, JeepV, JeepRoll;   // konum (tekerleklerin bastığı nokta), hız, gidilen yol
    public bool JeepRight = true;
    public const double JeepSpeed = 64;
    double jeepBlocked;
    bool celebrateWaiting;                 // kutlama, jeep ekrana girince başlayacak
    const double JeepOut = 130;            // ekran kenarından bu kadar ötede tamamen görünmez
    bool JeepInside => JeepX >= JeepMin && JeepX <= JeepMax;
    double JeepMin => Math.Min(110, Width / 2);
    double JeepMax => Math.Max(JeepMin, Width - 110);

    void StepOut()
    {
        Riding = false;
        Keeper.X = Math.Clamp(JeepX + (JeepRight ? -24 : 24), MinX, MaxX);
        Keeper.Y = Math.Clamp(JeepY + 30, MinY, MaxY);
        Keeper.State = WanderState.Idle; Keeper.Timer = 1.5; Keeper.HopT = 0;
    }

    void UpdateJeep(double dt)
    {
        if (PathY == null) return;
        if (double.IsNaN(JeepX)) JeepX = Width * 0.3;
        JeepRight = true;
        // Sağdan çıkar, soldan yeniden girer. Mola ya da bitişte ekranın dışındaysa hızlanıp girene kadar sürer, sonra durur.
        bool go = Riding && SessionOn && KeeperWalking, returning = Riding && !go && !JeepInside;
        double target = returning ? JeepSpeed * 2.5 : go && !Blocked(dt) ? JeepSpeed : 0;
        JeepV += Math.Clamp(target - JeepV, -150 * dt, (returning ? 220 : 55) * dt);
        JeepX += JeepV * dt;
        if (JeepX > Width + JeepOut) JeepX = -JeepOut;
        if (!Riding) JeepX = Math.Clamp(JeepX, JeepMin, JeepMax);
        JeepRoll += JeepV * dt;
        JeepY = Math.Clamp(PathY(JeepX), MinY, MaxY);
    }

    /// <summary>Yoldaki hayvanlar kenara çekilir; yakındaysa jeep bekler (en fazla birkaç saniye).</summary>
    bool Blocked(double dt)
    {
        bool stop = false;
        double dir = JeepRight ? 1 : -1;
        foreach (var w in Animals)
        {
            if (double.IsNaN(w.X) || w.Held) continue;
            double ahead = (w.X - JeepX) * dir, road = PathY!(w.X);
            if (ahead < -30 || ahead > 210 || Math.Abs(w.Y - road) > 30) continue;
            if (w.Dodge <= 0)
            {
                double side = w.Y >= road ? 1 : -1;
                if (road + side * 56 < MinY || road + side * 56 > MaxY) side = -side;
                double y = road + side * Rand(50, 72), x = w.X + dir * Rand(0, 40);
                Aim(w, () => (x + Rand(-40, 40), y));
                w.State = WanderState.Walk; w.Pose = ZooPose.Stand; w.Arriving = false; w.Intent = ZooSpotKind.None; w.Dodge = 3;
            }
            if (ahead > 0 && ahead < 125) stop = true;
        }
        jeepBlocked = stop ? jeepBlocked + dt : 0;
        return stop && jeepBlocked < 3.5;
    }

    /// <summary>Herkes (uyuyanlar uyanır) gezginin çevresine, iki yanına yayılarak gelir.</summary>
    void CallAnimals(Wanderer v)
    {
        var list = Animals.Where(w => !double.IsNaN(w.X) && !w.Held).OrderBy(w => w.X).ToList();
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
        UpdateJeep(dt);
        UpdateKeeper(dt);

        foreach (var w in Animals)
        {
            if (double.IsNaN(w.X)) Place(w);

            if (w.NameTimer > 0) w.NameTimer -= dt;
            if (w.GreetCooldown > 0) w.GreetCooldown -= dt;
            if (w.HappyTimer > 0) w.HappyTimer -= dt;
            if (w.Companion > 0) w.Companion = SessionOn ? 0 : w.Companion - dt;
            if (w.Dodge > 0) w.Dodge -= dt;
            if (w.HopT >= 0) { w.HopT += dt / HopSeconds; if (w.HopT >= 1) w.HopT = -1; }

            switch (w.State)
            {
                case WanderState.Held:
                    break;                                     // konumu fare belirler

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
                    double dist = Math.Sqrt(dx * dx + dy * dy), step = Speed * (w.Welcoming ? 2.4 : w.Dodge > 0 ? 2.8 : w.Companion > 0 ? 1.4 : 1) * dt;   // gezgine koşar adım
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

            if (!w.Arriving && !w.Held) { w.X = Math.Clamp(w.X, MinX, MaxX); w.Y = Math.Clamp(w.Y, MinY, MaxY); }
        }

        Meet();
    }

    /// <summary>Yürüyüş bitince ne yapacağını seçer: ayakta bekler, oturur, bir şeyler yer ya da uyur.</summary>
    void Rest(Wanderer w)
    {
        if (w.Companion > 0) { w.Pose = ZooPose.Stand; w.Timer = Rand(1, 2.5); return; }
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
        if (w.Companion > 0 && !double.IsNaN(Keeper.X))
        {
            // Gezginle birlikte yürür: gittiği yerin yanında bir nokta.
            var (kx, ky) = Keeper.State == WanderState.Walk ? (Keeper.TargetX, Keeper.TargetY) : (Keeper.X, Keeper.Y);
            Aim(w, () => (kx + (rng.Next(2) == 0 ? -1 : 1) * Rand(64, 120), ky + Rand(-18, 18)));
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
            if (a.Arriving || b.Arriving || a.Held || b.Held || double.IsNaN(a.X) || double.IsNaN(b.X)) continue;
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
