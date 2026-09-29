using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Muelsyse;

// One resample from decoded pixels to physical display pixels. Interpolate premultiplied
// channels together so invisible RGB cannot bleed into the outline on dark desktops.
internal sealed class SharpRaster : IDisposable
{
    public Bitmap Image {get;}
    readonly byte[] output;readonly float[] horizontal;
    readonly (int Index,float Weight)[][] xs,ys;
    public SharpRaster(int width,int height)
    {
        Image=new(width,height,PixelFormat.Format32bppPArgb);output=new byte[width*height*4];horizontal=new float[width*208*4];
        xs=Weights(192,width);ys=Weights(208,height);
    }
    static (int,float)[][] Weights(int source,int target)
    {
        var result=new (int,float)[target][];
        for(int i=0;i<target;i++)
        {
            double pos=(i+.5)*source/target-.5;int start=(int)Math.Floor(pos)-1;
            result[i]=Enumerable.Range(start,4).Select(x=>(Math.Clamp(x,0,source-1),Cubic((float)Math.Abs(pos-x)))).ToArray();
        }
        return result;
    }
    static float Cubic(float x)=>x<=1?(1.5f*x-2.5f)*x*x+1:x<2?((-.5f*x+2.5f)*x-4)*x+2:0;
    public void Update(byte[] pixels)
    {
        int width=Image.Width,height=Image.Height;
        if(width==192&&height==208)
        {
            for(int i=0;i<pixels.Length;i+=4){int a=pixels[i+3];output[i+3]=(byte)a;for(int c=0;c<3;c++)output[i+c]=(byte)((pixels[i+c]*a+127)/255);}
        }
        else
        {
            for(int y=0;y<208;y++)for(int x=0;x<width;x++)
            {
                int dst=(y*width+x)*4;
                for(int c=0;c<4;c++)
                {
                    float sum=0;foreach(var (index,weight) in xs[x]){int src=(y*192+index)*4;sum+=weight*(c==3?pixels[src+3]:pixels[src+c]*pixels[src+3]/255f);}
                    horizontal[dst+c]=sum;
                }
            }
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int dst=(y*width+x)*4;float alpha=0;
                foreach(var (index,weight) in ys[y])alpha+=weight*horizontal[(index*width+x)*4+3];
                int a=Math.Clamp((int)MathF.Round(alpha),0,255);output[dst+3]=(byte)a;
                for(int c=0;c<3;c++){float sum=0;foreach(var(index,weight)in ys[y])sum+=weight*horizontal[(index*width+x)*4+c];output[dst+c]=(byte)Math.Clamp((int)MathF.Round(sum),0,a);}
            }
        }
        var bits=Image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
        try{Marshal.Copy(output,0,bits.Scan0,output.Length);}finally{Image.UnlockBits(bits);}
    }
    public void Dispose()=>Image.Dispose();
}
