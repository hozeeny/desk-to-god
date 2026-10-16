using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
class MiniSmoke
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [STAThread] static int Main(string[] args)
    {
        int assertions=0;
        Action<bool,string> check=delegate(bool ok,string message){assertions++;if(!ok)throw new Exception(message);};
        Application.EnableVisualStyles();
        Assembly app=Assembly.LoadFrom(args[0]);Type type=app.GetType("NetBunny.SpeedMiniWindow");
        Form mini=(Form)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{new Action(delegate{})},null);
        try
        {
            mini.Location=new Point(-20000,-20000);
            IntPtr foreground=GetForegroundWindow();
            mini.Show();mini.Location=new Point(-20000,-20000);Application.DoEvents();
            check(GetForegroundWindow()==foreground,"show stole foreground focus");
            check(mini.Owner==null&&!mini.ShowInTaskbar&&mini.TopMost,"overlay ownership/taskbar/topmost");
            MethodInfo update=type.GetMethod("UpdateRates",BindingFlags.Instance|BindingFlags.NonPublic);
            update.Invoke(mini,new object[]{2097152d,524288d,true,true});
            Application.DoEvents();
            string labels="";foreach(Control control in mini.Controls)labels+=control.Text+"|";
            check(labels.Contains("2.00 MiB/s")&&labels.Contains("512.00 KiB/s"),"binary rate labels");
            check(labels.Contains("测速中"),"testing marker");
            using(Bitmap image=new Bitmap(mini.Width,mini.Height)){mini.DrawToBitmap(image,new Rectangle(Point.Empty,mini.Size));image.Save(args[1]);}
            update.Invoke(mini,new object[]{0d,0d,false,false});
            labels="";foreach(Control control in mini.Controls)labels+=control.Text+"|";
            check(labels.Contains("速率未知")&&labels.Contains("监控不可用"),"unavailable differs from zero");
            check(GetForegroundWindow()==foreground,"rate update stole foreground focus");
            mini.Close();check(!mini.IsDisposed&&!mini.Visible,"user close hides reusable window");
            type.GetMethod("KeepInScreen",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(mini,null);
            check(Screen.FromRectangle(mini.Bounds).WorkingArea.Contains(mini.Bounds),"screen edge clamp");
            Console.WriteLine("SpeedMiniWindow: "+assertions+" native UI checks passed.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{mini.Dispose();}
    }
}
