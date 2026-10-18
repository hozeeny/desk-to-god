using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace NetBunny {
#if STATE_TEST_NO_HOST
internal static class DesktopDemoSession { internal static int Run(string[] args) { throw new InvalidOperationException("State test runner cannot open the desktop host."); } }
#endif
internal static class DesktopDemoStateTests {
 [STAThread] static int Main(string[] args) {
  try {
   var ctor = typeof(AdventureController).GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 6 && c.GetParameters()[5].ParameterType == typeof(string));
   if (ctor == null) throw new Exception("FAIL: persistent isolated desktop constructor is missing; current controller cannot load/save a private profile without touching live archives");
   Console.WriteLine(DesktopDemoProfiles.Smoke(args[0], args[1]));
   return 0;
  } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
 }
}
}
