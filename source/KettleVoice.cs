using System.Runtime.InteropServices;

namespace Muelsyse;

internal record KettleLine(string FileName,string Text);

internal sealed class KettleLines(int? seed=null)
{
    // Existing game Mandarin recordings: CN_017 / 025 / 021 / 028.
    internal static readonly KettleLine[] Enter=[new("kettle-enter-01.wav","阳光有点辣，我先休息一会。"),new("kettle-enter-02.wav","如水随形。")];
    internal static readonly KettleLine[] Exit=[new("kettle-exit-01.wav","哈喽。"),new("kettle-exit-02.wav","愿望啊，请你凝结。")];
    readonly Random random=seed is{} value?new(value):new();
    int lastEnter=-1,lastExit=-1;double lastStarted=double.NegativeInfinity;
    internal KettleLine? Next(bool entering,double now)
    {
        // Rapid reversals stop the old recording without chattering new lines.
        if(now-lastStarted<1.2)return null;
        var lines=entering?Enter:Exit;int last=entering?lastEnter:lastExit;
        int i=random.Next(lines.Length-(last<0?0:1));if(last>=0&&i>=last)i++;
        if(entering)lastEnter=i;else lastExit=i;lastStarted=now;return lines[i];
    }
}

internal static class KettleAudio
{
    // File-backed asynchronous playback; a missing file never plays a system beep.
    internal static bool Play(string fileName)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"assets","audio",fileName);
        return File.Exists(path)&&PlaySound(path,IntPtr.Zero,0x00020003);
    }
    internal static void Stop()=>PlaySound(null,IntPtr.Zero,0);
    [DllImport("winmm.dll",EntryPoint="PlaySoundW",CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool PlaySound(string? sound,IntPtr module,uint flags);
}

internal sealed partial class PetForm
{
    readonly KettleLines kettleLines=new();bool kettleVoiceEnabled=true;
    void BeginKettleTransition(bool hide,double now)
    {
        bool changed=hide!=kettle.TargetHidden;kettle.Request(hide,now);
        if(!changed||!kettle.Moving)return;
        if(!smoke)KettleAudio.Stop();
        var line=kettleLines.Next(hide,now);if(line==null)return;
        speech=line.Text;speechUntil=now+KettleMotion.Duration;dirty=true;
        if(kettleVoiceEnabled&&!smoke)KettleAudio.Play(line.FileName);
    }
    void ToggleKettleVoice()
    {
        kettleVoiceEnabled=!kettleVoiceEnabled;
        if(!kettleVoiceEnabled&&!smoke)KettleAudio.Stop();
        SaveSettings();
    }
}
