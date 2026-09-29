namespace Muelsyse;

internal record Gesture(string Id,string Label,double Duration);

// Time is supplied by the window, making timing independent of render frequency.
internal sealed class Behavior(int seed = 0)
{
    public static readonly IReadOnlyList<Gesture> Gestures=Array.AsReadOnly(new Gesture[]{
        new("smile","微笑",1.8),new("tilt","歪头",1.5),new("hair","整理头发",2),new("wave","挥手招呼",2.4),
        new("leaf","整理叶饰",4),new("water-observe","观察掌心水滴",4.2),new("drink","捧杯喝水",5.4),new("stretch","轻轻伸展",4.4),
        new("leaf-breeze","叶尖听风",4.8),new("dew-hands","掌心蓄露",5.2),new("waiting-breeze","等一阵微风",5.6),new("offer-candy","递一颗糖",4.8)});
    private readonly Random random = seed == 0 ? new() : new(seed);
    private double nextIdle = -1, nearSince = -1, farSince = -1, lastClick = -10, actionUntil;
    private string lastIdle = "", taskState = "idle";
    private (string Action,double Duration)? pending;private double returnUntil;
    public string Action { get; private set; } = "idle";
    public double ActionStarted { get; private set; }
    public bool Looking { get; private set; }
    public int Direction { get; private set; }
    public bool Dragging { get; private set; }
    public bool Paused { get; set; }
    public double LastInteraction { get; private set; }
    public string TaskState => taskState;
    public string? PendingAction=>pending?.Action;
    public event Action<string>? ActionChanged;

    public void SetTask(string state, double now)
    {
        if (state == taskState) return;
        taskState = state;
        if (Dragging || Paused || actionUntil>now) return;
        Looking = false;
        if (state == "completed") Start("complete", now, 1.8);
        else if (state == "failed") Start("failed", now, 2.4);
        else Start(BaseAction(), now);
    }
    public void React(string state,double now)
    {
        Request(state=="completed"?"complete":state=="failed"?"failed":"waiting",now,state=="completed"?1.8:2.4);
    }
    public bool Busy(double now)=>Dragging||Paused||actionUntil>now||pending!=null;
    public void Request(string action,double now,double duration)
    {
        if(Paused)return;
        if(Dragging){pending=(action,duration);return;}
        if(actionUntil>now){pending=(action,duration);return;}
        if(Looking){Looking=false;nearSince=-1;returnUntil=now+.27;pending=(action,duration);return;}
        Start(action,now,duration);
    }
    private string BaseAction() => taskState switch { "thinking" => "working", "waiting" => "waiting", _ => "idle" };
    private void Start(string action, double now, double duration = 0)
    {
        Action = action; ActionStarted = now; actionUntil = duration > 0 ? now + duration : 0;
        ActionChanged?.Invoke(action);
    }
    public bool Greet(double now)
    {
        LastInteraction = now; nextIdle = now + random.Next(10, 21);
        if (Paused || Dragging || now - lastClick < 3) return false;
        lastClick = now; nearSince = -1;
        Request("greet", now, 2.4); return true;
    }
    public void BeginDrag(double now, bool right)
    {
        Dragging = true; Looking = false; pending=null;returnUntil=0;LastInteraction = now;
        Start(right ? "running-right" : "running-left", now);
    }
    public void EndDrag(double now)
    {
        Dragging = false; Looking = false; nearSince = -1;
        LastInteraction = now; nextIdle = now + random.Next(10, 21); Start(BaseAction(), now);
    }
    public static double GestureDuration(string action)=>Gestures.FirstOrDefault(g=>g.Id==action)?.Duration??2.4;
    public bool PlayGesture(string action,double now)
    {
        var gesture=Gestures.FirstOrDefault(g=>g.Id==action);if(gesture==null||Dragging)return false;
        Paused=false;LastInteraction=now;lastIdle=action;
        Request(action,now,gesture.Duration);return true;
    }
    public void Tick(double now, double x, double y, double distance)
    {
        if (Paused || Dragging) return;
        if(nextIdle<0)nextIdle=now+random.Next(10,21);
        if (actionUntil > 0)
        {
            if (now < actionUntil) return;
            Start(BaseAction(), now);
            nextIdle = now + random.Next(10, 21);
        }
        if(pending is{} queued)
        {
            if(now<returnUntil)return;
            pending=null;returnUntil=0;Start(queued.Action,now,queued.Duration);return;
        }
        bool near = distance <= (Looking ? 140 : 100);
        if (near)
        {
            farSince = -1;
            if (nearSince < 0) nearSince = now;
            if (now - nearSince >= .12 && Math.Sqrt(x*x + y*y) > 12)
            {
                double angle = (Math.Atan2(x, -y) * 180 / Math.PI + 360) % 360;
                int wanted = (int)Math.Round(angle / 22.5, MidpointRounding.AwayFromZero) % 16;
                double delta = Math.Abs((angle - Direction * 22.5 + 540) % 360 - 180);
                if (!Looking || delta > 15) Direction = wanted;
                Looking = true; LastInteraction = now;
                return;
            }
        }
        else
        {
            nearSince = -1;
            if (Looking)
            {
                if (farSince < 0) farSince = now;
                if (now - farSince < .25) return;
                Looking = false; Start(BaseAction(), now); nextIdle = now + random.Next(10, 21);
            }
        }
        if (Looking || taskState is "thinking" or "waiting" || now < nextIdle) return;
        // All approved gestures share the idle pool; do not repeat the last choice.
        var candidates = Gestures.Select(g=>g.Id)
            .Where(a => a != lastIdle).ToArray();
        string chosen = candidates[random.Next(candidates.Length)];
        lastIdle = chosen;
        Start(chosen, now, GestureDuration(chosen));
    }
}
