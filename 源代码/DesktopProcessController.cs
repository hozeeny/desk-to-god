using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace NetBunny
{
    internal sealed partial class AdventureController
    {
        internal Func<ProcessIdentity,bool> DesktopProcessConfirmation;
        Task<ProcessCloseResult> desktopProcessTask;
        CancellationTokenSource desktopProcessCancellation;
        string desktopProcessName;
        internal bool DesktopProcessBusy {get{return desktopProcessTask!=null;}}

        internal bool DesktopAddProcessTarget(ProcessIdentity identity)
        {
            if(!DesktopObservesSystem||disposed||!DesktopProcessTargets.ValidIdentity(identity)||!EnemyFieldBudget.CanAddOrdinary(battle.Enemies))return false;
            string id="process-"+identity.Id+"-"+identity.StartedUtcTicks;
            if(battle.Enemies.Any(e=>e.Id==id))return false;
            var copy=new ProcessIdentity{Id=identity.Id,StartedUtcTicks=identity.StartedUtcTicks,ExecutablePath=identity.ExecutablePath,Name=identity.Name,WorkingSetBytes=identity.WorkingSetBytes};
            var enemy=new AdventureEnemy{Id=id,ProcessTarget=copy,MonsterKind="imp",NoFieldClear=true,MaxHealth=EnemyBalance.OrdinaryHealth(battle.Progress.Level,1,0)};
            enemy.Health=enemy.MaxHealth;Spawn(enemy);battle.Enemies.Add(enemy);
            RecordDesktopEvent("已标记程序「"+copy.Name+"」 · 战斗后仍须单独确认正常退出");RenderWorld(true);return true;
        }
        private void BeginDesktopProcessAction(AdventureEnemy enemy)
        {
            // Settle combat exactly once. A program exit is a separate result.
            bool manual=enemy.Authorization==AdventureAuthorization.Manual;
            ProcessIdentity target=enemy.ProcessTarget;
            SettleHeld(enemy);
            if(!DesktopObservesSystem||!manual||desktopProcessTask!=null||DesktopProcessConfirmation==null)
            {RecordDesktopEvent("程序目标已击败 · 未请求退出程序");return;}
            bool confirmed=false;
            try{confirmed=DesktopProcessConfirmation(target);}catch{RecordDesktopEvent("确认窗口未完成 · 未请求退出程序");return;}
            if(!desktopEnvironment.MayMaintain(DesktopMaintenanceScope.ProcessExit,confirmed,false,false))
            {RecordDesktopEvent("已取消本次程序退出 · 修行奖励保留");return;}
            desktopProcessName=target.Name;
            desktopProcessCancellation=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            CancellationToken token=desktopProcessCancellation.Token;
            desktopProcessTask=Task.Run(()=>DesktopProcessActions.RequestGracefulClose(target,token));
            RecordDesktopEvent("已确认「"+target.Name+"」 · 正在请求正常退出，等待实际结果");
        }
        private void DrainDesktopProcessAction()
        {
            if(desktopProcessTask==null||!desktopProcessTask.IsCompleted)return;
            ProcessCloseResult result=desktopProcessTask.IsCanceled?new ProcessCloseResult{Status=ProcessCloseStatus.Cancelled,Detail="任务已取消"}
                :desktopProcessTask.IsFaulted?new ProcessCloseResult{Status=ProcessCloseStatus.Error,Detail=desktopProcessTask.Exception.GetBaseException().Message}:desktopProcessTask.Result;
            desktopProcessTask=null;desktopProcessCancellation.Dispose();desktopProcessCancellation=null;
            RecordDesktopMaintenance(new DesktopMaintenanceEntry{Kind="Process",Summary="「"+desktopProcessName+"」 · "+result.Summary});
        }
        internal void DesktopCancelProcessAction(){if(desktopProcessCancellation!=null)desktopProcessCancellation.Cancel();}
        private void DisposeDesktopProcessAction()
        {
            if(desktopProcessCancellation==null)return;
            if(desktopProcessTask!=null&&!desktopProcessTask.IsCompleted)
                RecordDesktopMaintenance(new DesktopMaintenanceEntry{Kind="Process",Summary="「"+desktopProcessName+"」 · 游戏退出，已停止等待；程序最终状态未核实，不计为已退出"});
            var source=desktopProcessCancellation;source.Cancel();
            if(desktopProcessTask!=null&&!desktopProcessTask.IsCompleted)desktopProcessTask.ContinueWith(t=>source.Dispose(),TaskScheduler.Default);
            else source.Dispose();
            desktopProcessCancellation=null;
        }
    }
}
