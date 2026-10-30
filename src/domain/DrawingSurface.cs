using System;
using System.Collections.Generic;
namespace ITPU.Domain
{
    // Paletted CPU reference. Integration uploads only when dirty, at a profiled rate.
    // 256x128 with 12 undo frames uses < 0.5 MiB pixel history. Not a Unity performance claim.
    public sealed class DrawingSurface
    {
        public int Width{get;}public int Height{get;}public int Revision{get;private set;}
        private readonly byte[] pixels;private readonly List<byte[]> history=new List<byte[]>();private readonly int maxUndo;
        private bool active;private byte[] before;
        public DrawingSurface(int width=256,int height=128,int maxUndo=12)
        {if(width<8||height<8||width>512||height>512||maxUndo<1||maxUndo>32)throw new ArgumentException("Invalid board limits");Width=width;Height=height;this.maxUndo=maxUndo;pixels=new byte[width*height];}
        public bool Begin(){if(active)return false;before=(byte[])pixels.Clone();active=true;return true;}
        private static double Clamp01(double v){Guard.Finite(v,"uv");return Guard.Clamp(v,0,1);}
        public bool Line(double x0,double y0,double x1,double y1,double radiusPixels,byte color)
        {
            if(!active)return false;if(color>5)throw new ArgumentException("Invalid palette index");
            Guard.Finite(radiusPixels,"radius");if(radiusPixels<.5||radiusPixels>32)throw new ArgumentException("Invalid radius");
            double ax=Clamp01(x0)*(Width-1),ay=Clamp01(y0)*(Height-1),bx=Clamp01(x1)*(Width-1),by=Clamp01(y1)*(Height-1);
            int loX=Math.Max(0,(int)Math.Floor(Math.Min(ax,bx)-radiusPixels)),hiX=Math.Min(Width-1,(int)Math.Ceiling(Math.Max(ax,bx)+radiusPixels));
            int loY=Math.Max(0,(int)Math.Floor(Math.Min(ay,by)-radiusPixels)),hiY=Math.Min(Height-1,(int)Math.Ceiling(Math.Max(ay,by)+radiusPixels));
            double dx=bx-ax,dy=by-ay,len=dx*dx+dy*dy;bool changed=false;
            for(int y=loY;y<=hiY;y++)for(int x=loX;x<=hiX;x++)
            {double t=len==0?0:Guard.Clamp(((x-ax)*dx+(y-ay)*dy)/len,0,1),ex=x-(ax+t*dx),ey=y-(ay+t*dy);int i=y*Width+x;if(ex*ex+ey*ey<=radiusPixels*radiusPixels&&pixels[i]!=color){pixels[i]=color;changed=true;}}
            if(changed)Revision++;return changed;
        }
        public bool End()
        {if(!active)return false;active=false;bool changed=false;for(int i=0;i<pixels.Length;i++)if(pixels[i]!=before[i]){changed=true;break;}
            if(changed){if(history.Count==maxUndo)history.RemoveAt(0);history.Add(before);}before=null;return changed;}
        public bool Cancel(){if(!active)return false;Array.Copy(before,pixels,pixels.Length);before=null;active=false;Revision++;return true;}
        public bool Undo(){if(active||history.Count==0)return false;int i=history.Count-1;Array.Copy(history[i],pixels,pixels.Length);history.RemoveAt(i);Revision++;return true;}
        public void CopyPixels(byte[] destination){if(destination==null||destination.Length!=pixels.Length)throw new ArgumentException("Wrong buffer size");Array.Copy(pixels,destination,pixels.Length);}
        public byte[] Snapshot(){return(byte[])pixels.Clone();}
        public int NonEmptyPixels(){int n=0;foreach(var c in pixels)if(c!=0)n++;return n;}
        public void Reset(){Array.Clear(pixels,0,pixels.Length);history.Clear();before=null;active=false;Revision++;}
    }
    public sealed class PaperBasketProbe
    {
        private readonly double dwell;private double time;private bool entered,scored;
        public PaperBasketProbe(double dwell=.15){this.dwell=Guard.NonNegative(dwell,"dwell");}
        public void Reset(){entered=scored=false;time=0;}
        public void EnterMouth(bool movingDown,bool trackedRelease){entered=movingDown&&trackedRelease;time=0;}
        public bool Tick(bool inside,double dt,bool paused)
        {Guard.NonNegative(dt,"dt");if(paused||scored||!entered)return false;if(!inside){entered=false;time=0;return false;}time+=Math.Min(dt,.25);if(time+1e-9<dwell)return false;scored=true;return true;}
    }
}
