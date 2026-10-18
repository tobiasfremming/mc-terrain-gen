using UnityEngine;

// Point-mass flight of a winged creature's girdle (design section 1 and 3). Pure C#, allocation-free,
// deterministic. Port of wyvern_proto/proto.py, made 3D (bank, heading) and Froude-scaled.
//
// A sub-step (Step):
//   1. Control: altitude / speed / turn-rate goals -> stroke demand, amplitude, downstroke rate, thrust,
//      left/right asymmetry (banking).
//   2. CreatureWings.Advance: the wingbeat state machine moves each wing's stroke angle and reports the
//      wrist's velocity relative to the girdle (Rise, along the banked up), the stroke normal tilt and
//      the effective area.
//   3. Integrate: gravity, parasitic drag and glide lift (explicit), then the implicit air plant per wing:
//        kappa = kGrip A max(|v|, vGripMin) / scale,  g = kappa h / (1 + kappa h),
//        v <- v - g ((v + rise) . n) n
//      g = 1 is a planted foot: the girdle rises exactly as fast as the wrist is driven down. g < 1 slips.
//
// Frames: Up is world up (Phase B injects PlanetGravity.UpAt), Forward the horizontal heading (from the
// velocity), Right = Up x Forward, BankedUp = Up rotated toward Right by Bank (a right bank tilts the lift
// to the right). Coordinated turns emerge: the tilted support turns the velocity at g tan(bank) / v.
public sealed class FlightModel
{
    public const float G = 9.81f;
    const float kMinHeadingSpeed = 0.05f;

    readonly FlightSpec f;
    public readonly float SizeScale, TimeScale;

    public Vector3 Position, Velocity;
    public Vector3 Up = Vector3.up;
    public float GravityScale = 1f;   // tests: 0 isolates the air plant
    public float GripScale = 1f;      // tests: the prototype's gripScale sweep

    // goals (world units: metres, m/s, rad/s; + = right turn)
    public float AltitudeGoal, SpeedGoal, TurnRateGoal;

    // state
    public float Bank, RollRate;      // rad, rad/s
    public Vector3 Forward { get; private set; }
    public Vector3 Right { get; private set; }
    public Vector3 BankedUp { get; private set; }

    // controller outputs (Control)
    public float Climb { get; private set; }      // -1..1
    public float Thrust { get; private set; }     // -1..1
    public float Demand { get; private set; }     // 0..1
    public float Amplitude { get; private set; }  // fraction of the comfort band
    public float StrokeRate { get; private set; } // downstroke rad/s (scaled)
    public float Asymmetry { get; private set; }  // + = left wing strokes harder (rolls right)
    public bool Gliding { get; private set; }

    // last sub-step, per wing: push along the stroke normal (m/s^2), for roll and the debug view
    public float PushLeft { get; private set; }
    public float PushRight { get; private set; }

    public FlightSpec Spec => f;
    float altitudeIntegral;   // dimensionless: altitude error / scale x time / sqrt(scale)

    public FlightModel(FlightSpec spec, float sizeScale, Vector3 position, Vector3 velocity, Vector3 forward)
    {
        f = spec;
        SizeScale = Mathf.Max(1e-3f, sizeScale);
        TimeScale = Mathf.Sqrt(SizeScale);
        Position = position; Velocity = velocity;
        Forward = Horizontal(forward, Vector3.forward);
        UpdateFrame();
        AltitudeGoal = Altitude;
        SpeedGoal = f.cruiseSpeed * TimeScale;
        Amplitude = f.amplitudeMin;
        StrokeRate = f.strokeRateMin / TimeScale;
    }

    // Restart at a new state (a launch): bank, roll and the altitude integrator cleared.
    public void Reset(Vector3 position, Vector3 velocity, Vector3 forward, Vector3 up)
    {
        Position = position; Velocity = velocity;
        Up = up.sqrMagnitude > 1e-12f ? up.normalized : Vector3.up;
        Bank = 0f; RollRate = 0f; altitudeIntegral = 0f; Asymmetry = 0f;
        Forward = Horizontal(forward, Vector3.forward);
        UpdateFrame();
    }

    public float Altitude => Vector3.Dot(Position, Up);
    public float ForwardSpeed => Vector3.Dot(Velocity, Forward);
    public float VerticalSpeed => Vector3.Dot(Velocity, Up);
    public float HorizontalSpeed => Vector3.ProjectOnPlane(Velocity, Up).magnitude;

    // Amplitude of the wing on `side` (-1 left, +1 right).
    public float WingAmplitude(int side) => Amplitude * (1f - side * Asymmetry);

    public static float GripFraction(float kappa, float h) => kappa * h / (1f + kappa * h);

    public float GripRate(float area, float speed) =>
        GripScale * f.kGrip * area * Mathf.Max(speed, f.vGripMin * TimeScale) / SizeScale;

    // One sub-step: control, wingbeat, integration.
    public void Step(float h, CreatureWings wings)
    {
        if (!(h > 0f)) return;
        Control(h);
        wings.Advance(h, this);
        Integrate(h, wings);
    }

    // h > 0 advances the altitude integrator (h = 0: evaluate only).
    public void Control(float h = 0f)
    {
        float T = TimeScale, s = SizeScale;
        float err = (AltitudeGoal - Altitude) / s;
        // integral term: removes the PD's steady sag below the goal (the stroke floor and the bob's
        // vertical-speed damping leave it ~13 cm low otherwise); clamped against wind-up
        if (h > 0f && f.climbIntegral > 0f)
        {
            float lim = f.climbIntegralLimit / f.climbIntegral;
            altitudeIntegral = Mathf.Clamp(altitudeIntegral + err * h / T, -lim, lim);
        }
        float c = Mathf.Clamp(f.climbGain * err - f.climbDamping * VerticalSpeed / T + f.climbIntegral * altitudeIntegral, -1f, 1f);
        float thr = Mathf.Clamp(f.thrustGain * (SpeedGoal - ForwardSpeed) / T, -1f, 1f);
        float floor = c > f.descendCutoff ? f.demandFloor : 0f;
        float demand = Mathf.Clamp01(Mathf.Max(c, Mathf.Max(0.5f * Mathf.Abs(thr), floor)));
        Climb = c; Thrust = thr; Demand = demand;
        Gliding = demand < f.glideDemand;
        Amplitude = f.amplitudeMin + (1f - f.amplitudeMin) * demand;
        StrokeRate = (f.strokeRateMin + (f.strokeRateMax - f.strokeRateMin) * demand) / T;

        // bank toward the coordinated-turn angle for the turn-rate goal; asymmetry is a PD on the bank
        float maxBank = f.maxBankDeg * Mathf.Deg2Rad;
        float v = Mathf.Max(HorizontalSpeed, f.minSpeed * T);
        float target = Mathf.Clamp(Mathf.Atan(TurnRateGoal * v / G), -maxBank, maxBank);
        Asymmetry = Mathf.Clamp(f.bankGain * (target - Bank) - f.bankDamping * RollRate * T, -f.maxAsymmetry, f.maxAsymmetry);
    }

    public void Integrate(float h, CreatureWings wings)
    {
        float s = SizeScale;
        float sp = Velocity.magnitude;
        float fwd = ForwardSpeed;

        // explicit: gravity, parasitic drag, glide lift of the spread wings (per wing, by its fold)
        float spread = 0f;
        int n = wings.Count;
        for (int w = 0; w < n; w++) spread += 1f - wings.State[w].fold;
        spread = n > 0 ? spread / n : 0f;
        Vector3 acc = -G * GravityScale * Up - (f.kDrag / s) * sp * Velocity + (f.kLift / s) * spread * fwd * fwd * BankedUp;
        Velocity += acc * h;

        // Implicit air plant per wing (the prototype's sequential update): kappa = GripRate, g = kappa h /
        // (1 + kappa h), v <- v - g ((v + rise) . n) n. A sequential solve depends on the order (the first
        // wing takes the whole grip when it is large, which would roll the body), so it runs in both orders
        // and averages: symmetric left/right, and identical to the prototype for symmetric wings.
        float pushL = 0f, pushR = 0f;
        if (n > 0)
        {
            Vector3 v0 = Velocity, sumV = Vector3.zero;
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 v = v0;
                for (int k = 0; k < n; k++)
                {
                    int w = pass == 0 ? k : n - 1 - k;
                    ref WingState st = ref wings.State[w];
                    Vector3 nrm = Mathf.Cos(st.tilt) * BankedUp + Mathf.Sin(st.tilt) * Forward;
                    float g = GripFraction(GripRate(st.area, sp), h);
                    float un = Vector3.Dot(v, nrm) + st.rise * Vector3.Dot(BankedUp, nrm);
                    v -= (g * un) * nrm;
                    float push = -0.5f * g * un / h;
                    if (wings.Side(w) < 0) pushL += push; else pushR += push;
                }
                sumV += v;
            }
            Velocity = 0.5f * sumV;
        }
        PushLeft = pushL; PushRight = pushR;

        // roll: torque from the left/right push difference, dihedral damping
        float rollAcc = f.rollGain * (pushL - pushR) / s - f.rollDamping / TimeScale * RollRate;
        RollRate += rollAcc * h;
        Bank += RollRate * h;
        float lim = 1.5f * f.maxBankDeg * Mathf.Deg2Rad;
        if (Bank > lim) { Bank = lim; if (RollRate > 0f) RollRate = 0f; }
        else if (Bank < -lim) { Bank = -lim; if (RollRate < 0f) RollRate = 0f; }

        Position += Velocity * h;
        UpdateFrame();
    }

    void UpdateFrame()
    {
        Vector3 u = Up.sqrMagnitude > 1e-12f ? Up.normalized : Vector3.up;
        Up = u;
        Vector3 hv = Vector3.ProjectOnPlane(Velocity, u);
        if (hv.sqrMagnitude > kMinHeadingSpeed * kMinHeadingSpeed) Forward = hv.normalized;
        else Forward = Horizontal(Forward, Vector3.forward);
        Right = Vector3.Cross(u, Forward);
        BankedUp = Mathf.Cos(Bank) * u + Mathf.Sin(Bank) * Right;
    }

    Vector3 Horizontal(Vector3 v, Vector3 fallback)
    {
        Vector3 u = Up.sqrMagnitude > 1e-12f ? Up.normalized : Vector3.up;
        Vector3 t = Vector3.ProjectOnPlane(v, u);
        if (t.sqrMagnitude < 1e-10f) t = Vector3.ProjectOnPlane(fallback, u);
        if (t.sqrMagnitude < 1e-10f) t = Vector3.ProjectOnPlane(Vector3.right, u);
        return t.normalized;
    }
}
