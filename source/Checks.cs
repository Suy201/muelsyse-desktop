using System.IO;
using System.Text.Json;

namespace Muelsyse;

internal static class Checks
{
    public static void Run(string[] args)
    {
        List<string> passed=[];
        void Check(bool result,string name){if(!result)throw new InvalidOperationException(name);passed.Add(name);}
        var dialogue=new InteractionLines(42);var lines=Enumerable.Range(0,600).Select(_=>dialogue.Next()).ToArray();
        Check(lines.Zip(lines.Skip(1),(a,b)=>a!=b).All(s=>s),"click dialogue never immediately repeats");
        Check(lines.ToHashSet().SetEquals(InteractionLines.Lines),"every click dialogue can be selected");
        Check(InteractionLines.Lines.All(s=>s.Length<=25),"click dialogue fits the existing speech bubble");
        Check(CodexLaunch.ParseChord("Ctrl+Alt+V")!.SequenceEqual(new ushort[]{0x11,0x12,0x56}),"configured realtime voice chord parses without sending input");
        Check(CodexLaunch.ParseChord("CommandOrControl+Shift+F12")!.SequenceEqual(new ushort[]{0x11,0x10,0x7b}),"Codex accelerator modifiers and function keys supported");
        Check(new string?[]{null,"","V","Alt","Ctrl+Ctrl+V","Ctrl+A+B","Ctrl+Enter"}.All(s=>CodexLaunch.ParseChord(s)==null),"unknown and incomplete voice shortcuts fail closed");
        Check(CodexLaunch.IsCodexFrontend(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.915.4065.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe")&&!CodexLaunch.IsCodexFrontend(@"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1.0\app\ChatGPT.exe")&&!CodexLaunch.IsCodexFrontend(null),"voice targets the verified Codex package rather than another ChatGPT executable");
        var b=new Behavior(1);
        b.Tick(0,100,-100,10);Check(!b.Looking,"proximity requires dwell");
        b.Tick(.13,100,-100,10);Check(b.Looking&&b.Direction==2,"near mouse selects northeast");
        b.Tick(.2,100,-100,120);Check(b.Looking,"distance hysteresis retains gaze");
        b.Tick(.3,500,500,200);Check(b.Looking,"exit delay prevents flicker");
        b.Tick(.56,500,500,200);Check(!b.Looking,"far mouse exits gaze");
        for(int i=0;i<16;i++)
        {
            var g=new Behavior(2);double a=i*Math.PI/8;
            g.Tick(0,100*Math.Sin(a),-100*Math.Cos(a),30);g.Tick(.2,100*Math.Sin(a),-100*Math.Cos(a),30);
            Check(g.Looking&&g.Direction==i,$"direction {i} reachable");
        }
        b=new Behavior(3);b.Greet(1);Check(!b.Greet(1.3)&&b.Action=="greet","click cooldown");
        b.Tick(2,100,0,10);Check(!b.Looking&&b.Action=="greet","greeting finishes without mouse interruption");
        b.BeginDrag(3,true);Check(b.Dragging&&b.Action=="running-right","drag priority");
        b.EndDrag(4);Check(!b.Dragging&&b.Action=="idle","drag returns to idle");
        b.SetTask("thinking",5);b.Tick(6,100,0,0);Check(b.Action=="working"&&!b.Looking,"task work while proximity dwell pending");
        b.Tick(6.2,100,0,0);Check(b.Looking&&b.TaskState=="thinking","mouse gaze works while Codex is busy");
        b.Tick(6.3,500,500,500);b.Tick(6.6,500,500,500);Check(!b.Looking&&b.Action=="working","mouse departure restores work animation");
        b.Greet(7);b.Tick(7.3,500,500,500);Check(b.Action=="greet","user click may greet during work");b.Tick(10,100,0,0);Check(b.Action=="working","greeting restores task state");
        b.SetTask("completed",11);Check(b.Action=="complete","completion reaction");b.Tick(14,500,500,500);Check(b.Action=="idle","completion returns to idle");
        var history=new List<(string Name,double At)>();b=new Behavior(42);double clock=0;
        b.ActionChanged+=s=>{if(s!="idle")history.Add((s,clock));};
        for(clock=0;clock<900;clock+=1.0/30){b.Tick(clock,500,500,500);if(b.Looking)throw new Exception("automatic gaze without mouse");}
        Check(history.Select(h=>h.Name).ToHashSet().SetEquals(new[]{"smile","tilt","hair","wave","leaf","water-observe","drink","stretch","leaf-breeze","dew-hands","waiting-breeze","offer-candy"}),"all twelve approved idle gestures scheduled");
        Check(history[0].At>=10&&history[0].At<=20.04,"first idle gesture waits ten to twenty seconds");
        Check(history.Zip(history.Skip(1),(x,y)=>x.Name!=y.Name).All(x=>x),"no consecutive idle gesture repeat");
        Check(history.Zip(history.Skip(1),(x,y)=>y.At-x.At-Behavior.GestureDuration(x.Name)).All(gap=>gap>=10&&gap<=20.08),"ten to twenty quiet seconds after every complete idle gesture");
        Check(history.Zip(history.Skip(1),(x,y)=>Math.Round(y.At-x.At-Behavior.GestureDuration(x.Name))).Distinct().Count()>1,"quiet intervals vary randomly");
        foreach(string state in new[]{"thinking","waiting"})
        {
            var occupied=new Behavior(42);occupied.SetTask(state,0);
            for(int second=0;second<300;second++)occupied.Tick(second,500,500,500);
            Check(occupied.Action==(state=="thinking"?"working":"waiting"),state+" suppresses random idle gestures");
        }
        b=new Behavior(4){Paused=true};b.Tick(100,100,0,0);Check(b.Action=="idle"&&!b.Looking,"pause stops behavior");
        using var doc=JsonDocument.Parse("""{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"usedPercent":37,"windowDurationMins":10080,"resetsAt":123},"secondary":null},"other":{"limitId":"other","primary":{"usedPercent":null}}}}""");
        var quota=CodexLink.ParseQuota(doc.RootElement);Check(quota.Count==1&&quota[0].Used==37&&quota[0].Minutes==10080,"quota duration from data; null is unknown");
        using var old=JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":101,"windowDurationMins":300},"secondary":{"usedPercent":10,"windowDurationMins":10080}}}""");
        quota=CodexLink.ParseQuota(old.RootElement);Check(quota.Count==2&&quota[0].Used==100&&100-quota[1].Used==90,"legacy quota and independent remaining values");
        var sprites=new Sprites();foreach(var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,"assets"),"*.anim"))sprites.Get(Path.GetFileNameWithoutExtension(file));
        Check(sprites.Bytes<=16L*1024*1024,"compressed cache bounded to 16 MiB");
        var busy=new Behavior(9);busy.SetTask("thinking",0);busy.Tick(0,100,0,20);busy.Tick(.2,100,0,20);
        var player=new SpritePlayer(sprites);player.Draw(busy,.2);player.Draw(busy,.5);
        Check(player.LastClip=="look"&&player.LastFrame==4,"renderer displays mouse gaze over working state");
        busy.Tick(.6,500,500,500);busy.Tick(.9,500,500,500);player.Draw(busy,.9);player.Draw(busy,1.2);
        Check(player.LastClip=="working","renderer returns from gaze to work without losing task state");
        b=new Behavior(8);b.SetTask("thinking",0);b.React("completed",1);b.Tick(3,500,500,500);
        Check(b.Action=="working"&&b.TaskState=="thinking","completion of one task restores remaining work");
        var reminders=new Reminders();Check(reminders.Tick(1799,0,false)==null,"water reminder not early");
        Check(reminders.Tick(1800,0,false)=="water","water reminder at thirty minutes");
        Check(reminders.Tick(1801,0,false)==null,"water reminder not repeated");
        Check(reminders.Tick(3000,0,true)==null,"reminder deferred during an action or notice");
        Check(reminders.Tick(3001,0,false)=="rest","rest reminder after fifty active minutes");
        Check(reminders.Tick(3500,400,false)==null,"away user not disturbed");
        Check(reminders.Tick(4000,0,false)==null,"return from break does not stack overdue reminders");
        reminders.Snooze(4200);Check(reminders.Tick(4201,0,false)==null,"snooze suppresses immediate repeat");
        reminders.Enabled=false;Check(reminders.Tick(9000,0,false)==null,"health reminders can be disabled");
        var snooze=new Reminders();snooze.Tick(1800,0,false);snooze.Snooze(1810);Check(snooze.Tick(2409,0,false)==null&&snooze.Tick(2410,0,false)=="water","snoozed water reminder repeats in ten minutes");
        snooze.AfterSleep(9000);Check(snooze.Tick(9001,0,false)==null,"sleep resume avoids stacked old reminders");
        player.Paint("idle",0);
        using var native=new SharpRaster(192,208);native.Update(player.Pixels);
        byte[] Read(System.Drawing.Bitmap im)
        {
            var bits=im.LockBits(new System.Drawing.Rectangle(0,0,im.Width,im.Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            byte[] data=new byte[im.Width*im.Height*4];try{System.Runtime.InteropServices.Marshal.Copy(bits.Scan0,data,0,data.Length);}finally{im.UnlockBits(bits);}return data;
        }
        var actual=Read(native.Image);bool exact=true;
        for(int i=0;i<actual.Length;i+=4){int a=player.Pixels[i+3];exact&=actual[i+3]==a;for(int c=0;c<3;c++)exact&=actual[i+c]==(player.Pixels[i+c]*a+127)/255;}
        Check(exact,"native size preserves exact source pixels with alpha premultiplication");
        using var enlarged=new SharpRaster(288,312);enlarged.Update(player.Pixels);actual=Read(enlarged.Image);bool valid=true;
        for(int i=0;i<actual.Length;i+=4)for(int c=0;c<3;c++)valid&=actual[i+c]<=actual[i+3];
        Check(valid,"enlarged edges obey premultiplied alpha without invisible color fringe");
        foreach(string action in new[]{"leaf","water-observe","drink","stretch","leaf-breeze","dew-hands","waiting-breeze","offer-candy"})
        {
            double duration=Behavior.GestureDuration(action);var clip=sprites.Get(action);
            Check(Math.Abs(duration-clip.Frames/30.0)<.00001,action+" duration matches released frame count");
            var gesture=new Behavior(77);var renderer=new SpritePlayer(sprites);gesture.Request(action,0,duration);gesture.Greet(1);
            gesture.Tick(duration-.01,500,500,500);renderer.Draw(gesture,duration-.01);
            Check(gesture.Action==action&&renderer.LastClip==action&&renderer.LastFrame==clip.Frames-1,action+" reaches final frame before queued greeting");
            gesture.Tick(duration+.01,500,500,500);Check(gesture.Action=="greet",action+" releases queued greeting after complete return");
            gesture=new Behavior(78);gesture.SetTask("thinking",0);gesture.Request(action,1,duration);gesture.Tick(1+duration+.01,500,500,500);
            Check(gesture.Action=="working",action+" restores working state after completion");
        }
        var animation=new Behavior(78);
        animation.Tick(0,100,0,0);animation.Tick(.2,100,0,0);animation.Greet(.3);
        Check(!animation.Looking&&animation.Action=="idle","gaze returns before greeting");animation.Tick(.6,500,500,500);Check(animation.Action=="greet","greeting starts after gaze return buffer");
        Check(Behavior.Gestures.Select(g=>g.Id).Distinct().Count()==Behavior.Gestures.Count,"gesture catalog has unique IDs");
        foreach(var gesture in Behavior.Gestures)
        {
            var manual=new Behavior(17);manual.SetTask("thinking",0);manual.Paused=true;
            Check(manual.PlayGesture(gesture.Id,1)&&!manual.Paused&&manual.Action==gesture.Id,gesture.Id+" selectable while paused or working");
            manual.Tick(1+gesture.Duration-.001,500,500,500);
            Check(manual.Action==gesture.Id,gesture.Id+" manual action completes before returning");
            manual.Tick(1+gesture.Duration+.001,500,500,500);Check(manual.Action=="working",gesture.Id+" manual action restores task state");
        }
        var selection=new Behavior(42);selection.PlayGesture("drink",0);selection.PlayGesture("leaf",1);selection.PlayGesture("stretch",2);
        Check(selection.Action=="drink"&&selection.PendingAction=="stretch","manual selection queues the latest choice without truncating the current action");
        selection.Tick(5.401,500,500,500);Check(selection.Action=="stretch","queued manual action starts after the previous action completes");
        selection.Tick(9.802,500,500,500);bool quiet=true;for(double time=9.9;time<19.8;time+=.1){selection.Tick(time,500,500,500);quiet&=selection.Action=="idle";}
        Check(quiet,"manual playback preserves at least ten quiet seconds after completion");
        for(double time=19.8;time<30;time+=.02){selection.Tick(time,500,500,500);if(selection.Action!="idle")break;}
        Check(selection.Action!="idle"&&selection.Action!="stretch","random idle action resumes within twenty seconds without repeating the manual choice");
        selection=new Behavior(5);Check(!selection.PlayGesture("missing",0)&&selection.Action=="idle","unregistered actions cannot be requested by the action bar");
        selection.Tick(0,100,0,10);selection.Tick(.2,100,0,10);selection.PlayGesture("leaf",.3);selection.Tick(.6,500,500,500);
        Check(selection.Action=="leaf"&&!selection.Looking,"manual selection returns smoothly from mouse gaze");
        var output=args.SkipWhile(a=>a!="--self-test").Skip(1).FirstOrDefault()??Path.Combine(CodexLink.Data,"self-test.json");
        File.WriteAllText(output,JsonSerializer.Serialize(new{ok=true,count=passed.Count,checks=passed,idleEvents=history.Count},new JsonSerializerOptions{WriteIndented=true}));
    }
    public static async Task Diagnose()
    {
        using var link=new CodexLink();var state=link.ReadStatus();await link.RefreshQuota();
        File.WriteAllText(Path.Combine(CodexLink.Data,"connection-diagnostic.json"),JsonSerializer.Serialize(new{state,quotas=link.Quotas,quotaNote=link.QuotaNote,quotaUpdated=link.QuotaUpdated,readOnly=true},new JsonSerializerOptions{WriteIndented=true}));
    }
    public static async Task MonitorProbe(string[] args)
    {
        using var link=new CodexLink();List<CodexSnapshot> values=[];
        for(int i=0;i<12;i++){values.Add(link.ReadStatus());await Task.Delay(500);}
        File.WriteAllText(args[Array.IndexOf(args,"--monitor-probe")+1],JsonSerializer.Serialize(values));
    }
}
