using System.IO;
using System.IO.Compression;

namespace Muelsyse;

internal record Clip(byte[][] Compressed)
{
    public const int FrameBytes=192*208*4;
    public int Frames=>Compressed.Length;
    public long Bytes=>Compressed.Sum(p=>(long)p.Length);
}
internal sealed class Sprites
{
    private readonly Dictionary<string,(Clip Clip,long Stamp)> cache=[];
    private readonly object gate=new();
    private long stamp;
    private const long Budget=16L*1024*1024;
    public long Bytes { get {lock(gate)return cache.Values.Sum(x=>x.Clip.Bytes);} }
    public int Loads {get;private set;}
    public Clip Get(string name)
    {
        lock(gate)
        {
            if(cache.TryGetValue(name,out var hit)){cache[name]=(hit.Clip,++stamp);return hit.Clip;}
            using var stream=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"assets",name+".anim"));
            using var reader=new BinaryReader(stream);
            if(reader.ReadUInt32()!=0x3150554d)throw new InvalidDataException("动画格式不兼容");
            int width=reader.ReadInt32(),height=reader.ReadInt32(),count=reader.ReadInt32(),fps=reader.ReadInt32();
            if(width!=192||height!=208||count<1||count>180||fps!=30)throw new InvalidDataException("动画资源不兼容");
            byte[][] frames=new byte[count][];
            for(int i=0;i<count;i++)
            {
                int length=reader.ReadInt32();if(length<1||length>Clip.FrameBytes+2048)throw new InvalidDataException("动画帧长度不正确");
                frames[i]=GC.AllocateUninitializedArray<byte>(length);stream.ReadExactly(frames[i]);
            }
            var clip=new Clip(frames);
            while(cache.Count>0 && cache.Values.Sum(x=>x.Clip.Bytes)+clip.Bytes>Budget)
                cache.Remove(cache.MinBy(x=>x.Value.Stamp).Key);
            cache[name]=(clip,++stamp);Loads++;return clip;
        }
    }
    public Task Warm(params string[] names)=>Task.Run(()=>{foreach(var name in names)Get(name);});
}

internal sealed class SpritePlayer(Sprites sprites)
{
    public byte[] Pixels=>frameBuffer;
    private string drawn="";
    private int drawnIndex=-1,look=-1,target=-1;
    private string? transition;
    private bool reverse;
    private double transitionStart;
    private readonly byte[] frameBuffer=new byte[Clip.FrameBytes];
    public long Presented {get;private set;}
    public string LastClip=>drawn;
    public int LastFrame=>drawnIndex;
    public void ResetPose(){look=-1;target=-1;transition=null;}
    public void Draw(Behavior behavior,double now)
    {
        int wanted=behavior.Looking?behavior.Direction:-1;
        if(behavior.Action is not ("idle" or "working" or "waiting")) {look=-1;target=-1;transition=null;}
        if(wanted!=target){target=wanted;}
        if(transition!=null)
        {
            var clip=sprites.Get(transition);int index=(int)((now-transitionStart)*30);
            if(index<clip.Frames){Paint(transition,reverse?clip.Frames-1-index:index);return;}
            transition=null;
        }
        if(look!=target)
        {
            if(look==-1){look=target;transition=$"look-in-{target}";reverse=false;}
            else if(target==-1){transition=$"look-in-{look}";reverse=true;look=-1;}
            else
            {
                int delta=(target-look+16)%16;
                if(delta<=8){transition=$"look-turn-{look}";look=(look+1)%16;reverse=false;}
                else {look=(look+15)%16;transition=$"look-turn-{look}";reverse=true;}
            }
            transitionStart=now;Paint(transition,reverse?sprites.Get(transition).Frames-1:0);return;
        }
        if(look>=0){Paint("look",look);return;}
        string action=behavior.Action=="greet"?"wave":behavior.Action;
        var active=sprites.Get(action);
        Paint(action,(int)((now-behavior.ActionStarted)*30)%active.Frames);
    }
    public void Paint(string name,int index)
    {
        var clip=sprites.Get(name);index=Math.Clamp(index,0,clip.Frames-1);
        if(drawn==name&&drawnIndex==index)return;
        using(var memory=new MemoryStream(clip.Compressed[index],writable:false))
        using(var decoder=new ZLibStream(memory,CompressionMode.Decompress))decoder.ReadExactly(frameBuffer);
        drawn=name;drawnIndex=index;Presented++;
    }
}
