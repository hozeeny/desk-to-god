using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace NetBunny
{
    internal sealed class DesktopMaintenanceEntry
    {
        internal string Kind, Summary;
        internal long FreedBytes, WorkingSetReduction;
        internal int DeletedFiles, TrimmedProcesses;
    }
    internal sealed partial class AdventureController
    {
        private int GrantCultivationKill(AdventureEnemy enemy)
        {
            if(enemy.CultivationRewarded)return enemy.CultivationRewardQi;
            enemy.CultivationRewarded=true;
            int qi=encounters.RollKillQi(enemy.IsBoss);if(enemy.Elite)qi*=CultivationBalance.EliteQiFactor;
            enemy.CultivationRewardQi=qi;cultivation.AddQi(qi);
            if(cultivation.Mortal&&enemy.FellInCombat)cultivation.RecordMortalKill();
            RecordDesktopEvent("击败"+(IsolatedDesktop?DesktopEnemyName(enemy):AdventureMonster.Find(enemy.MonsterKind).Name)+" · 修为+"+qi+(cultivation.Mortal?" · 历练 "+cultivation.MortalKills+"/10":""));
            return qi;
        }
        readonly Queue<DesktopMaintenanceEntry> desktopMaintenance = new Queue<DesktopMaintenanceEntry>();
        string desktopMaintenanceHistoryError;
        internal string DesktopEcologyStatus
        {
            get
            {
                if(!DesktopObservesSystem)return "虚拟历练 · 系统维护关闭";
                return (DesktopMode==DesktopEnvironmentMode.Maintain?"电脑生态 · 维护已分别授权":"电脑生态 · 只读观察")
                    + "\n临时文件 " + (liveTemp.Known?liveTemp.EligibleFiles+"个 / "+AdventureBattle.FormatBytes(liveTemp.EligibleBytes):"正在普查")
                    + " · 内存 " + (memoryAvailable?memoryPercent.ToString("0.0",CultureInfo.InvariantCulture)+"%":"等待采样")
                    + (String.IsNullOrEmpty(desktopMaintenanceHistoryError)?"":"\n"+desktopMaintenanceHistoryError);
            }
        }
        private bool DesktopCanMaintain(AdventureEnemy enemy)
        {
            if(!IsolatedDesktop)return true; // Existing formal path retains its own consent gate.
            bool memory=enemy.IsBoss&&!enemy.IsDiskBoss;
            return desktopEnvironment.MayMaintain(memory?DesktopMaintenanceScope.MemoryReclaim:DesktopMaintenanceScope.FileCleanup,
                memory?consent.MemoryEnabled:consent.CleanupEnabled,enemy.Authorization==AdventureAuthorization.Automatic,consent.AutoMaintenanceEnabled);
        }
        private void LoadDesktopMaintenanceHistory()
        {
            if(!IsolatedDesktop)return;
            string path=Path.Combine(isolatedProfileRoot,"maintenance-results-v1.log");
            try
            {
                if(!File.Exists(path))return;
                foreach(string line in File.ReadAllLines(path,Encoding.UTF8).Reverse().Take(12).Reverse())
                {
                    string[] p=line.Split('|');long bytes,working;int files,trimmed;
                    if(p.Length!=7||!Int64.TryParse(p[2],out bytes)||!Int32.TryParse(p[3],out files)||!Int64.TryParse(p[4],out working)||!Int32.TryParse(p[5],out trimmed))continue;
                    try { desktopMaintenance.Enqueue(new DesktopMaintenanceEntry{Kind=p[1],FreedBytes=bytes,DeletedFiles=files,WorkingSetReduction=working,TrimmedProcesses=trimmed,Summary=Encoding.UTF8.GetString(Convert.FromBase64String(p[6]))}); }catch(FormatException){}
                }
            }
            catch {desktopMaintenanceHistoryError="维护历史暂不可读取；不影响当前修行";}
        }
        private void RecordDesktopMaintenance(DesktopMaintenanceEntry result)
        {
            if(!IsolatedDesktop)return;
            desktopMaintenance.Enqueue(result);while(desktopMaintenance.Count>12)desktopMaintenance.Dequeue();
            RecordDesktopEvent(result.Summary);
            try
            {
                string line=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)+"|"+result.Kind+"|"+result.FreedBytes+"|"+result.DeletedFiles+"|"+result.WorkingSetReduction+"|"+result.TrimmedProcesses+"|"+Convert.ToBase64String(Encoding.UTF8.GetBytes(result.Summary))+Environment.NewLine;
                File.AppendAllText(Path.Combine(isolatedProfileRoot,"maintenance-results-v1.log"),line,Encoding.UTF8);
                string historyPath=Path.Combine(isolatedProfileRoot,"maintenance-results-v1.log");
                if(new FileInfo(historyPath).Length>128*1024)
                    File.WriteAllLines(historyPath,File.ReadAllLines(historyPath,Encoding.UTF8).Reverse().Take(64).Reverse(),Encoding.UTF8);
                desktopMaintenanceHistoryError=null;
            }
            catch {desktopMaintenanceHistoryError="本次维护结果尚未写入历史，请保留当前手札记录";}
        }
        internal void DesktopRefreshSources(){if(DesktopObservesSystem&&!disposed)RefreshWorld(true);}
    }
}
