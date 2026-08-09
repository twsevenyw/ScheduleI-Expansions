# API-VEHICLES.md — Schedule I vehicles, driving AI and vehicle storage

Source of truth: the IL2CPP dumps under `research/raw/` for the installed build
(`Assembly-CSharp.dll` from MelonLoader `Il2CppAssemblies`). Every type name, member name and
signature below is copied verbatim from those dumps.

**Reading conventions** are the same as `API-NPCS.md` §0: `Il2Cpp`-prefixed namespaces, IL2CPP
fields surfaced as C# properties, `_Foo_k__BackingField` == `Foo`, `Rpc*___` methods are FishNet
codegen (call the public wrapper), `field_Private_*` / `Method_*_PDM_*` names are index-based and
version-fragile, `(inferred …)` and **UNVERIFIED** mean what they say. Section 8 collects every
unverified claim.

**Relevance tags:** `[HD]` Hireable Drivers, `[PI]` Police Improvements, `[SC]` Special Customers.

---

## 0. Headline answer: native NPC driving AI exists and is complete

`Il2CppScheduleOne.Vehicles.AI.VehicleAgent` is a full autonomous car controller — A\* pathfinding
over a dedicated vehicle graph, PID steering and throttle, five obstacle sensors, sweep tests,
speed zones, traffic-light handling, stuck detection and auto-reverse, plus a teleport-to-graph
recovery. Three shipped consumers drive it:

| Consumer | Namespace | What it does |
|---|---|---|
| `VehiclePatrolBehaviour` | `NPCs.Behaviour` | Drives a fixed `VehiclePatrolRoute` waypoint loop `[PI]` |
| `VehiclePursuitBehaviour` | `NPCs.Behaviour` | Chases a `Player`, re-pathing as the target moves `[PI]` |
| `NPCSignal_DriveToCarPark` | `NPCs.Schedules` | **Get in a car → drive to a `ParkingLot` → park → get out**, as a scheduled action `[HD]` |

`[HD]` **Hireable Drivers should use the native stack.** The minimum viable driver is
`VehicleAgent.Navigate(destination, settings, callback)` plus `NPC.EnterVehicle` /
`LandVehicle.AddNPCOccupant`, and `NPCSignal_DriveToCarPark` is a working, shipped reference
implementation of the whole round trip including parking. No fallback is required. Details in §3.

---

## 1. Namespace map

| Namespace | Types | Role |
|---|---|---|
| `Il2CppScheduleOne.Vehicles` | 18 | `LandVehicle`, `VehicleManager`, `VehicleSeat`, `Wheel`, `VehicleLights`, `VehicleCamera`, `VehicleColor`, `ParkData`, `SpeedZone`, `VehicleObstacle`, `ObstructionDetector`, `VehicleRecoveryPoint`, `VehicleInitializer`, `PlayerPusher`, `VehicleFX`, `VehicleHumanoidCollider`, `VehicleAxle`, `Shitbox`, `LoanSharkCarVisuals`, `EParkingAlignment` |
| `Il2CppScheduleOne.Vehicles.AI` | 13 | `VehicleAgent`, `DriveFlags`, `NavigationSettings`, `NavigationUtility`, `PathGroup`, `PathPoint`, `PathUtility`, `PathCalculator`, `RoadPath`, `Sensor`, `SteerPID`, `PID_Parameters`, `FunnelZone`, `VehicleTeleporter` |
| `Il2CppScheduleOne.Vehicles.Modification` | 2 | `EVehicleColor`, `VehicleColors` |
| `Il2CppScheduleOne.Vehicles.Sound` | 1 | `VehicleSound` |
| `Il2CppPathfinding` | large | **A\* Pathfinding Project** — `Seeker`, `Path`, `NodeLink`, `NavmeshCut`. Used by vehicles only. |
| `UnityEngine.AI` | — | `NavMeshAgent`/`NavMeshObstacle`. Used by **NPCs on foot** (`NPCMovement.Agent`) and by `LandVehicle.NavMeshObstacle` so pedestrians path around parked cars. |
| `Il2CppScheduleOne.Map` | large | `ParkingLot`, `ParkingSpot`, `PoliceStation` (vehicle pool + dispatch) |
| `Il2CppScheduleOne.Storage` | 8 | `StorageEntity` — the trunk |

**Two pathfinding systems coexist and do not overlap.** Feet = Unity NavMesh. Cars = A\*
(`Il2CppPathfinding`), over two named graphs (`VehicleAgent.VehicleGraphName`,
`VehicleAgent.RoadGraphName`).

---

## 2. `Il2CppScheduleOne.Vehicles.LandVehicle`

`public class LandVehicle : Il2CppFishNet.Object.NetworkBehaviour`, implements at minimum
`Il2CppScheduleOne.Core.Weather.IWeatherEntity` and a saveable interface (`GetSaveString`,
`SaveFolderName`, `Loader` → `VehicleLoader`), plus `Hovered()`/`Interacted()` for
`Interaction.InteractableObject`.

One subclass ships: `Il2CppScheduleOne.Vehicles.Shitbox` (overrides `Awake` only).

### 2.1 Identity, ownership, save

```csharp
System.String vehicleName  { public get; public set; }   // authored
System.String vehicleCode  { public get; public set; }   // <-- the spawn key used by VehicleManager
System.Single vehiclePrice { public get; public set; }
System.String VehicleName  { public get; }
System.String VehicleCode  { public get; }
System.Single VehiclePrice { public get; }

Il2CppSystem.Guid GUID          { public get; public set; }
System.Boolean SpawnAsPlayerOwned { public get; public set; }
System.Boolean IsPlayerOwned    { public get; public set; }
System.Boolean IsVisible        { public get; public set; }
Il2CppScheduleOne.State.MonoState State { public get; }
Il2CppScheduleOne.Persistence.Loaders.VehicleLoader loader { public get; public set; }
System.String SaveFolderName { public get; }   System.String SaveFileName { public get; }

public virtual System.Void SetGUID(Il2CppSystem.Guid guid);
public          System.Void SetIsPlayerOwned(Il2CppFishNet.Connection.NetworkConnection conn, System.Boolean playerOwned);
public virtual System.Void SetOwner(Il2CppFishNet.Connection.NetworkConnection conn);
public virtual System.Void OnOwnerChanged();
public          System.Void SetVisible(System.Boolean vis);
public          System.Void GetNetworth(Il2CppScheduleOne.Money.MoneyManager+FloatContainer container);
public          System.Single GetVehicleValue();
public virtual Il2CppScheduleOne.Persistence.Datas.VehicleData GetVehicleData();
public virtual System.Void Load(Il2CppScheduleOne.Persistence.Datas.VehicleData data, System.String containerPath);
public virtual System.Void InitializeSaveable();
public virtual System.String GetSaveString();
```

### 2.2 Physics and handling parameters (all authored per-prefab, all writable at runtime)

```csharp
// Statics
static System.Single KINEMATIC_THRESHOLD_DISTANCE;   // beyond this from a player the car goes kinematic
static System.Single MAX_TURNOVER_SPEED;             // above this, no auto-righting
static System.Single TURNOVER_FORCE;                 // auto-righting torque
static System.Boolean USE_WHEEL;
static System.Single SPEED_DISPLAY_MULTIPLIER;
static System.Single MaxImpactDamage;
static System.Single MaxImpactDamageSpeed;
static System.Int32  previousSpeedsSampleSize;

// Handling
System.Single maxSteeringAngle;      // degrees
System.Single steerRate;             // deg/s toward target angle
System.Boolean flipSteer;
UnityEngine.AnimationCurve motorTorque;      // torque vs speed
System.Single TopSpeed;
System.Single diffGearing;
System.Single handBrakeForce;
UnityEngine.AnimationCurve brakeForce;
System.Single BrakeForceMultiplier;
System.Single downforce;
System.Single reverseMultiplier;

// Wheels & body
Il2CppReferenceArray<UnityEngine.WheelCollider> driveWheels, steerWheels, handbrakeWheels;
List<Il2CppScheduleOne.Vehicles.Wheel> wheels;
UnityEngine.Rigidbody Rb;
UnityEngine.Transform centerOfMass;
UnityEngine.BoxCollider boundingBox;
UnityEngine.GameObject vehicleModel;
System.Boolean UseHumanoidCollider;
Il2CppScheduleOne.Vehicles.VehicleHumanoidCollider HumanoidColliderContainer;
List<Il2CppScheduleOne.Vehicles.PlayerPusher> pushers;
UnityEngine.AI.NavMeshObstacle NavMeshObstacle;      // so pedestrians avoid it
Il2CppPathfinding.NavmeshCut  NavmeshCut;            // so other cars avoid it

// Live state
System.Single  Speed_Kmh          { public get; public set; }
System.Single  currentThrottle    { public get; public set; }
System.Single  CurrentSteerAngle  { public get; public set; }   // SyncVar
System.Boolean BrakesApplied      { public get; public set; }   // SyncVar
System.Boolean IsReversing        { public get; public set; }   // SyncVar
System.Boolean HandbrakeApplied   { public get; public set; }
System.Boolean IsPhysicallySimulated { public get; public set; }
UnityEngine.Vector3 BoundingBoxDimensions { public get; }
System.Single boundingBaseOffset  { public get; }
System.Single timeSinceSpawn      { public get; }
System.Single timeSinceLastOccupied { public get; }

// Manual control override — the "drive it yourself from a mod" hatch
System.Boolean overrideControls   { public get; public set; }
System.Single  throttleOverride   { public get; public set; }
System.Single  steerOverride      { public get; public set; }
System.Boolean handbrakeOverride  { public get; public set; }

public System.Void SetSteeringAngle(System.Single sa);
public System.Void SetIsBraking(System.Boolean braking);          public System.Void SetIsBreaking_Server(System.Boolean braking);   // (sic)
public System.Void SetIsReversing(System.Boolean reversing);      public System.Void SetIsReversing_Server(System.Boolean reversing);
public System.Void OverrideMaxSteerAngle(System.Single maxAngle); public System.Void ResetMaxSteerAngle();
System.Single ActualMaxSteeringAngle { public get; }
System.Boolean MaxSteerAngleOverridden { public get; public set; }
System.Single  OverriddenMaxSteerAngle { public get; public set; }

public virtual System.Void ApplyThrottle();   public virtual System.Void UpdateThrottle();
public virtual System.Void ApplySteerAngle(); public virtual System.Void UpdateSteerAngle();
public          System.Void ApplyDownForce();
public          System.Void UpdateTurnOver();
public          System.Void UpdateSpeedCalculation();
public          System.Void UpdatePhysicallySimulated(System.Boolean forceApply = false);
public          System.Boolean ShouldBePhysicallySimulated();
public          System.Void SetObstaclesActive(System.Boolean active);

public System.Void StartVehicle();   // engine on
public System.Void StopVehicle();    // engine off
Il2CppSystem.Action onVehicleStart, onVehicleStop, onHandbrakeApplied;
Il2CppSystem.Action<UnityEngine.Collision> onCollision;
```

**Kinematic mode matters for `[HD]`.** A vehicle far from any player (`KINEMATIC_THRESHOLD_DISTANCE`)
stops being physically simulated and is moved kinematically — `VehicleAgent.KinematicMode`,
`KinematicModeRotationSpeed`, `KinematicModeSpeedMultiplier`, `VehicleAgent.UpdateKinematic(deltaTime)`.
A hired driver sent across town will spend most of the trip in kinematic mode, which is *good*
(cheap, and it can't get stuck on physics) but means you should not read `Rb.velocity` for
progress — read `Speed_Kmh` / `VehicleAgent.TargetLocation`.

### 2.3 Placement, parking, recovery

```csharp
public enum EParkingAlignment { FrontToKerb = 0, RearToKerb = 1 }

public class ParkData { Il2CppSystem.Guid lotGUID; System.Int32 spotIndex; EParkingAlignment alignment; }

Il2CppScheduleOne.Vehicles.ParkData      CurrentParkData    { public get; public set; }
System.Boolean                           isParked           { public get; }
Il2CppScheduleOne.Map.ParkingLot         CurrentParkingLot  { public get; public set; }
Il2CppScheduleOne.Map.ParkingSpot        CurrentParkingSpot { public get; public set; }

public System.Void Park(Il2CppFishNet.Connection.NetworkConnection conn, ParkData parkData, System.Boolean network);
public System.Void Park_Networked(Il2CppFishNet.Connection.NetworkConnection conn, ParkData parkData);
public System.Void ExitPark(System.Boolean moveToExitPoint = true);
public System.Void ExitPark_Networked(Il2CppFishNet.Connection.NetworkConnection conn, System.Boolean moveToExitPoint = true);
public System.Void AlignTo(UnityEngine.Transform target, EParkingAlignment type, System.Boolean network = false);
public Il2CppSystem.Tuple<UnityEngine.Vector3, UnityEngine.Quaternion> GetAlignmentTransform(UnityEngine.Transform target, EParkingAlignment type);
public System.Void SetTransform(UnityEngine.Vector3 pos, UnityEngine.Quaternion rot);
public System.Void SetTransform_Server(UnityEngine.Vector3 pos, UnityEngine.Quaternion rot);
public System.Void TeleportToNavMesh(System.Boolean resetVelocity);
public virtual System.Void RecoverVehicle();
public virtual System.Boolean CanBeRecovered();
public System.Void UpdateOutOfBounds();
public System.Void DestroyVehicle();
```

### 2.4 Cosmetics

```csharp
Il2CppScheduleOne.Vehicles.VehicleColor Color;        // component: BodyMesh[] BodyMeshes, DefaultColor, ApplyColor(EVehicleColor)
Il2CppScheduleOne.Vehicles.Modification.EVehicleColor OwnedColor { public get; public set; }
Il2CppScheduleOne.Vehicles.VehicleLights lights;      // headlights/brake/reverse; HeadlightsOn is a SyncVar
Il2CppReferenceArray<Il2CppScheduleOne.Graffiti.SpraySurface> _spraySurfaces;
Il2CppScheduleOne.Map.POI POI;

public System.Void ApplyColor(EVehicleColor col);
public System.Void ApplyOwnedColor();
public virtual System.Void SetOwnedColor(Il2CppFishNet.Connection.NetworkConnection conn, EVehicleColor col);
public System.Void SendOwnedColor(EVehicleColor col);
public System.Void RefreshPoI();
public List<Il2CppScheduleOne.Persistence.Datas.SpraySurfaceData> GetSpraySurfaceData();

public enum EVehicleColor { Black=0, DarkGrey=1, LightGrey=2, White=3, Yellow=4, Orange=5, Red=6, DullRed=7,
                            Pink=8, Purple=9, Navy=10, DarkBlue=11, LightBlue=12, Cyan=13,
                            LightGreen=14, DarkGreen=15, Custom=16 }

public class VehicleColors : Singleton<VehicleColors>
{ List<VehicleColors+VehicleColorData> colorLibrary;
  public System.String  GetColorName(EVehicleColor c);
  public UnityEngine.Color32 GetColorUIColor(EVehicleColor c); }
```

---

## 3. Driving AI — `Il2CppScheduleOne.Vehicles.AI`

### 3.1 `VehicleAgent : UnityEngine.MonoBehaviour`

Reached from a vehicle as `landVehicle.Agent`, and from a driving behaviour as `behaviour.Agent`.

**The entire public control surface is three methods:**

```csharp
public System.Void Navigate(UnityEngine.Vector3 location,
                            Il2CppScheduleOne.Vehicles.AI.NavigationSettings settings = null,
                            Il2CppScheduleOne.Vehicles.AI.VehicleAgent+NavigationCallback callback = null);
public System.Void RecalculateNavigation();
public System.Void StopNavigating();
```

```csharp
public delegate System.Void VehicleAgent+NavigationCallback(VehicleAgent+ENavigationResult status);
public enum VehicleAgent+ENavigationResult { Failed = 0, Complete = 1, Stopped = 2 }
public enum VehicleAgent+EAgentStatus      { Inactive = 0, MovingToRoad = 1, OnRoad = 2 }
public enum VehicleAgent+EPathGroupStatus  { Inactive = 0, Calculating = 1 }
public enum VehicleAgent+ESweepType        { FL = 0, FR = 1, RL = 2, RR = 3 }
```

State you can read:

```csharp
System.Boolean AutoDriving      { public get; public set; }
System.Boolean KinematicMode    { public get; }
System.Boolean IsReversing      { public get; }
UnityEngine.Vector3 TargetLocation { public get; public set; }
System.Boolean NavigationCalculationInProgress { public get; }
System.Single  timeSinceLastNavigationCall     { public get; }
Il2CppScheduleOne.Vehicles.LandVehicle vehicle { public get; public set; }
Il2CppScheduleOne.Vehicles.AI.DriveFlags Flags { public get; public set; }
Il2CppScheduleOne.Math.PathSmoothingUtility+SmoothedPath path { public get; public set; }
Il2CppScheduleOne.Vehicles.SpeedZone currentSpeedZone { public get; public set; }
System.Single targetSpeed, targetSteerAngle_Normalized, lateralOffset;
```

Graphs, tuning and geometry (all static unless noted):

```csharp
static System.String VehicleGraphName;        // A* graph for off-road/lot driving
static System.String RoadGraphName;           // A* graph for roads
static UnityEngine.Vector3 MainGraphSamplePoint;
static System.Single MaxDistanceFromPath, MaxDistanceFromPathWhenReversing;
static System.Single MinRenavigationRate;
static System.Single Steer_P, Steer_I, Steer_D, Steer_Rate;
static System.Single Throttle_P, Throttle_I, Throttle_D;
static System.Single MaxAxlePositionShift, MAX_STEER_ANGLE_OVERRIDE;
static System.Single OBSTACLE_MIN_RANGE, OBSTACLE_MAX_RANGE;
static System.Single INFREQUENT_UPDATE_RATE;
static System.Single KinematicModeRotationSpeed, KinematicModeSpeedMultiplier;
static System.Single DestinationDistanceSlowThreshold, DestinationArrivalThreshold;
static System.Single UnmarkedSpeed, ReverseSpeed;
static System.Single sweepSegment;
// per-instance geometry, computed by InitializeVehicleData()
System.Single wheelbase, wheeltrack, vehicleLength, vehicleWidth, turnRadius, sweepTrack, wheelBottomOffset, maxSteerAngle;
// turn/speed shaping
System.Single turnSpeedReductionMinRange, turnSpeedReductionMaxRange, turnSpeedReductionDivisor,
              minTurnSpeedReductionAngleThreshold, minTurningSpeed, throttleMin, throttleMax,
              steerTargetFollowRate, sampleStepSizeMin, sampleStepSizeMax;
System.Int32  aheadPointSamples;
// stuck detection
System.Single StuckTimeThreshold, StuckDistanceThreshold;   System.Int32 StuckSamples;
Il2CppScheduleOne.DevUtilities.PositionHistoryTracker PositionHistoryTracker;
// pursuit shortcut
System.Boolean PursuitModeEnabled;  UnityEngine.Transform PursuitTarget;
System.Single PursuitDistanceUpdateThreshold;  UnityEngine.Vector3 PursuitTargetLastPosition;
// components
Il2CppPathfinding.Seeker roadSeeker, generalSeeker;
Il2CppScheduleOne.Vehicles.AI.Sensor sensor_FL, sensor_FM, sensor_FR, sensor_RR, sensor_RL;
Il2CppReferenceArray<Sensor> sensors;
Il2CppScheduleOne.Vehicles.AI.SteerPID steerPID;   Il2CppScheduleOne.DevUtilities.PID throttlePID;
Il2CppScheduleOne.Vehicles.AI.VehicleTeleporter Teleporter;
Il2CppScheduleOne.Vehicles.Wheel leftWheel, rightWheel;
UnityEngine.Transform CTE_Origin, FrontAxlePosition, RearAxlePosition;
UnityEngine.Transform sweepOrigin_FL, sweepOrigin_FR, sweepOrigin_RL, sweepOrigin_RR;
UnityEngine.LayerMask sweepMask, _groundMask;
```

Internals you may want to hook, but should not have to call:

```csharp
public System.Void InitializeVehicleData();
public System.Void EndDriving();
public System.Void UpdateSteering();      UpdateSpeed();      UpdateSpeedReduction();
public System.Void UpdateSweep();         UpdateOvertaking();  UpdatePursuitMode();
public System.Void UpdateStuckDetection(); public System.Boolean GetIsStuck();
public System.Void CheckDistanceFromPath();
public System.Void StartReverse();  public System.Void StopReversing();  public IEnumerator Reverse();
public System.Boolean IsOnVehicleGraph();  public System.Single GetDistanceFromVehicleGraph();
public UnityEngine.Collider GetClosestForwardObstruction(out System.Single& obstructionDist);
public UnityEngine.Vector3 GetPathLateralDirection();
public UnityEngine.Vector3 GetAxleGroundHit(System.Boolean front);
public System.Void UpdateKinematic(System.Single deltaTime);
public virtual System.Void RefreshSpeedZone();
public System.Boolean SweepTurn(ESweepType sweep, System.Single sweepAngle, System.Boolean reverse,
                                out System.Single& hitDistance, out UnityEngine.Vector3& hitPoint, System.Single steerAngle = 0f);
public System.Void BetterSweepTurn(ESweepType sweep, System.Single steerAngle, System.Boolean reverse,
                                   UnityEngine.LayerMask mask, out System.Single& hitDistance, out UnityEngine.RaycastHit& hit);
public System.Void NavigationCalculationCallback(NavigationUtility+ENavigationCalculationResult result,
                                                 Il2CppScheduleOne.Math.PathSmoothingUtility+SmoothedPath _path);
```

### 3.2 `DriveFlags` — per-trip driving style `[HD]` `[PI]`

`landVehicle.Agent.Flags` — mutate before calling `Navigate`.

```csharp
public class DriveFlags
{
    System.Boolean OverrideSpeed;
    System.Single  OverriddenSpeed;
    System.Single  OverriddenReverseSpeed;
    System.Single  SpeedLimitMultiplier;
    System.Boolean IgnoreTrafficLights;
    System.Boolean UseRoads;                 // false = allow the general (off-road) graph
    System.Boolean StuckDetection;
    DriveFlags+EObstacleMode ObstacleMode;
    System.Boolean AutoBrakeAtDestination;
    System.Boolean TurnBasedSpeedReduction;
    public System.Void ResetFlags();
}
public enum DriveFlags+EObstacleMode { Default = 0, IgnoreAll = 1, IgnoreOnlySquishy = 2 }
```

`[HD]` A civilian hired driver wants `UseRoads = true`, `IgnoreTrafficLights = false`,
`ObstacleMode = Default`, `AutoBrakeAtDestination = true`, `TurnBasedSpeedReduction = true`.
`[PI]` An aggressive police pursuit is exactly the opposite — that is what
`VehiclePursuitBehaviour.SetAggressiveDriving(bool)` and `aggressiveDrivingEnabled` toggle.

### 3.3 `NavigationSettings`

```csharp
public class NavigationSettings
{
    System.Boolean endAtRoad;                        // finish on the road graph rather than at the raw point
    System.Boolean ensureProximityToGraph;
    System.Boolean teleportToGraphIfCalculationFails; // the anti-stuck escape hatch
}
```

### 3.4 Path machinery (you rarely touch this directly)

```csharp
public static class NavigationUtility
{
    static System.Single ROAD_MULTIPLIER, OFFROAD_MULTIPLIER;
    public static UnityEngine.Coroutine CalculatePath(UnityEngine.Vector3 startPosition, UnityEngine.Vector3 destination,
                                                      NavigationSettings navSettings, DriveFlags flags,
                                                      Il2CppPathfinding.Seeker generalSeeker, Il2CppPathfinding.Seeker roadSeeker,
                                                      NavigationUtility+NavigationCalculationCallback callback);
    public static IEnumerator GenerateNavigationGroup(UnityEngine.Vector3 startPoint, UnityEngine.Vector3 entryPoint,
                                                      Il2CppPathfinding.NodeLink exitLink, UnityEngine.Vector3 exitPoint,
                                                      UnityEngine.Vector3 destination,
                                                      Il2CppPathfinding.Seeker generalSeeker, Il2CppPathfinding.Seeker roadSeeker,
                                                      NavigationUtility+PathGroupEvent callback);
    public static UnityEngine.Vector3 SampleVehicleGraph(UnityEngine.Vector3 destination);   // <-- snap any point to a drivable node
    public static Il2CppScheduleOne.Math.PathSmoothingUtility+SmoothedPath GetSmoothedPath(PathGroup group);
    public static System.Void AdjustEntryPoint(PathGroup group);   AdjustExitPoint(PathGroup group);
    public static System.Void DrawPath(PathGroup group, System.Single duration = 10f);
    public static UnityEngine.Vector3 GetClosestPointOnFiniteLine(UnityEngine.Vector3 point, UnityEngine.Vector3 line_start, UnityEngine.Vector3 line_end);
    public static System.Boolean DoesCloseDistanceExist(List<UnityEngine.Vector3> vectorList, UnityEngine.Vector3 point, System.Single thresholdDistance);
}
public enum NavigationUtility+ENavigationCalculationResult { Success = 0, Failed = 1 }
public delegate System.Void NavigationUtility+NavigationCalculationCallback(ENavigationCalculationResult result,
                                                                            PathSmoothingUtility+SmoothedPath path);
public delegate System.Void NavigationUtility+PathGroupEvent(PathGroup calculatedGroup);

public class PathGroup { UnityEngine.Vector3 entryPoint;
                         Il2CppPathfinding.Path startToEntryPath, entryToExitPath, exitToDestinationPath; }
```

A trip is therefore three A\* paths stitched together: **start → road entry → road exit →
destination**, with `FunnelZone` marking the entry/exit funnels:

```csharp
public class FunnelZone : UnityEngine.MonoBehaviour
{ static List<FunnelZone> funnelZones; UnityEngine.BoxCollider col; UnityEngine.Transform entryPoint;
  public static FunnelZone GetFunnelZone(UnityEngine.Vector3 point); }
```

Support types: `PathPoint : MonoBehaviour { List<PathPoint> connections; Boolean unique; }`,
`RoadPath { List<PathPoint> vectorPath; }`, `PathUtility` (cross-track error, ahead-point
sampling, angle-change-over-path), `SteerPID`/`PID_Parameters { float P, I, D; }`,
`PathCalculator` (static, empty surface in the dump).

```csharp
public class Sensor : UnityEngine.MonoBehaviour     // 5 per vehicle: FL, FM, FR, RL, RR
{ System.Boolean Enabled; UnityEngine.Collider obstruction; System.Single obstructionDistance;
  static System.Single checkRate; System.Single minDetectionRange, maxDetectionRange, checkRadius;
  UnityEngine.LayerMask checkMask; LandVehicle vehicle; System.Single calculatedDetectionRange;
  public System.Void Check(); }

public class VehicleTeleporter : UnityEngine.MonoBehaviour
{ public System.Void MoveToGraph(System.Boolean resetRotation = true);
  public System.Void MoveToRoadNetwork(System.Boolean resetRotation = true); }   // <-- unstick recovery
```

### 3.5 Speed zones and obstacles

```csharp
public class SpeedZone : UnityEngine.MonoBehaviour
{ static List<SpeedZone> speedZones; UnityEngine.BoxCollider col; System.Single speed;
  public static IEnumerable<SpeedZone> GetSpeedZones(UnityEngine.Vector3 point); }

public class VehicleObstacle : UnityEngine.MonoBehaviour
{ UnityEngine.Collider col; System.Boolean twoSided; VehicleObstacle+EObstacleType type; }
public enum VehicleObstacle+EObstacleType { Generic = 0, TrafficLight = 1 }

public class ObstructionDetector : UnityEngine.MonoBehaviour
{ LandVehicle vehicle; List<LandVehicle> vehicles; List<NPC> npcs;
  List<Il2CppScheduleOne.PlayerScripts.PlayerMovement> players; List<VehicleObstacle> vehicleObstacles;
  System.Single closestObstructionDistance, range; }

public class VehicleRecoveryPoint : UnityEngine.MonoBehaviour
{ static List<VehicleRecoveryPoint> recoveryPoints;
  public static VehicleRecoveryPoint GetClosestRecoveryPoint(UnityEngine.Vector3 pos); }
```

### 3.6 The two shipped driving behaviours

```csharp
public class VehiclePatrolBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour    // [PI]
{
    static System.Single MAX_CONSECUTIVE_PATHING_FAILURES, PROGRESSION_THRESHOLD;
    System.Int32 CurrentWaypoint;
    Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute Route;
    Il2CppScheduleOne.Vehicles.LandVehicle Vehicle;
    System.Boolean aggressiveDrivingEnabled;
    System.Int32   consecutivePathingFailures;
    System.Boolean isDriving { get; }
    Il2CppScheduleOne.Vehicles.AI.VehicleAgent Agent { get; }
    public System.Void SetRoute(VehiclePatrolRoute route);
    public System.Void StartPatrol();
    public System.Void DriveTo(UnityEngine.Vector3 location);
    public System.Void NavigationCallback(VehicleAgent+ENavigationResult status);
    public System.Boolean IsAsCloseAsPossible(UnityEngine.Vector3 pos, out UnityEngine.Vector3& closestPosition);
}

public class VehiclePursuitBehaviour : Il2CppScheduleOne.NPCs.Behaviour.Behaviour   // [PI]
{
    static System.Single RECENT_VISIBILITY_THRESHOLD, EXIT_VEHICLE_MAX_SPEED, CLOSE_ENOUGH_THRESHOLD,
                         UPDATE_FREQUENCY, STATIONARY_THRESHOLD, TIME_STATIONARY_TO_EXIT;
    UnityEngine.AnimationCurve RepathDistanceThresholdMap;   // re-path frequency vs distance
    LandVehicle vehicle;   Il2CppScheduleOne.PlayerScripts.Player Target;
    System.Boolean initialContactMade, aggressiveDrivingEnabled, beginAsSighted;
    System.Boolean IsTargetRecentlyVisible, IsTargetImmediatelyVisible, isDriving;
    UnityEngine.Vector3 currentDriveTarget;   System.Single timeStationary, timeSincePursuitStart;
    VehicleAgent Agent { get; }
    public virtual System.Void AssignTarget(Player target);
    public System.Void StartPursuit();      public System.Void BeginAsSighted();
    public System.Void DriveTo(UnityEngine.Vector3 location);
    public System.Void UpdateDestination();
    public UnityEngine.Vector3 GetPlayerChasePoint();
    public System.Void CheckExitVehicle();      // bails out on foot when stationary too long
    public System.Void SetAggressiveDriving(System.Boolean aggressive);
    public System.Void NavigationCallback(VehicleAgent+ENavigationResult status);
    public System.Void ProcessVisionEvent(Il2CppScheduleOne.Vision.VisionEventReceipt visionEventReceipt);
    public System.Void ProcessThirdPartyVisionEvent(Il2CppScheduleOne.Vision.VisionEventReceipt visionEventReceipt);
}

public class VehiclePatrolRoute : UnityEngine.MonoBehaviour
{ System.String RouteName; Il2CppReferenceArray<UnityEngine.Transform> Waypoints; System.Int32 StartWaypointIndex; }
```

### 3.7 `NPCSignal_DriveToCarPark` — the complete NPC round trip `[HD]`

`Il2CppScheduleOne.NPCs.Schedules.NPCSignal_DriveToCarPark : NPCSignal : NPCAction`

```csharp
Il2CppScheduleOne.Map.ParkingLot          ParkingLot;
Il2CppScheduleOne.Vehicles.LandVehicle    Vehicle;
System.Boolean                            OverrideParkingType;
Il2CppScheduleOne.Vehicles.EParkingAlignment ParkingType;
System.Boolean isAtDestination;   System.Single timeInVehicle, timeAtDestination;

public System.Void CheckValidForStart();
public UnityEngine.Vector3 GetWalkDestination();     // where to walk to reach the car
public System.Void DriveCallback(VehicleAgent+ENavigationResult result);
public EParkingAlignment GetParkingType();
public System.Void Park();
public virtual System.Void WalkCallback(NPCMovement+WalkResult result);
public virtual System.Void Started();  LateStarted();  Resume();  ResumeFailed();  JumpTo();  Skipped();  Interrupt();  End();
public virtual System.Void OnActiveTick();
```

**Read this as the reference implementation.** The flow it encodes:

1. `Started()` → `GetWalkDestination()` → `npc.Movement.SetDestination(...)`.
2. `WalkCallback(WalkResult.Success)` → `npc.EnterVehicle(conn, Vehicle)` (which fires
   `NPC.onEnterVehicle` and calls `LandVehicle.AddNPCOccupant(npc)`).
3. `Vehicle.Agent.Navigate(lotEntry, settings, DriveCallback)`.
4. `DriveCallback(ENavigationResult.Complete)` → `Park()` →
   `Vehicle.Park(conn, new ParkData { lotGUID, spotIndex, alignment }, network: true)`.
5. `npc.ExitVehicle()`; `End()` when `timeAtDestination` elapses.

`JumpTo()` handles the time-skip case by snapping the NPC and the car to the parked end state
instead of simulating the drive — **your driver mod must implement the same thing or the NPC will
be mid-road when the player sleeps.**

---

## 4. `Il2CppScheduleOne.Vehicles.VehicleManager` — registry and spawning

`public class VehicleManager : Il2CppScheduleOne.DevUtilities.NetworkSingleton<VehicleManager>`
→ `VehicleManager.Instance`, `VehicleManager.InstanceExists`.

```csharp
List<Il2CppScheduleOne.Vehicles.LandVehicle> AllVehicles         { public get; public set; }   // every vehicle in the world
List<Il2CppScheduleOne.Vehicles.LandVehicle> VehiclePrefabs      { public get; public set; }   // spawnable catalogue, keyed by vehicleCode
List<Il2CppScheduleOne.Vehicles.LandVehicle> PlayerOwnedVehicles { public get; public set; }
Il2CppScheduleOne.Persistence.Loaders.VehiclesLoader loader;
System.Int32 LoadOrder { public get; }

public Il2CppScheduleOne.Vehicles.LandVehicle GetVehiclePrefab(System.String vehicleCode);
public System.Void SpawnVehicle(System.String vehicleCode, UnityEngine.Vector3 position,
                                UnityEngine.Quaternion rotation, System.Boolean playerOwned);
public Il2CppScheduleOne.Vehicles.LandVehicle SpawnAndReturnVehicle(System.String vehicleCode, UnityEngine.Vector3 position,
                                                                    UnityEngine.Quaternion rotation, System.Boolean playerOwned);
public Il2CppScheduleOne.Vehicles.LandVehicle SpawnAndLoadVehicle(Il2CppScheduleOne.Persistence.Datas.VehicleData data,
                                                                  System.String path, System.Boolean playerOwned);
public System.Void LoadVehicle(Il2CppScheduleOne.Persistence.Datas.VehicleData data, System.String path);
public System.Void SpawnLoanSharkVehicle(UnityEngine.Vector3 position, UnityEngine.Quaternion rot);
public System.Void EnableLoanSharkVisuals(Il2CppFishNet.Object.NetworkObject veh);
public virtual System.String GetSaveString();
public virtual System.Void InitializeSaveable();
```

**Unlike NPCs, vehicles have a real spawn API.** `SpawnAndReturnVehicle(code, pos, rot, playerOwned)`
is the one-liner you want. Despawn is `LandVehicle.DestroyVehicle()`.

`VehicleManager.VehiclePrefabs` is the authoritative list of `vehicleCode` strings — **enumerate
it at runtime**; the codes are not literals in the metadata:

```csharp
foreach (var p in Il2CppScheduleOne.Vehicles.VehicleManager.Instance.VehiclePrefabs)
    MelonLogger.Msg($"{p.VehicleCode,-16} {p.VehicleName,-20} ${p.VehiclePrice} seats={p.Seats.Length}");
```

### 4.1 Spawn points and parking

```csharp
public class VehicleInitializer : Il2CppFishNet.Object.NetworkBehaviour
{ Il2CppScheduleOne.Map.ParkingLot InitialParkingLot; }     // scene-placed vehicles park themselves on server start

public class Il2CppScheduleOne.Map.ParkingLot : UnityEngine.MonoBehaviour
{
    System.String BakedGUID;   Il2CppSystem.Guid GUID;
    List<Il2CppScheduleOne.Map.ParkingSpot> ParkingSpots;
    UnityEngine.Transform EntryPoint;                  // <-- Navigate() target for a car heading here
    UnityEngine.Transform HiddenVehicleAccessPoint;
    System.Boolean UseExitPoint;   UnityEngine.Transform ExitPoint;
    Il2CppScheduleOne.Vehicles.EParkingAlignment ExitAlignment;
    Il2CppScheduleOne.DevUtilities.VehicleDetector ExitPointVehicleDetector;
    public List<ParkingSpot> GetFreeParkingSpots();
    public ParkingSpot       GetRandomFreeSpot();
    public System.Int32      GetRandomFreeSpotIndex();     // <-- ParkData.spotIndex
    public virtual System.Void SetGUID(Il2CppSystem.Guid guid);
}

public class Il2CppScheduleOne.Map.ParkingSpot : UnityEngine.MonoBehaviour
{
    Il2CppScheduleOne.Map.ParkingLot ParentLot;
    UnityEngine.Transform AlignmentPoint;
    Il2CppScheduleOne.Vehicles.EParkingAlignment Alignment;
    Il2CppScheduleOne.Vehicles.LandVehicle OccupantVehicle, OccupantVehicle_Readonly;
    public System.Void SetOccupant(Il2CppScheduleOne.Vehicles.LandVehicle vehicle);
    public System.Void Init();
}
```

`[PI]` Police vehicles live on `Il2CppScheduleOne.Map.PoliceStation : NPCEnterableBuilding`:

```csharp
static List<PoliceStation> PoliceStations;
UnityEngine.Transform SpawnPoint;
Il2CppReferenceArray<UnityEngine.Transform> VehicleSpawnPoints, PossessedVehicleSpawnPoints;
Il2CppScheduleOne.Map.ParkingLot PoliceVehicleParkingLot;
Il2CppReferenceArray<Il2CppScheduleOne.Vehicles.LandVehicle> PoliceVehicles;
List<Il2CppScheduleOne.Vehicles.LandVehicle> deployedVehicles;
List<Il2CppScheduleOne.Police.PoliceOfficer> OfficerPool;
System.Int32 AvailableVehicleCount { get; }   System.Int32 deployedVehicleCount { get; }
System.Single TimeSinceLastDispatch;
public Il2CppScheduleOne.Vehicles.LandVehicle DeployVehicle();
public System.Void Dispatch(System.Int32 requestedOfficerCount, Il2CppScheduleOne.PlayerScripts.Player targetPlayer,
                            PoliceStation+EDispatchType type = 0, System.Boolean beginAsSighted = false);
public static PoliceStation GetClosestPoliceStation(UnityEngine.Vector3 point);

public enum PoliceStation+EDispatchType { Auto = 0, UseVehicle = 1, OnFoot = 2 }
```

---

## 5. Seats, occupancy, and player/NPC interaction

### 5.1 Seats

```csharp
public class VehicleSeat : UnityEngine.MonoBehaviour
{
    System.Boolean isDriverSeat;
    Il2CppScheduleOne.PlayerScripts.Player Occupant { public get; public set; }
    System.Boolean isOccupied { public get; }
}
```

On `LandVehicle`:

```csharp
Il2CppReferenceArray<Il2CppScheduleOne.Vehicles.VehicleSeat> Seats;
List<UnityEngine.Transform> exitPoints;
UnityEngine.Transform cameraOrigin;
UnityEngine.Transform driverEntryPoint { public get; }
System.Int32   Capacity                { public get; }     // Seats.Length
System.Int32   CurrentPlayerOccupancy  { public get; }
System.Boolean IsOccupied              { public get; public set; }
System.Boolean LocalPlayerIsDriver     { public get; public set; }
System.Boolean LocalPlayerIsInVehicle  { public get; public set; }
Il2CppScheduleOne.PlayerScripts.Player       DriverPlayer   { public get; }
List<Il2CppScheduleOne.PlayerScripts.Player> OccupantPlayers{ public get; }
Il2CppReferenceArray<Il2CppScheduleOne.NPCs.NPC> OccupantNPCs { public get; public set; }   // <-- NPCs are tracked SEPARATELY

public Il2CppScheduleOne.Vehicles.VehicleSeat GetFirstFreeSeat();
public UnityEngine.Transform GetExitPoint(System.Int32 seatIndex = 0);
public UnityEngine.Transform GetClosestExitPoint(UnityEngine.Vector3 pos);
public UnityEngine.Transform GetValidExitPoint(List<UnityEngine.Transform> possibleExitPoints);
public System.Void SetSeatOccupant(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 seatIndex,
                                   Il2CppFishNet.Connection.NetworkConnection occupant);
public System.Void SetSeatOccupant_Server(System.Int32 seatIndex, Il2CppFishNet.Connection.NetworkConnection conn);
```

**Important asymmetry:** `VehicleSeat.Occupant` is typed `Player`, so **NPCs do not occupy
`VehicleSeat`s.** NPC occupancy is the separate `OccupantNPCs` array plus
`NPCMovement.SetSeat(Il2CppScheduleOne.AvatarFramework.Animation.AvatarSeat)` for the sitting
pose. `[HD]` A hired driver is an `OccupantNPC`, not a seat occupant — do not try to write it into
`Seats[i].Occupant`.

### 5.2 Enter / exit

```csharp
// NPC side (Il2CppScheduleOne.NPCs.NPC)
public virtual System.Void EnterVehicle(Il2CppFishNet.Connection.NetworkConnection connection,
                                        Il2CppScheduleOne.Vehicles.LandVehicle veh);
public virtual System.Void ExitVehicle();
Il2CppScheduleOne.Vehicles.LandVehicle CurrentVehicle { public get; public set; }
System.Boolean IsInVehicle { public get; }
Il2CppSystem.Action<Il2CppScheduleOne.Vehicles.LandVehicle> onEnterVehicle, onExitVehicle;

// Vehicle side
public System.Void AddNPCOccupant(Il2CppScheduleOne.NPCs.NPC npc);
public System.Void RemoveNPCOccupant(Il2CppScheduleOne.NPCs.NPC npc);

// Player side (Il2CppScheduleOne.PlayerScripts)
public System.Void Player.EnterVehicle(Il2CppScheduleOne.Vehicles.LandVehicle vehicle,
                                       Il2CppScheduleOne.Vehicles.VehicleSeat seat);
public System.Void Player.ExitVehicle(UnityEngine.Transform exitPoint);
public System.Void PlayerMovement.EnterVehicle(Il2CppScheduleOne.Vehicles.LandVehicle vehicle);
public System.Void PlayerMovement.ExitVehicle(Il2CppScheduleOne.Vehicles.LandVehicle veh);
public System.Void Player.CurrentVehicleChanged(Il2CppFishNet.Object.NetworkObject oldVeh,
                                                Il2CppFishNet.Object.NetworkObject newVeh, System.Boolean asServer);
//   Player.CurrentVehicle is a FishNet SyncVar (sync___get_value__CurrentVehicle_k__BackingField)

// Vehicle-side player hooks — the best Harmony targets [HD]
public System.Void LandVehicle.EnterVehicle();          // local-player interaction entry
public System.Void LandVehicle.ExitVehicle();
public System.Void LandVehicle.Exit(Il2CppScheduleOne.ExitAction action);   // ExitAction { ExitType Type; bool Used; Use(); }
public System.Void LandVehicle.OnLocalPlayerEnter();
public System.Void LandVehicle.OnLocalPlayerExit();
public System.Void LandVehicle.EndJustExited();
public System.Void LandVehicle.Hovered();
public System.Void LandVehicle.Interacted();            // <-- InteractableObject callback; patch to add "Ask driver to…" options
Il2CppScheduleOne.Interaction.InteractableObject intObj;
Il2CppScheduleOne.Vehicles.VehicleSeat localPlayerSeat;
System.Boolean justExitedVehicle;
public enum Il2CppScheduleOne.ExitType { Primary = 0, Secondary = 1 }
```

`[HD]` **Hook points for a driver mod:** `LandVehicle.Interacted()` (offer the "hire driver"
option on the car), `LandVehicle.OnLocalPlayerEnter/Exit` (player boards as a passenger),
`NPC.onEnterVehicle` / `onExitVehicle` (track the driver), and
`VehicleAgent.NavigationCallback` via the `Navigate` callback parameter (trip finished).

### 5.3 Camera and pushers

```csharp
public class VehicleCamera : UnityEngine.MonoBehaviour   // orbit cam while driving
{ static System.Single followDelta, xSpeed, ySpeed, yMinLimit, yMaxLimit, manualOverrideTime, manualOverrideReturnTime;
  LandVehicle vehicle; UnityEngine.Transform cameraOrigin, cameraDolly, targetTransform;
  System.Single lateralOffset, verticalOffset, orbitDistance; System.Boolean cameraReversed;
  public System.Void PlayerEnteredVehicle(LandVehicle veh);  public System.Void ForceCameraReturn();
  public UnityEngine.Vector3 GetTargetCameraPosition();      public UnityEngine.Vector3 LimitCameraPosition(UnityEngine.Vector3 targetPosition); }

public class PlayerPusher : UnityEngine.MonoBehaviour     // shoves pedestrians out of the way
{ LandVehicle veh; System.Single MinSpeedToPush, MaxPushSpeed, MinPushForce, MaxPushForce;
  UnityEngine.Collider collider; public System.Void SetEnabled(System.Boolean isEnabled); }
public System.Void LandVehicle.RegisterPusher(PlayerPusher pusher);
public System.Void LandVehicle.DeregisterPusher(PlayerPusher pusher);
```

---

## 6. Storage — the trunk

`LandVehicle` exposes storage through two members:

```csharp
Il2CppScheduleOne.Storage.StorageEntity        Storage { public get; public set; }   // the item container
Il2CppScheduleOne.Storage.StorageDoorAnimation Trunk   { public get; public set; }   // the lid animation

public List<Il2CppScheduleOne.ItemFramework.ItemInstance> GetContents();
public Il2CppScheduleOne.Persistence.Datas.ItemSet        GetContentsSet();
```

`Il2CppScheduleOne.Storage.StorageEntity : Il2CppFishNet.Object.NetworkBehaviour` — the **same**
interface used by every other container in the game (safes, shelves, briefcases):

```csharp
static System.Int32 MAX_SLOTS;
System.String  StorageEntityName, StorageEntitySubtitle;
System.Int32   SlotCount;                 // <-- trunk capacity, authored per vehicle prefab
System.Boolean EmptyOnSleep;
System.Boolean SlotsAreFilterable;
System.Int32   DisplayRowCount;
StorageEntity+EAccessSettings AccessSettings;   // Closed = 0, SinglePlayerOnly = 1, Full = 2
System.Single  MaxAccessDistance;
System.Boolean IsOpened { public get; }
Il2CppScheduleOne.PlayerScripts.Player CurrentPlayerAccessor { public get; public set; }
System.Int32 ItemCount { public get; }
List<Il2CppScheduleOne.ItemFramework.ItemSlot> ItemSlots { public get; public set; }
Il2CppSystem.Action onOpened, onClosed, onContentsChanged;

public virtual System.Boolean CanBeOpened();
public System.Void Open();
public virtual System.Void OnOpened();   public virtual System.Void OnClosed();
public System.Void SetAccessor(Il2CppFishNet.Object.NetworkObject accessor);
public System.Void SendAccessor(Il2CppFishNet.Object.NetworkObject accessor);

public System.Void InsertItem(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Boolean network = true);
public System.Boolean CanItemFit(Il2CppScheduleOne.ItemFramework.ItemInstance item, System.Int32 quantity = 1);
public System.Int32   HowManyCanFit(Il2CppScheduleOne.ItemFramework.ItemInstance item);
public List<Il2CppScheduleOne.ItemFramework.ItemInstance> GetAllItems();
public Dictionary<Il2CppScheduleOne.Storage.StorableItemInstance, System.Int32> GetContentsDictionary();
public System.Void ClearContents();
public System.Void LoadFromItemSet(Il2CppReferenceArray<Il2CppScheduleOne.ItemFramework.ItemInstance> items);
public virtual System.Void ContentsChanged();
public System.Void GetNetworth(Il2CppScheduleOne.Money.MoneyManager+FloatContainer container);

// Slot mutation (networked; each has an _Internal + Rpc trio)
public virtual System.Void SetStoredInstance(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex,
                                             Il2CppScheduleOne.ItemFramework.ItemInstance instance);
public virtual System.Void SetItemSlotQuantity(System.Int32 itemSlotIndex, System.Int32 quantity);
public virtual System.Void SetSlotFilter(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex,
                                         Il2CppScheduleOne.ItemFramework.SlotFilter filter);
public virtual System.Void SetSlotLocked(Il2CppFishNet.Connection.NetworkConnection conn, System.Int32 itemSlotIndex,
                                         System.Boolean locked, Il2CppFishNet.Object.NetworkObject lockOwner,
                                         System.String lockReason);
```

**Capacity is `Storage.SlotCount`, per vehicle prefab** — it is a plain writable int, so a mod can
enlarge a trunk with `veh.Storage.SlotCount = n` (clamped by `StorageEntity.MAX_SLOTS`; read that
static rather than guessing). `[PI]` `CheckpointBehaviour.DoesVehicleContainIllicitItems()` and
its `trunkOpened` flag are what police searches use against exactly this container.

---

## 7. Sound and effects (brief)

```csharp
public class Il2CppScheduleOne.Vehicles.Sound.VehicleSound : UnityEngine.MonoBehaviour
{
    static System.Single COLLISION_SOUND_COOLDOWN, AUDIO_LERP_SPEED,
                         MinCollisionMomentum, MaxCollisionMomentum,
                         MinCollisionVolume, MaxCollisionVolume,
                         MinCollisionPitch,  MaxCollisionPitch;
    System.Single EngineVolumeMultiplier, EnginePitchMultiplier;
    AudioSourceController EngineStartSource, EngineIdleSource, EngineLoopSource, HandbrakeSource, ImpactSound;
    UnityEngine.AnimationCurve EngineLoopPitchCurve, EngineLoopVolumeCurve;
    LandVehicle Vehicle;
    public System.Void EngineStart();  HandbrakeApplied();  OnCollision(UnityEngine.Collision collision);
    public System.Void UpdateEngineLoop(System.Boolean engineRunning, System.Single normalizedspeed);
    public System.Void UpdateIdle(System.Boolean engineRunning);
}

public class Il2CppScheduleOne.Vehicles.VehicleFX     : MonoBehaviour { ParticleSystem[] exhaustFX; OnVehicleStart(); OnVehicleStop(); }
public class Il2CppScheduleOne.Vehicles.VehicleLights : NetworkBehaviour { System.Boolean HeadlightsOn; /* SyncVar */ UpdateVisuals(); }
public class Il2CppScheduleOne.Vehicles.Wheel         : MonoBehaviour { /* friction curves, drift detection, weather override */ }
public class Il2CppScheduleOne.Vehicles.VehicleAxle   : MonoBehaviour { Wheel wheel; UnityEngine.Transform model; }
```

`Wheel` carries `_defaultData` (`Experimental.WheelData`) and `_rainOverrideData`
(`Experimental.WheelOverrideData`) plus `OnWeatherChange(WeatherConditions)`, so grip already
degrades in rain. Statics: `SIDEWAY_SLIP_THRESHOLD`, `FORWARD_SLIP_THRESHOLD`,
`DRIFT_AUDIO_THRESHOLD`, `MIN_SPEED_FOR_DRIFT`, `HandbrakeFowardStiffnessMultiplier_Front/Rear`,
`HandbrakeSidewayStiffnessMultiplier_Front/Rear`.

---

## 8. Recipes

### 8.1 Spawn a car and have an NPC drive it somewhere `[HD]`

```csharp
using Il2CppScheduleOne.Vehicles;
using Il2CppScheduleOne.Vehicles.AI;

// 1. Get / spawn the vehicle (server only).
var veh = VehicleManager.Instance.SpawnAndReturnVehicle("shitbox", spawnPos, spawnRot, playerOwned: false);

// 2. Walk the NPC to the driver door, then board.
npc.Movement.SetDestination(veh.driverEntryPoint.position,
    new System.Action<Il2CppScheduleOne.NPCs.NPCMovement.WalkResult>(r => {
        if (r != Il2CppScheduleOne.NPCs.NPCMovement.WalkResult.Success) return;

        npc.EnterVehicle(null, veh);          // null conn == broadcast; also calls veh.AddNPCOccupant(npc)
        veh.StartVehicle();

        // 3. Tune the driving style.
        veh.Agent.Flags.UseRoads               = true;
        veh.Agent.Flags.IgnoreTrafficLights    = false;
        veh.Agent.Flags.AutoBrakeAtDestination = true;
        veh.Agent.Flags.TurnBasedSpeedReduction= true;
        veh.Agent.Flags.StuckDetection         = true;
        veh.Agent.Flags.ObstacleMode           = DriveFlags.EObstacleMode.Default;

        // 4. Snap the destination onto the drivable graph, then go.
        var dest = NavigationUtility.SampleVehicleGraph(rawDestination);
        var nav  = new NavigationSettings { endAtRoad = true, ensureProximityToGraph = true,
                                            teleportToGraphIfCalculationFails = true };
        veh.Agent.Navigate(dest, nav, (Il2CppSystem.Action<VehicleAgent.ENavigationResult>)(status => {
            if (status == VehicleAgent.ENavigationResult.Complete) OnArrived(npc, veh);
            else if (status == VehicleAgent.ENavigationResult.Failed) OnFailed(npc, veh);
        }));
    }));
```

The callback parameter is typed `VehicleAgent+NavigationCallback`, a `MulticastDelegate`, and it
**does** carry an implicit conversion, so the cast above compiles:

```csharp
public static VehicleAgent+NavigationCallback op_Implicit(System.Action<VehicleAgent+ENavigationResult> = null)
public static VehicleAgent+NavigationCallback op_Addition(VehicleAgent+NavigationCallback = null, VehicleAgent+NavigationCallback = null)
public static VehicleAgent+NavigationCallback op_Subtraction(VehicleAgent+NavigationCallback = null, VehicleAgent+NavigationCallback = null)
```

Same pattern on `NavigationUtility+NavigationCalculationCallback` and `+PathGroupEvent`.

### 8.2 Park at the end of the trip

```csharp
var lot  = /* Il2CppScheduleOne.Map.ParkingLot */;
int spot = lot.GetRandomFreeSpotIndex();
if (spot >= 0)
{
    veh.Agent.Navigate(lot.EntryPoint.position, nav, (…status…) => {
        veh.Park(null, new ParkData { lotGUID = lot.GUID, spotIndex = spot,
                                      alignment = lot.ParkingSpots[spot].Alignment }, network: true);
        npc.ExitVehicle();
        veh.StopVehicle();
    });
}
```

### 8.3 Unstick a car

```csharp
if (veh.Agent.GetIsStuck())
{
    veh.Agent.StartReverse();                       // try reversing out first
    // if that fails:
    veh.Agent.Teleporter.MoveToRoadNetwork(resetRotation: true);
    veh.Agent.RecalculateNavigation();
    // last resort:
    var rp = VehicleRecoveryPoint.GetClosestRecoveryPoint(veh.transform.position);
    veh.RecoverVehicle();
}
```

### 8.4 Enumerate what exists

```csharp
foreach (var v in VehicleManager.Instance.AllVehicles)
    MelonLogger.Msg($"{v.VehicleCode} guid={v.GUID} owned={v.IsPlayerOwned} parked={v.isParked} " +
                    $"lot={(v.CurrentParkingLot ? v.CurrentParkingLot.name : "-")} " +
                    $"trunk={v.Storage?.SlotCount} seats={v.Capacity}");
foreach (var lot in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.Map.ParkingLot>())
    MelonLogger.Msg($"LOT {lot.name} guid={lot.GUID} spots={lot.ParkingSpots.Count} free={lot.GetFreeParkingSpots().Count}");
foreach (var r in UnityEngine.Object.FindObjectsOfType<Il2CppScheduleOne.NPCs.Behaviour.VehiclePatrolRoute>())
    MelonLogger.Msg($"ROUTE {r.RouteName} waypoints={r.Waypoints.Length}");
```

---

## 9. UNVERIFIED / not present in the dumps

1. **`vehicleCode` string values.** No vehicle-code literals appear in `literals-*.txt`. Only the
   class `Il2CppScheduleOne.Vehicles.Shitbox` hints at one. **Enumerate
   `VehicleManager.Instance.VehiclePrefabs` at runtime** — do not hardcode codes.
2. **`VehicleAgent.VehicleGraphName` / `RoadGraphName` values.** They are static `String` fields;
   the values live in serialized data, not metadata. Read them at runtime if you need to query the
   A\* graphs directly.
3. **Trunk `SlotCount` per shipped vehicle.** Authored on the prefab; not in metadata. Read
   `veh.Storage.SlotCount` and `StorageEntity.MAX_SLOTS` at runtime.
4. **How `LandVehicle.Interacted()` decides between enter-driver / enter-passenger / open-trunk.**
   The branching is in IL, not metadata. `intObj`, `GetFirstFreeSeat()`, `driverEntryPoint` and
   `Trunk` are the inputs — **inferred**.
5. **Whether `NPC.EnterVehicle(null, veh)` is a legal null-connection call.** The parameter is a
   FishNet `NetworkConnection` on an `[ObserversRpc]`/`[TargetRpc]` pair; `null` normally means
   "broadcast to observers" but the game may branch on it. Test on a listen server before relying
   on it — **UNVERIFIED**.
6. **A driver/chauffeur NPC role.** There is **no** `Driver` type, and
   `Il2CppScheduleOne.Employees.EEmployeeType` is `{ Botanist=0, Handler=1, Chemist=2, Cleaner=3 }`
   — no driver member. Hireable Drivers must add its own `Employee` subclass or repurpose an
   existing NPC. Verified absent, not merely unfound.
7. **`PathCalculator`** is `public static class` with an empty member list in the dump — either a
   pure-static holder that the dumper skipped, or genuinely vestigial. **UNVERIFIED.**
