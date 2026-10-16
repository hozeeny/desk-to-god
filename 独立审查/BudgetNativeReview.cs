using System;
using System.Windows.Forms;
namespace NetBunny
{
    internal static class BudgetNativeReview
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Console.WriteLine(EnemyFieldBudget.SelfTest());
                Console.WriteLine(AdventureController.SelfTest());
                Console.WriteLine(AdventureController.NativeSmoke(@"D:\骑士助手-v5.2\源代码\Assets"));
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        }
    }
}
