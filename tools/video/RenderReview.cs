using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RussiaFlag
{
    // Uses the shipping scene renderer; never starts desktop hooks or loads user settings.
    internal static class ReviewVideo
    {
        const int W = 1920, H = 1080, Fps = 30;
        static string[][] captions;
        static Font captionFont = new Font("Segoe UI", 28, FontStyle.Regular, GraphicsUnit.Pixel);
        static Font titleFont = new Font("Segoe UI Semibold", 54, FontStyle.Bold, GraphicsUnit.Pixel);
        static Font smallFont = new Font("Segoe UI Semibold", 23, FontStyle.Regular, GraphicsUnit.Pixel);
        static StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        static double Smooth(double p) { p = Math.Max(0, Math.Min(1, p)); return p*p*(3-2*p); }
        static PointF Mid(RectangleF r) { return new PointF(r.X+r.Width/2, r.Y+r.Height/2); }
        static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath(); float d=radius*2;
            p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
        static void Card(Graphics g, RectangleF r, int alpha)
        { using(GraphicsPath p=Round(r,18)) using(Brush b=new SolidBrush(Color.FromArgb(alpha,12,18,25))) g.FillPath(b,p); }
        static void Caption(Graphics g, double t)
        {
            foreach(string[] row in captions)
                if(t>=Double.Parse(row[0]) && t<Double.Parse(row[1]))
                {
                    string text=row[2]; SizeF size=g.MeasureString(text,captionFont,1540);
                    RectangleF r=new RectangleF((W-size.Width-64)/2,H-64-size.Height-28,size.Width+64,size.Height+28);
                    Card(g,r,230); g.DrawString(text,captionFont,Brushes.White,new RectangleF(r.X+32,r.Y+14,r.Width-64,r.Height-28),center); break;
                }
        }
        static void Label(Graphics g,string text)
        { SizeF s=g.MeasureString(text,smallFont); RectangleF r=new RectangleF(48,40,s.Width+40,49); Card(g,r,205); g.DrawString(text,smallFont,Brushes.White,new RectangleF(r.X+20,r.Y,r.Width-40,r.Height),center); }
        static void Focus(Graphics g, Bitmap scene, PointF target, double amount)
        {
            double z=1+amount; float cw=(float)(W/z),ch=(float)(H/z);
            float x=Math.Max(0,Math.Min(W-cw,target.X-cw/2)),y=Math.Max(0,Math.Min(H-ch,target.Y-ch/2));
            g.DrawImage(scene,new Rectangle(0,0,W,H),new RectangleF(x,y,cw,ch),GraphicsUnit.Pixel);
        }
        static void Click(Graphics g, PointF point, double age)
        {
            if(age<0 || age>1) return;
            using(Pen pen=new Pen(Color.FromArgb((int)(230*(1-age)),255,255,255),4))
            { float r=(float)(15+age*40); g.DrawEllipse(pen,point.X-r,point.Y-r,r*2,r*2); }
        }
        static Bitmap Pocket(bool lake, string root)
        {
            PocketStore store=new PocketStore(lake?"lake":"office",root);
            if(store.Items.Count==0)
            {
                string source=Path.Combine(Path.GetDirectoryName(root),"demo-source"); Directory.CreateDirectory(source);
                string file=Path.Combine(source,lake?"Планы на отдых.txt":"Заметки.txt");
                File.WriteAllText(file,"Демонстрационный файл для видеообзора."); store.Import(new string[]{file});
            }
            using(ShortcutPocket p=new ShortcutPocket(lake,new Point(1000,800),root))
            {
                p.Opacity=0; p.Show(); Application.DoEvents(); p.PerformLayout();
                foreach(Control control in p.Controls)
                { ListView list=control as ListView; if(list!=null && list.Items.Count>0) {list.Items[0].Selected=true;list.Items[0].Focused=true;} }
                Application.DoEvents();
                Bitmap b=new Bitmap(p.Width,p.Height); p.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height)); p.Hide(); return b;
            }
        }
        static void Panel(Graphics g,Bitmap panel,double age)
        {
            float width=1000,height=panel.Height*width/panel.Width;
            RectangleF r=new RectangleF((W-width)/2,(H-height)/2-20,width,height);
            Card(g,new RectangleF(r.X-10,r.Y-10,r.Width+20,r.Height+20),230);
            g.DrawImage(panel,r);
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                captions=Array.ConvertAll(File.ReadAllLines(args[0]),delegate(string x){return x.Split('\t');});
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"demo-data"); Directory.CreateDirectory(root);
                using(SceneCanvas office=new SceneCanvas(W,H,"kremlin-office.png"))
                using(SceneCanvas flag=new SceneCanvas(W,H,"kremlin-office.png"))
                using(SceneCanvas lake=new SceneCanvas(W,H,"lake-rest.png"))
                using(Bitmap officePanel=Pocket(false,root))
                using(Bitmap lakePanel=Pocket(true,root))
                using(Stream output=Console.OpenStandardOutput())
                {
                    if(args.Length>2 && args[2]=="--panels")
                    { officePanel.Save(Path.Combine(args[1],"panel-office.png")); lakePanel.Save(Path.Combine(args[1],"panel-lake.png")); return 0; }
                    InteractionState os=new InteractionState(),fs=new InteractionState {Monitor=true,Lamp=true,Curtain=1},ls=new InteractionState();
                    office.Bind(os,"","clock"); flag.Bind(fs,"","flag"); lake.Bind(ls,"");
                    byte[] bytes=new byte[W*H*4];
                    for(int i=0;i<45*Fps;i++)
                    {
                        double t=(double)i/Fps;
                        os.Time=fs.Time=ls.Time=t; os.Curtain=(float)Smooth((t-9)/1.5); os.Lamp=t>=14; os.Monitor=t>=17.7; os.OfficePocket=t>=22.5 && t<26;
                        ls.LakePocket=t>=31.6 && t<35;
                        office.Tick(1.0/Fps); flag.Tick(1.0/Fps); lake.Tick(1.0/Fps);
                        using(Bitmap a=office.Snapshot()) using(Bitmap b=lake.Snapshot()) using(Bitmap f=flag.Snapshot())
                        using(Bitmap frame=new Bitmap(W,H,PixelFormat.Format32bppRgb))
                        {
                            using(Graphics g=Graphics.FromImage(frame))
                            {
                                g.SmoothingMode=SmoothingMode.AntiAlias; g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                                if(t<4 || t>=35)
                                {
                                    g.DrawImage(a,new Rectangle(0,0,W/2,H),new Rectangle(W/5,0,W/2,H),GraphicsUnit.Pixel);
                                    g.DrawImage(b,new Rectangle(W/2,0,W/2,H),new Rectangle(W/3,0,W/2,H),GraphicsUnit.Pixel);
                                    using(Pen line=new Pen(Color.FromArgb(180,255,255,255),3)) g.DrawLine(line,W/2,0,W/2,H);
                                    if(t<4 || t>=40)
                                    {
                                        Card(g,new RectangleF(310,375,1300,260),220);
                                        g.DrawString(t<4?"Кремль и отдых у озера":"Ваш рабочий стол — с характером",titleFont,Brushes.White,new RectangleF(340,405,1240,90),center);
                                        g.DrawString(t<4?"ИНТЕРАКТИВНЫЕ ОБОИ ДЛЯ WINDOWS":"github.com/MaksimDenisov31/kremlin-lake-wallpapers",smallFont,Brushes.White,new RectangleF(340,525,1240,65),center);
                                    }
                                    else { Label(g,"ОДИН ИЛИ ДВА МОНИТОРА"); }
                                }
                                else if(t<26)
                                {
                                    double z=t>=8 && t<22 ? .65*Smooth((t-8)/1.1)*Smooth((22-t)/.8):0;
                                    Focus(g,t>=20 && t<22?f:a,new PointF(1450,470),z);
                                    Label(g,t<8?"01 / КАБИНЕТ КРЕМЛЯ":t<13?"ШТОРА":t<17?"ЛАМПА":t<22?"ЭКРАН МОНИТОРА":"ТАЙНИК ДЛЯ ФАЙЛОВ");
                                    if(t>=22.8) Panel(g,officePanel,t-22.8);
                                    if(t>=22 && t<22.8) Click(g,Mid(office.PocketArea),t-22.5);
                                }
                                else
                                {
                                    g.DrawImage(b,0,0,W,H); Label(g,t<31?"02 / ОТДЫХ У ОЗЕРА":"РЫБОЛОВНЫЙ ЯЩИК");
                                    if(t>=32.2) Panel(g,lakePanel,t-32.2);
                                    Click(g,Mid(lake.PocketArea),t-31.6);
                                }
                                Caption(g,t);
                                if(t<.4 || t>44.5) using(Brush fade=new SolidBrush(Color.FromArgb((int)(255*(t<.4?1-t/.4:(t-44.5)/.5)),0,0,0))) g.FillRectangle(fade,0,0,W,H);
                            }
                            if(i==60 || i==315 || i==450 || i==570 || i==630 || i==720 || i==855 || i==1005 || i==1125 || i==1275)
                                frame.Save(Path.Combine(args[1],"frame-"+i+".jpg"),ImageFormat.Jpeg);
                            BitmapData data=frame.LockBits(new Rectangle(0,0,W,H),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
                            try { Marshal.Copy(data.Scan0,bytes,0,bytes.Length); output.Write(bytes,0,bytes.Length); }
                            finally {frame.UnlockBits(data);}
                        }
                        if(i%150==0) Console.Error.WriteLine("Rendered "+i+" / "+45*Fps);
                    }
                }
                return 0;
            }
            catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
