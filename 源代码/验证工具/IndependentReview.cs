using System;
using System.Collections.Generic;
using System.Linq;
namespace NetBunny {
internal static class IndependentReview {
 static int n;
 static void Assert(bool v,string message){if(!v)throw new Exception(message);n++;}
 static AdventureEnemy Enemy(string id,int hp,double x){return new AdventureEnemy{Id=id,Health=hp,MaxHealth=hp,X=x};}
 static void Main(){
  Console.WriteLine(AdventureBattle.SelfTest());
  var p=new AdventureProgress();var b=new AdventureBattle(p);
  var auto=Enemy("auto",10,0);var manual1=Enemy("m1",10,100);var manual2=Enemy("m2",10,200);
  b.Enemies.AddRange(new[]{auto,manual1,manual2});b.SetAutomatic(true);Assert(b.PlanNext(0,0)==auto,"auto nearest selected");
  b.Authorize(manual1);b.Authorize(manual2);Assert(b.Target==manual1,"manual takes priority");Assert(manual1.QueueNumber==1&&manual2.QueueNumber==2&&auto.QueueNumber==3,"manual FIFO with auto tail");
  b.SetAutomatic(false);Assert(!auto.Authorized&&b.QueueCount==2,"stop-auto retains two manual orders");Assert(b.Target==manual1,"FIFO unchanged");
  b.ClearFieldSnapshot();Assert(b.QueueCount==3&&!b.Automatic,"clear includes prior queue and unqueued enemy");Assert(b.Enemies.All(e=>e.Authorized&&e.Cleaning),"snapshot marks only existing members");
  var future=Enemy("future",20,1);b.Enemies.Add(future);b.PlanNext(0,0);Assert(!future.Authorized,"future excluded from clear snapshot");
  manual1.CleanupStarted=true;b.CancelQueue();Assert(manual1.Authorized&&manual1.Cleaning,"in-flight retains settlement authority");
  Assert(!manual2.Authorized&&!auto.Authorized&&!manual2.Cleaning&&!auto.Cleaning,"cancel resets remaining batches");
  b.Damage(100000);Assert(p.Dead&&p.Health==0,"dead");b.Complete(manual1,80L*1024*1024,8,"partial success after cancelled",1);
  Assert(p.FreedBytes==80L*1024*1024&&p.Experience==50&&p.Health==0&&p.Dead,"partial success accounted without revival");
  int xp=p.Experience,score=p.Score;b.Complete(manual1,80L*1024*1024,8,"duplicate",0);
  Assert(p.Experience==xp&&p.Score==score&&p.Inventory.Count==1,"duplicate completion no rewards");
  b.Revive();Assert(p.Health==p.MaxHealth&&!b.Automatic,"manual revive no auto mode");
  b.Authorize(future);future.Cleaning=true;future.CleanupStarted=true;b.Damage(100000);b.CancelQueue();b.Complete(future,80L*1024*1024,8,"partial success",0);
  Assert(p.Level==2&&p.Dead&&p.Health==0,"level during death does not heal");Assert(p.Inventory.Count==2,"valid level drop credited once");
  var cancelled=Enemy("cancelled",20,0);b.Revive();b.Enemies.Add(cancelled);b.Authorize(cancelled);cancelled.Cleaning=true;b.CancelQueue();
  xp=p.Experience;score=p.Score;b.Complete(cancelled,100000,1,"not started",0);Assert(p.Experience==xp&&p.Score==score&&!cancelled.Defeated,"unstarted revoked completion rejected");
  var occupied=Enemy("occupied",20,0);b.Enemies.Add(occupied);b.Authorize(occupied);occupied.Cleaning=true;occupied.CleanupStarted=true;
  b.Complete(occupied,0,0,"busy",0);Assert(occupied.Failed&&p.Experience==xp&&p.Score==score,"busy skipped no reward");
  b.SetAutomatic(true);b.PlanNext(0,0);Assert(b.Target==null||!b.Target.Failed,"failed monster not auto-retried");
  b.CancelQueue();var boss=Enemy("boss",900,0);boss.IsBoss=true;b.Enemies.Add(boss);b.Authorize(boss);boss.Cleaning=true;
  xp=p.Experience;long disk=p.FreedBytes;b.Complete(boss,9999999,10,"ignored",0);
  Assert(p.Experience==xp&&p.FreedBytes==disk,"boss ignores fake disk/RAM reward");Assert(p.Score==score+100,"boss score only");
  Console.WriteLine("Independent transition review: "+n+" assertions passed; synthetic batches only.");
 }
}}
