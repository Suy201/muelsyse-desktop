namespace Muelsyse;

// One reversible authored sequence keeps rapid hide/show requests on the same pose.
internal sealed class KettleMotion
{
    public const double Duration=4.8;
    double origin,started;
    public double Progress {get;private set;}
    public bool TargetHidden {get;private set;}
    public bool Moving=>Progress!=(TargetHidden?1:0);
    public bool Active=>Progress>0||TargetHidden;
    public bool Hidden=>Progress==1&&!Moving;
    public void Request(bool hide,double now)
    {
        Advance(now);
        if(hide==TargetHidden)return;
        origin=Progress;started=now;TargetHidden=hide;
    }
    public void Advance(double now)
    {
        Progress=Math.Clamp(origin+(TargetHidden?1:-1)*Math.Max(0,now-started)/Duration,0,1);
        double target=TargetHidden?1:0;if(Math.Abs(Progress-target)<1e-9)Progress=target;
    }
    public int Frame(int count)=>(int)Math.Round(Progress*(count-1));
    public static float Opacity(double progress)
    {
        double t=progress<.5?(progress-.09)/.14:(.91-progress)/.14;
        t=Math.Clamp(t,0,1);return (float)(1-.32*t*t*(3-2*t));
    }
    public static bool NearEdge(Rectangle bounds,Rectangle area,double scale,int distance)
    {
        double margin=distance*scale;
        return bounds.Left-area.Left<=margin||area.Right-bounds.Right<=margin||bounds.Top-area.Top<=margin||area.Bottom-bounds.Bottom<=margin;
    }
    public static bool HideAfterDrop(bool alreadyHidden,Rectangle bounds,Rectangle area,double scale)
        =>NearEdge(bounds,area,scale,alreadyHidden?64:18);
}

internal sealed partial class PetForm
{
    readonly KettleMotion kettle=new();
    bool pendingKettle;
    string? afterKettleGesture;
    bool WantsKettle=>pendingKettle||kettle.TargetHidden;
    bool KettleBusy=>pendingKettle||kettle.Active;
    void SetKettle(bool hide)
    {
        if(hide==WantsKettle)return;
        card?.Hide();actionPalette?.HidePreview();speech="";notice="";noticeUntil=0;dirty=true;
        if(hide){dashboard?.Hide();actionPalette?.Hide();afterKettleGesture=null;}
        double now=clock.Elapsed.TotalSeconds;
        // Let a finite gesture or gaze-return finish before starting from the neutral pose.
        if(hide&&!kettle.Active&&!brain.Paused&&!dragging){pendingKettle=true;StartClock();return;}
        pendingKettle=false;
        if(!kettle.Active&&hide){brain.ReturnToBase(now);player.ResetPose();}
        BeginKettleTransition(hide,now);
        if(!Visible)Show();
        StartClock();
    }
    void PaintKettle(double now)
    {
        bool wasActive=kettle.Active;
        kettle.Advance(now);
        player.Paint("kettle",kettle.Frame(sprites.Get("kettle").Frames));
        if(wasActive&&!kettle.Active)
        {
            brain.ReturnToBase(now);
            player.ResetPose();
            if(afterKettleGesture is{} action){afterKettleGesture=null;brain.PlayGesture(action,now);}
        }
        if(kettle.Hidden||(!kettle.Active&&brain.Paused))StopClock();
    }
    void FinishDrop()
    {
        bool hidden=KettleBusy;
        var dropped=Bounds;var area=Screen.FromRectangle(dropped).WorkingArea;
        if(brain.Dragging)brain.EndDrag(clock.Elapsed.TotalSeconds);
        ClampPosition();SaveSettings();
        bool hide=KettleMotion.HideAfterDrop(hidden,dropped,area,scale);
        if(hide||hidden)SetKettle(hide);
    }
}
