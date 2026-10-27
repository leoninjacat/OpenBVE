using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using OpenBveApi;
using OpenBveApi.Hosts;
using OpenBveApi.Trains;
using TrainManager;
using TrainManager.Car;
using TrainManager.Trains;

class Program {
 static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
 static void Set(object o,string field,object value) => o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);
 static void Main(string[] args) {
  var host=new Host(); typeof(TrainManagerBase).GetField("currentHost",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,host); typeof(TrainManagerBase).GetField("CurrentOptions",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,new Options());
  var train=new ScriptedTrain(TrainState.Available) { RepeatInterval=240, LeaveTime=180 };
  // Headless lifecycle fixture: no renderer, sounds or cars required.
  var ai=(TrackFollowingObjectAI)RuntimeHelpers.GetUninitializedObject(typeof(TrackFollowingObjectAI));
  Set(ai,"Train",train); Set(ai,"Data",new TravelData[]{new TravelStopData { Position=7165, RailIndex=2 },new TravelStopData {Position=8200,RailIndex=2}}); train.AI=ai;
  host.Now=180; train.Dispose();
  Check(train.State==TrainState.Pending && train.AppearanceTime==240,"reuse after first trip at 00:04");
  Check(double.IsNaN((double)typeof(TrackFollowingObjectAI).GetField("TimeLastProcessed",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ai)),"AI clock cleared between trips");
  ai.Trigger(0.1);
  Check(double.IsNaN((double)typeof(TrackFollowingObjectAI).GetField("TimeLastProcessed",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ai)),"pending train cannot restart its clock");
  Set(train,"LastAppearanceTime",240d); host.Now=420; train.State=TrainState.Available; train.Dispose();
  Check(train.AppearanceTime==480,"second reuse at 00:08");
  Set(train,"LastAppearanceTime",240d); host.Now=1000; train.Dispose();
  Check(train.AppearanceTime==1200,"time jump skips missed departures");
  train.RepeatInterval=0; train.Dispose();
  Check(train.State==TrainState.DisposePending,"legacy trains still dispose normally");
  Check(host.Stops==4,"sounds stopped on every disappearance");
  var car=new CarBase(train,0);
  car.Doors[0]=new Door(-1,1,0); car.Doors[1]=new Door(1,1,0);
  train.Cars=new[]{car}; car.Length=20;
  car.FrontAxle.Follower.TrackIndex=3; car.FrontAxle.Follower.TrackPosition=9000;
  car.Doors[0].State=1; car.Doors[0].AnticipatedOpen=true; car.CurrentSpeed=10;
  ai.ResetForRepeat();
  Check(car.FrontAxle.Follower.TrackIndex==2 && car.RearBogie.RearAxle.Follower.TrackIndex==2,"all axles return to initial rail");
  Check(car.FrontAxle.Follower.TrackPosition==7155+car.FrontAxle.Position,"composition returns to initial position");
  Check(car.CurrentSpeed==0 && car.Doors[0].State==0 && !car.Doors[0].AnticipatedOpen,"speed and doors reset before reuse");
  var oneWay=Service("generic", null, false, 20);
  oneWay.DisposeAtDestination=true;
  var oneWayAI=(TrackFollowingObjectAI)oneWay.AI;
  host.Now=0; oneWayAI.Trigger(0.1);
  host.Now=100; oneWayAI.Trigger(0.1);
  Check(oneWay.State==TrainState.Available,"one-way AI stays visible before reaching destination");
  host.Now=2000; oneWayAI.Trigger(0.1);
  Check(oneWay.State==TrainState.DisposePending,"one-way AI clears terminal after final dwell without a return rail");
  var outbound=Service("L1_GAT", "L1_CAC", false, 0);
  var singleManager=new Manager(host);var singleFleet=new ScriptedServiceFleet(120);
  for(int slot=0;slot<3;slot++) {
   var service=Service("one-way",null,false,0);service.WarmStart=true;service.State=TrainState.Pending;
   singleFleet.Add(service);singleManager.TFOs.Add(service);
  }
  host.Now=0;singleManager.InitializeScriptedTraffic();
  Check(singleFleet.Members.All(t=>t.WarmStartPending),"one-way warm start seeds traffic without paired return services");
  double seedAge=(double)typeof(TrackFollowingObjectAI).GetField("InitialElapsed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(singleFleet.Members[0].AI);
  Check(seedAge==120 && singleFleet.NextDepartureTime==120,"one-way initial traffic uses requested headway and starts ahead of the player");
  foreach(var service in singleFleet.Members){service.WarmStartPending=false;service.AppearanceTime=100;}
  singleManager.InitializeScriptedTraffic();
  Check(singleFleet.Members.All(t=>!t.WarmStartPending),"future one-way service does not seed traffic before its requested start time");
  var inbound=Service("L1_CAC", "L1_GAT", true, 180);
  int handoffs=0;
  outbound.JourneyCompleted=()=>{handoffs++;inbound.ActivateService();};
  inbound.JourneyCompleted=()=>{handoffs++;outbound.ActivateService();};
  host.Now=0; outbound.AI.Trigger(0.1);
  host.Now=1379; outbound.AI.Trigger(1);
  Check(outbound.State==TrainState.Available && inbound.AwaitingTrigger,"no handoff before 23 minutes");
  host.Now=1380; outbound.AI.Trigger(1);
  Check(outbound.State==TrainState.Pending && outbound.AwaitingTrigger && !inbound.AwaitingTrigger && inbound.AppearanceTime==1380,"GAT arrival activates CAC service at 00:23");
  inbound.State=TrainState.Available; inbound.AI.Trigger(0.1);
  host.Now=2760; inbound.AI.Trigger(1);
  Check(inbound.State==TrainState.Available && outbound.AwaitingTrigger,"CAC arrival remains visible during terminal wait");
  host.Now=2939; inbound.AI.Trigger(1);
  Check(handoffs==1,"three-minute terminal wait is not shortened");
  host.Now=2940; inbound.AI.Trigger(1);
  Check(handoffs==2 && !outbound.AwaitingTrigger && inbound.AwaitingTrigger && outbound.AppearanceTime==2940,"CAC handoff closes cycle at 00:49");
  outbound.State=TrainState.Available; outbound.AI.Trigger(0.1);
  host.Now=4320; outbound.AI.Trigger(1);
  Check(handoffs==3 && inbound.AppearanceTime==4320,"same pair reused for second cycle at 01:12");
  var pool=new ScriptedServiceFleet(240);
  var a=new ScriptedTrain(TrainState.Pending); var b=new ScriptedTrain(TrainState.Pending); var c3=new ScriptedTrain(TrainState.Pending);
  pool.Add(a);pool.Add(b);pool.Add(c3);
  Check(!b.TryReserveDeparture(0),"fleet serves oldest slot first regardless of update order");
  Check(a.TryReserveDeparture(0),"first composition departs immediately");a.State=TrainState.Available;
  Check(!b.TryReserveDeparture(239),"headway blocks early departure");
  Check(b.TryReserveDeparture(240),"second instance departs while first is still running");b.State=TrainState.Available;
  Check(a.State==TrainState.Available && b.State==TrainState.Available,"same service supports simultaneous compositions");
  c3.CanIntroduce=_=>false;
  Check(!c3.TryReserveDeparture(480) && pool.NextDepartureTime==480,"occupied birth defers departure without consuming slot");
  c3.CanIntroduce=_=>true;
  Check(c3.TryReserveDeparture(600) && pool.NextDepartureTime==840,"release uses actual departure for next headway");c3.State=TrainState.Available;
  a.State=TrainState.Pending;a.AppearanceTime=650;
  Check(a.TryReserveDeparture(5000) && pool.NextDepartureTime==5240,"clock jump permits one departure, not a burst");a.State=TrainState.Available;
  b.State=TrainState.Pending;b.AppearanceTime=700;
  Check(!b.TryReserveDeparture(5000),"missed fleet departures do not bunch together");
  var older=new ScriptedTrain(TrainState.Pending);var newer=new ScriptedTrain(TrainState.Pending);var fair=new ScriptedServiceFleet(240);fair.Add(newer);fair.Add(older);older.AppearanceTime=0;newer.AppearanceTime=50;
  Check(!newer.TryReserveDeparture(100) && older.TryReserveDeparture(100),"waiting fleet instance has priority over later return");
  var birth=new ScriptedTrain(TrainState.Pending);birth.Cars=new[]{Car(birth,1000,1)};
  var obstacle=new ScriptedTrain(TrainState.Available);obstacle.Cars=new[]{Car(obstacle,1000,1)};
  Check(!birth.IsBirthAreaFree(new AbstractTrain[]{obstacle}),"occupied birth rail blocks overlapping composition");
  obstacle.Cars[0].FrontAxle.Follower.TrackPosition=1055;
  Check(!birth.IsBirthAreaFree(new AbstractTrain[]{obstacle}),"birth clearance includes car body and safety margin");
  obstacle.Cars[0].FrontAxle.Follower.TrackPosition=1100;
  Check(birth.IsBirthAreaFree(new AbstractTrain[]{obstacle}),"birth releases after complete train clears margin");
  obstacle.Cars[0].FrontAxle.Follower.TrackPosition=1000;obstacle.Cars[0].FrontAxle.Follower.TrackIndex=0;obstacle.Cars[0].RearAxle.Follower.TrackIndex=0;
  Check(birth.IsBirthAreaFree(new AbstractTrain[]{obstacle}),"parallel rail remains available");
  obstacle.Cars[0].RearAxle.Follower.TrackIndex=1;
  Check(!birth.IsBirthAreaFree(new AbstractTrain[]{obstacle}),"rear axle still crossing birth rail prevents introduction");
  obstacle.State=TrainState.Pending;
  Check(birth.IsBirthAreaFree(new AbstractTrain[]{obstacle,birth}),"hidden instances and self do not block birth");
  var reuse=Service("", "", false, 0);reuse.State=TrainState.Available;var reusablePool=new ScriptedServiceFleet(240);reusablePool.Add(reuse);host.Now=600;reuse.Dispose();
  Check(reuse.State==TrainState.Pending && !reuse.AwaitingTrigger && reuse.AppearanceTime==600,"unchained fleet returns expired instance to pool");
  var out1=Service("GAT","CAC",false,0);var out2=Service("GAT","CAC",false,0);var ret1=Service("CAC","GAT",true,180);var ret2=Service("CAC","GAT",true,180);
  var outgoing=new ScriptedServiceFleet(240);outgoing.Add(out1);outgoing.Add(out2);var returning=new ScriptedServiceFleet(240);returning.Add(ret1);returning.Add(ret2);
  out1.JourneyCompleted=()=>ret1.ActivateService();out2.JourneyCompleted=()=>ret2.ActivateService();
  out1.CompleteJourney();Check(!ret1.AwaitingTrigger && ret2.AwaitingTrigger,"handoff activates corresponding fleet slot only");
  Check(out1.FleetSlot==ret1.FleetSlot && out2.FleetSlot==ret2.FleetSlot && out1.FleetSlot!=out2.FleetSlot,"paired services have stable independent fleet slots");
  var longOut=new ScriptedServiceFleet(240);var longReturn=new ScriptedServiceFleet(240);
  for(int slot=0;slot<13;slot++) {
   var x=Service("GAT","CAC",true,0);x.AwaitingTrigger=false;
   var y=Service("CAC","GAT",true,180);longOut.Add(x);longReturn.Add(y);
   x.JourneyCompleted=()=>y.ActivateService();y.JourneyCompleted=()=>x.ActivateService();
  }
  int departures=0,previous=-240,maxActive=0;bool maintained=true;
  for(int second=0;second<=7200;second++) {
   host.Now=second;
   foreach(var service in longOut.Members) {
    if(service.State==TrainState.Pending && service.TryReserveDeparture(second)) {
     maintained &= second-previous>=240;previous=second;departures++;service.State=TrainState.Available;
    }
    if(service.State==TrainState.Available) service.AI.Trigger(1);
   }
   foreach(var service in longReturn.Members) {
    if(service.State==TrainState.Pending && service.TryReserveDeparture(second)) service.State=TrainState.Available;
    if(service.State==TrainState.Available) service.AI.Trigger(1);
   }
   int active=0;foreach(var x in longOut.Members)if(x.State==TrainState.Available)active++;
   foreach(var x in longReturn.Members)if(x.State==TrainState.Available)active++;
   maxActive=Math.Max(active,maxActive);
  }
  Check(departures==31 && maintained,"two-hour fleet cycle sustains four-minute departures with no bursts");
  Check(maxActive==13,"thirteen paired fleet slots cap active compositions at thirteen");
  var moving=new ScriptedTrain(TrainState.Available);moving.Cars=new[]{Car(moving,1000,1)};
  var ahead=new ScriptedTrain(TrainState.Available);ahead.Cars=new[]{Car(ahead,1100,1)};
  var traffic=new ScriptedTrafficController(()=>new AbstractTrain[]{moving,ahead});
  Check(!traffic.CanAdvance(moving,5,10),"moving train stops before leader using braking clearance");
  ahead.Cars[0].FrontAxle.Follower.TrackPosition=5000;
  Check(traffic.CanAdvance(moving,5,10),"following train resumes after leader clears");
  ahead.Cars[0].FrontAxle.Follower.TrackPosition=1100;ahead.Cars[0].FrontAxle.Follower.TrackIndex=0;ahead.Cars[0].RearAxle.Follower.TrackIndex=0;
  Check(traffic.CanAdvance(moving,5,10),"parallel trains remain independent outside conflict zones");
  var crossing=new ScriptedTrain(TrainState.Available);crossing.Cars=new[]{Car(crossing,90,4)};
  var mainline=new ScriptedTrain(TrainState.Available);mainline.Cars=new[]{Car(mainline,150,0)};
  var interlocking=new ScriptedTrafficController(()=>new AbstractTrain[]{crossing,mainline});interlocking.AddZone(100,200,new[]{0,4});
  Check(!interlocking.CanAdvance(crossing,5),"different rail indices share an exclusive crossing zone");
  mainline.Cars[0].FrontAxle.Follower.TrackPosition=500;
  Check(interlocking.CanAdvance(crossing,5),"clear throat grants reservation");
  mainline.Cars[0].FrontAxle.Follower.TrackPosition=250;
  Check(!interlocking.CanAdvance(mainline,-5),"opposing train waits for entire owner to clear throat");
  crossing.Cars[0].FrontAxle.Follower.TrackPosition=350;
  Check(interlocking.CanAdvance(mainline,-5),"throat releases when owner clears completely");
  Check(interlocking.IsNearConflictZone(mainline,100),"warm start detects throat proximity");
  var terminalTrain=new ScriptedTrain(TrainState.Available);terminalTrain.Cars=new[]{Car(terminalTrain,15990,50)};
  var approach=new ScriptedTrain(TrainState.Available);approach.Cars=new[]{Car(approach,15310,0)};
  var terminalControl=new ScriptedTrafficController(()=>new AbstractTrain[]{terminalTrain,approach});terminalControl.AddZone(15350,15775,new[]{0},"CAC");terminalControl.AddZone(15350,16025,new[]{4,50},"CAC");
  Check(!terminalControl.CanAdvance(approach,5),"occupied terminal platform blocks arrival before the throat");
  terminalTrain.Cars[0].FrontAxle.Follower.TrackIndex=0;terminalTrain.Cars[0].RearAxle.Follower.TrackIndex=0;
  Check(terminalControl.CanAdvance(approach,5),"player parked on CAC rail zero does not reserve rail fifty platform");

  var hold=Service("GAT","CAC",false,180);var successor=Service("CAC","GAT",true,180);hold.ContinuousHandoff=true;
  var handoffPool=new ScriptedServiceFleet(240);handoffPool.Add(successor);handoffPool.SeedHeadway(0,0);
  hold.JourneyCompleted=()=>successor.ActivateService(hold);host.Now=100;hold.CompleteJourney();
  Check(hold.State==TrainState.Available && hold.AwaitingHandoff && successor.HandoffFrom==hold,"terminal train stays visible while successor queues");
  Check(!successor.TryReserveDeparture(200) && hold.State==TrainState.Available,"headway wait cannot make terminal train disappear");
  host.Now=240;Check(successor.TryReserveDeparture(240),"queued successor starts after terminal release");successor.Introduce();
  Check(hold.State==TrainState.Pending && hold.AwaitingTrigger && successor.State==TrainState.Available && !hold.AwaitingHandoff,"handoff exchanges visibility atomically");
  birth.HandoffFrom=obstacle;obstacle.State=TrainState.Available;obstacle.Cars[0].FrontAxle.Follower.TrackIndex=1;
  Check(birth.IsBirthAreaFree(new AbstractTrain[]{obstacle}),"continuous handoff ignores its own predecessor only");birth.HandoffFrom=null;
  var delayed=Service("GAT","CAC",false,0);delayed.ProtectedTraffic=true;
  var block=new ScriptedTrain(TrainState.Available);block.Cars=new[]{Car(block,250,0)};
  delayed.TrafficControl=new ScriptedTrafficController(()=>new AbstractTrain[]{delayed,block});host.Now=0;delayed.AI.Trigger(0);
  delayed.Cars=new[]{Car(delayed,200,0)};
  for(int second=1;second<=10;second++){host.Now=second;delayed.AI.Trigger(1);}
  double stoppedPosition=(double)typeof(TrackFollowingObjectAI).GetField("CurrentPosition",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(delayed.AI);
  Check(stoppedPosition==200 && delayed.CurrentSpeed==0,"blocked AI freezes itinerary instead of passing through player");
  delayed.Cars=Array.Empty<CarBase>();block.State=TrainState.Pending;host.Now=11;delayed.AI.Trigger(1);
  double releasedPosition=(double)typeof(TrackFollowingObjectAI).GetField("CurrentPosition",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(delayed.AI);
  Check(releasedPosition>200 && releasedPosition<205,"release resumes from held position without catching up by teleport");
  var manager=new Manager(host);var warmOut=new ScriptedServiceFleet(240);var warmBack=new ScriptedServiceFleet(240);
  for(int slot=0;slot<13;slot++) {
   var x=Service("WG","WC",true,180);x.WarmStart=true;var y=Service("WC","WG",true,180);
   warmOut.Add(x);warmBack.Add(y);manager.TFOs.Add(x);manager.TFOs.Add(y);
  }
  host.Now=0;manager.InitializeScriptedTraffic();int seeded=0,queued=0;
  foreach(var x in manager.TFOs) {var st=(ScriptedTrain)x;if(st.WarmStartPending && !st.AwaitingTrigger)seeded++;if(st.AwaitingTrigger)queued++;}
  Check(seeded==13 && queued==13,"initial circulation seeds thirteen trains and keeps their partners hidden");
  Check(warmOut.NextDepartureTime==240 && warmBack.NextDepartureTime==120,"warm start seeds both departure clocks from existing traffic");
  Check(warmOut.Members[1].WarmStartPending && warmOut.Members[1].TryReserveDeparture(0),"existing en-route traffic is introduced without generating platform departures");




  var evenManager=new Manager(host);var evenOut=new ScriptedServiceFleet(1200);var evenBack=new ScriptedServiceFleet(1200);
  for(int slot=0;slot<3;slot++){var x=Service("EG","EC",true,180);x.WarmStart=true;var y=Service("EC","EG",true,180);evenOut.Add(x);evenBack.Add(y);evenManager.TFOs.Add(x);evenManager.TFOs.Add(y);}
  host.Now=0;evenManager.InitializeScriptedTraffic();
  double middleAge=(double)typeof(TrackFollowingObjectAI).GetField("InitialElapsed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(evenOut.Members[1].AI);
  double lastAge=(double)typeof(TrackFollowingObjectAI).GetField("InitialElapsed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(evenBack.Members[2].AI);
  Check(middleAge==1040 && lastAge==520,"initial fleet phases divide the full cycle evenly instead of leaving a short wraparound gap");
  var priorityPool=new ScriptedServiceFleet(390);var seed=new ScriptedTrain(TrainState.Pending) {WarmStartPending=true};
  var turn=new ScriptedTrain(TrainState.Pending) {AppearanceTime=100,HandoffFrom=new ScriptedTrain(TrainState.Available)};
  priorityPool.Add(seed);priorityPool.Add(turn);
  Check(!seed.TryReserveDeparture(200) && turn.TryReserveDeparture(200),"visible terminal turnover takes priority over an older blocked initial seed");turn.State=TrainState.Available;
  Check(!seed.WarmStartPending && !seed.TryReserveDeparture(201),"delayed initial seed obeys the headway instead of jumping the departure queue");
  SmallvilleExitCase(host);
  StationPreferenceCase(host,1,0);
  StationPreferenceCase(host,-1,0);
  StationPreferenceCase(host,1,400);
  if(args.Length>0) { RunCirculation(host,args[0],false); RunCirculation(host,args[0],true); }
 }
 static void SmallvilleExitCase(Host host) {
  var train=new ScriptedTrain(TrainState.Available);train.Cars=new[]{Car(train,14190,0)};
  var path=new TravelData[]{new TravelStopData {Position=14190,Direction=(TravelDirection)1,RailIndex=0,Accelerate=1,TargetSpeed=20},
   new TravelPointData {Position=15575,RailIndex=50,Accelerate=1,Decelerate=-1,PassingSpeed=8,TargetSpeed=8},
   new TravelStopData {Position=15990,Direction=(TravelDirection)1,RailIndex=50,Decelerate=-1}};
  train.AI=new TrackFollowingObjectAI(train,path);((TrackFollowingObjectAI)train.AI).ResetForRepeat();
  var obstacle=new ScriptedTrain(TrainState.Available);obstacle.Cars=new[]{Car(obstacle,15990,50)};
  var control=new ScriptedTrafficController(()=>new AbstractTrain[]{train,obstacle});control.AddZone(15350,15775,new[]{0},"CAC");control.AddZone(15350,16025,new[]{1,4,50},"CAC");
  Check(!control.CanDepartStation(train,(TravelStopData)path[0],(TravelStopData)path[2],path),"occupied CAC platform retains departure in Smallville before the throat");
  obstacle.Cars[0].FrontAxle.Follower.TrackIndex=0;obstacle.Cars[0].RearAxle.Follower.TrackIndex=0;
  Check(control.CanDepartStation(train,(TravelStopData)path[0],(TravelStopData)path[2],path),"Smallville departure permits player resting on parallel CAC platform after switch");
  obstacle.Cars[0].FrontAxle.Follower.TrackPosition=15000;
  Check(!control.CanDepartStation(train,(TravelStopData)path[0],(TravelStopData)path[2],path),"occupied open line retains train at Smallville even outside terminal conflict zones");
  obstacle.Cars[0].FrontAxle.Follower.TrackIndex=1;obstacle.Cars[0].RearAxle.Follower.TrackIndex=1;
  Check(control.CanDepartStation(train,(TravelStopData)path[0],(TravelStopData)path[2],path),"opposite-track traffic outside throat does not block station departure");
  var competing=new ScriptedTrain(TrainState.Available);competing.Cars=new[]{Car(competing,15290,0)};
  Check(!control.CanAdvance(competing,5,10),"station departure reserves terminal route before reaching the throat");
 }
 static void StationPreferenceCase(Host host,int direction,double duration) {
  foreach(int rail in new[]{0,1})host.Tracks[rail]=new OpenBveApi.Routes.Track {Elements=new[]{new OpenBveApi.Routes.TrackElement(0) {IsDriveable=true}}};
  var t=new ScriptedTrain(TrainState.Available) {ProtectedTraffic=true,PreferStationWaiting=true,JourneyTime=duration};
  t.Cars=new[]{Car(t,0,1)};t.Cars[0].TractionModel=new TrainManager.Motor.BVETrailerCar(t.Cars[0]);
  double start=direction>0?200:1800,end=direction>0?1800:200;
  var data=new TravelData[]{new TravelStopData {Position=start,Direction=(TravelDirection)direction,RailIndex=1,Accelerate=1,TargetSpeed=10},
   new TravelStopData {Position=1000,Direction=(TravelDirection)direction,RailIndex=1,Accelerate=1,TargetSpeed=10,Decelerate=-1,StopTime=20,OpenLeftDoors=true},
   new TravelStopData {Position=end,Direction=(TravelDirection)direction,RailIndex=1,Decelerate=-1}};
  t.AI=new TrackFollowingObjectAI(t,data);((TrackFollowingObjectAI)t.AI).ResetForRepeat();
  var obstacle=new ScriptedTrain(TrainState.Available);obstacle.Cars=new[]{Car(obstacle,direction>0?1500:500,1)};
  t.TrafficControl=new ScriptedTrafficController(()=>new AbstractTrain[]{t,obstacle});
  for(int second=0;second<=600;second++){host.Now=second;t.AI.Trigger(1);}
  var ai=(TrackFollowingObjectAI)t.AI;
  Check(ai.WaitingStationPosition==1000,"blocked interstation retains train at previous station (direction="+direction+", duration="+duration+")");
  Check(Math.Abs(t.Cars[0].FrontAxle.Follower.TrackPosition-t.Cars[0].FrontAxle.Position+t.Cars[0].Length/2-1000)<0.001 && t.CurrentSpeed==0,"station wait never creeps into occupied section");
  Check(t.Cars[0].Doors[0].AnticipatedOpen,"station wait keeps boarding doors open");
  obstacle.State=TrainState.Pending;host.Now=601;t.AI.Trigger(1);
  double reference=t.Cars[0].FrontAxle.Follower.TrackPosition-t.Cars[0].FrontAxle.Position+t.Cars[0].Length/2;
  Check(double.IsNaN(ai.WaitingStationPosition) && direction*(reference-1000)>0 && Math.Abs(reference-1000)<5,"released station resumes without accumulated-time jump");
  for(int second=602;second<=610;second++){host.Now=second;t.AI.Trigger(1);}
  double before=t.Cars[0].FrontAxle.Follower.TrackPosition;
  obstacle.State=TrainState.Available;obstacle.Cars[0].FrontAxle.Follower.TrackPosition=before+direction*40;
  host.Now=611;t.AI.Trigger(1);
  Check(double.IsNaN(ai.WaitingStationPosition) && t.Cars[0].FrontAxle.Follower.TrackPosition==before,"new conflict after departure uses line protection without reversing to a station");
  double rewound=ai.PrepareAtPreviousStation(duration>0?350:170);
  double atStop=t.Cars[0].FrontAxle.Follower.TrackPosition-t.Cars[0].FrontAxle.Position+t.Cars[0].Length/2;
  Check(Math.Abs(atStop-1000)<0.001 && rewound<(duration>0?350:170),"initial conflict relocation selects an actual prior station");
 }
 static void RunCirculation(Host host,string folder,bool playerBlocksGat) {
  var manager=new Manager(host);host.Now=0;
  foreach(int rail in new[]{0,1,4,50})host.Tracks[rail]=new OpenBveApi.Routes.Track {Elements=new[]{new OpenBveApi.Routes.TrackElement(0) {IsDriveable=true}}};
  foreach(string name in new[]{"L1_GAT.XML","L1_CAC.XML"}) {
   var xml=System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(folder,name));var node=xml.Root.Element("TrackFollowingObject");var def=node.Element("Definition");
   var fleet=new ScriptedServiceFleet(double.Parse(def.Element("SpawnInterval").Value));int size=int.Parse(def.Element("FleetSize").Value);
   for(int slot=0;slot<size;slot++) {
    var t=new ScriptedTrain(TrainState.Pending) {ServiceId=def.Element("ServiceId").Value,NextService=def.Element("NextService").Value,
     ContinuousHandoff=true,ProtectedTraffic=true,PreferStationWaiting=def.Element("PreferStationWaiting")?.Value=="true",WarmStart=def.Element("WarmStart").Value=="true",AwaitingTrigger=def.Element("TriggeredStart")?.Value=="true",JourneyTime=1380,CompletionWait=180,TrafficControl=manager.ScriptedTraffic};
    t.Cars=new CarBase[8];for(int i=0;i<8;i++){var car=Car(t,0,0);car.Length=25;car.TractionModel=new TrainManager.Motor.BVETrailerCar(car);t.Cars[i]=car;}
    var data=new System.Collections.Generic.List<TravelData>();
    foreach(var n in node.Element("Stops").Elements()) {
     double Read(string k)=>double.Parse(n.Element(k)?.Value??"0",System.Globalization.CultureInfo.InvariantCulture);
     TravelData point;
     if(n.Name.LocalName=="Stop") {
      string door=n.Element("Doors")?.Value;
      string time=n.Element("StopTime")?.Value??"00.0000";var split=time.Split('.');string fraction=split.Length>1?split[1].PadRight(4,'0'):"0000";
      point=new TravelStopData {Position=Read("StopPosition"),Direction=(TravelDirection)(int)Read("Direction"),OpenLeftDoors=door=="L",OpenRightDoors=door=="R",StopTime=int.Parse(split[0])*3600+int.Parse(fraction.Substring(0,2))*60+int.Parse(fraction.Substring(2,2))};
     } else point=new TravelPointData {Position=Read("Position"),PassingSpeed=Read("PassingSpeed")/3.6};
     point.Accelerate=Read("Accelerate")/3.6;point.Decelerate=-Read("Decelerate")/3.6;point.TargetSpeed=Read("TargetSpeed")/3.6;point.RailIndex=(int)Read("Rail");data.Add(point);
    }
    t.AI=new TrackFollowingObjectAI(t,data.ToArray());((TrackFollowingObjectAI)t.AI).ResetForRepeat();fleet.Add(t);manager.TFOs.Add(t);
   }
  }
  manager.ScriptedTraffic.AddZone(0,350,new[]{0,1},"GAT");manager.ScriptedTraffic.AddZone(15350,15775,new[]{0},"CAC");manager.ScriptedTraffic.AddZone(15350,16025,new[]{1,4,50},"CAC");
  var player=new ScriptedTrain(TrainState.Available);player.Cars=new CarBase[8];for(int i=0;i<8;i++){player.Cars[i]=Car(player,0,0);player.Cars[i].Length=25;}player.PlaceCars(playerBlocksGat?200:15990);manager.Trains.Add(player);
  int handoffs=0;
  foreach(ScriptedTrain t in manager.TFOs) {
   t.CanIntroduce=x=>x.IsBirthAreaFree(manager.Trains.Cast<AbstractTrain>().Concat(manager.TFOs));
   t.JourneyCompleted=()=>{handoffs++;foreach(ScriptedTrain next in manager.TFOs)if(next.ServiceId==t.NextService && next.FleetSlot==t.FleetSlot)next.ActivateService(t);};
  }
  manager.InitializeScriptedTraffic();bool overlap=false,playerOverlap=false;int max=0,maxCAC=0,smallvilleWaits=0;
  for(int second=0;second<=7200;second++) {
   host.Now=second;
   if(playerBlocksGat && second==600)player.PlaceCars(15990);
   foreach(ScriptedTrain t in manager.TFOs) {
    if(t.State==TrainState.Pending && t.TryReserveDeparture(second)) {
     // Suppress only renderer work in this headless fixture; movement and controller are real.
     var cars=t.Cars;var old=t.HandoffFrom;var oldCars=old?.Cars;t.Cars=Array.Empty<CarBase>();if(old!=null)old.Cars=Array.Empty<CarBase>();
     t.Introduce();t.Cars=cars;if(old!=null){old.Cars=oldCars;((TrackFollowingObjectAI)old.AI).ResetForRepeat();}
    }
    if(t.State==TrainState.Available)t.AI.Trigger(1);
    if(t.ServiceId=="L1_CAC" && ((TrackFollowingObjectAI)t.AI).WaitingStationPosition==14190)smallvilleWaits++;
   }
   int active=0;for(int i=0;i<manager.TFOs.Count;i++) {
    var x=(ScriptedTrain)manager.TFOs[i];if(x.State!=TrainState.Available)continue;active++;
    for(int j=i+1;j<manager.TFOs.Count;j++) {
     var y=(ScriptedTrain)manager.TFOs[j];if(y.State!=TrainState.Available)continue;
     foreach(var a in x.Cars)foreach(var b in y.Cars) {
      if(a.FrontAxle.Follower.TrackIndex!=b.FrontAxle.Follower.TrackIndex)continue;
      double ac=a.FrontAxle.Follower.TrackPosition-a.FrontAxle.Position,bc=b.FrontAxle.Follower.TrackPosition-b.FrontAxle.Position;
      if(Math.Abs(ac-bc)<(a.Length+b.Length)/2-0.01)overlap=true;
     }
    }
   }

   int cac=0;
   foreach(ScriptedTrain t in manager.TFOs)if(t.State==TrainState.Available) {
    bool inCac=false;
    foreach(var a in t.Cars) {
     double center=a.FrontAxle.Follower.TrackPosition-a.FrontAxle.Position;
     int rail=a.FrontAxle.Follower.TrackIndex;
     if(center+a.Length/2>=15350 && center-a.Length/2<=(rail==0?15775:16025) && (rail==0||rail==1||rail==4||rail==50))inCac=true;
     foreach(var b in player.Cars) {
      if(rail!=b.FrontAxle.Follower.TrackIndex)continue;
      double bc=b.FrontAxle.Follower.TrackPosition-b.FrontAxle.Position;
      if(Math.Abs(center-bc)<(a.Length+b.Length)/2-0.01)playerOverlap=true;
     }
    }
    if(inCac)cac++;
   }
   maxCAC=Math.Max(maxCAC,cac);
   max=Math.Max(max,active);
  }
  Console.WriteLine("Circulation integration: handoffs="+handoffs+" maxActive="+max+" overlap="+overlap+" playerOverlap="+playerOverlap+" maxCAC="+maxCAC+" playerBlocksGat="+playerBlocksGat+" smallvilleWaitFrames="+smallvilleWaits);
  if(handoffs<20) {
   foreach(var z in (System.Collections.IEnumerable)typeof(ScriptedTrafficController).GetField("Zones",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager.ScriptedTraffic)) {
    object reservation=z.GetType().GetField("Lock",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(z);
    var owner=(ScriptedTrain)reservation.GetType().GetField("Owner",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reservation);
    Console.WriteLine("Zone="+z.GetType().GetField("Group",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(z)+" owner="+owner?.ServiceId+"#"+owner?.FleetSlot+" state="+owner?.State+" entered="+reservation.GetType().GetField("Entered",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reservation));
   }
  }
  if(handoffs<20)foreach(ScriptedTrain t in manager.TFOs)Console.WriteLine(t.ServiceId+"#"+t.FleetSlot+" "+t.State+" wait="+t.AwaitingTrigger+" transfer="+t.AwaitingHandoff+" pred="+(t.HandoffFrom?.ServiceId??"-")+" pos="+t.Cars[0].FrontAxle.Follower.TrackPosition+" rail="+t.Cars[0].FrontAxle.Follower.TrackIndex+" held="+typeof(TrackFollowingObjectAI).GetField("HeldTime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(t.AI));

  Check(!overlap,"actual Line 1 itineraries run two hours without same-rail overlap");
  Check(handoffs>=20,"terminal interlocking sustains circulation without permanent deadlock");
  Check(max<=manager.TFOs.Count/2,"actual circulation preserves fleet cap through visible handoffs");
  Check(!playerOverlap,"actual circulation avoids player parked in GAT then CAC");
  Check(maxCAC<=1,"CAC throat and platform remain mutually exclusive across all rail indices");
  // Light circulation may require no Smallville hold; explicit occupancy cases above verify it.
 }
 static CarBase Car(ScriptedTrain train,double position,int rail) {
  var c=new CarBase(train,0);c.Length=20;c.Doors[0]=new Door(-1,1,0);c.Doors[1]=new Door(1,1,0);
  c.FrontAxle.Follower.TrackPosition=position;c.FrontAxle.Follower.TrackIndex=rail;c.RearAxle.Follower.TrackIndex=rail;return c;
 }
 static ScriptedTrain Service(string id,string next,bool waiting,double dwell) {
  var t=new ScriptedTrain(waiting?TrainState.Pending:TrainState.Available) {ServiceId=id,NextService=next,AwaitingTrigger=waiting,JourneyTime=1380,CompletionWait=dwell};
  var c=new CarBase(t,0); c.Doors[0]=new Door(-1,1,0);c.Doors[1]=new Door(1,1,0);t.Cars=new[]{c};
  var data=new TravelData[]{new TravelStopData {Position=200,Direction=(TravelDirection)1,Accelerate=1,TargetSpeed=10},new TravelStopData {Position=15990,Direction=(TravelDirection)1,Decelerate=-1,StopTime=dwell}};
  t.AI=new TrackFollowingObjectAI(t,data);t.Cars=Array.Empty<CarBase>();return t;
 }
 sealed class Manager: TrainManagerBase { public Manager(Host host):base(host,null,new Options(),null) {} }
 sealed class Options: BaseOptions {public override void Save(string path) {}}
 sealed class Host: HostInterface {
  public override System.Collections.Generic.Dictionary<int,OpenBveApi.Routes.Track> Tracks {get;set;} = new System.Collections.Generic.Dictionary<int,OpenBveApi.Routes.Track>();
  public double Now; public int Stops; public Host():base(HostApplication.OpenBve) {}
  public override double InGameTime=>Now;
  public override void StopAllSounds(object parent) {Stops++;}
  public override AbstractTrain ParseTrackFollowingObject(string a,string b)=>throw new NotSupportedException();
 }
}
