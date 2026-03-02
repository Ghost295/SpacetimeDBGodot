using System;
using System.Collections.Generic;
using SpacetimeDB;

public static partial class Module
{
    // private const uint DefaultTickRateTps = 20;
    // private const float FlowLookAheadDistance = 5.0f;
    // private const float MinVectorSqrMagnitude = 0.000001f;
    // private const float DefaultSeparationRadius = 0.8f;
    // private const float DefaultAlignmentRadius = 3.0f;
    // private const float DefaultCohesionRadius = 4f;
    // private const float DefaultSeparationWeight = 50f;
    // private const float DefaultAlignmentWeight = 0.3f;
    // private const float DefaultCohesionWeight = 0.2f;
    // private const float DefaultMaxSpeed = 6.0f;
    // private const float DefaultMaxForce = 20.0f;
    // private const ulong DefaultRngState = 0x9E3779B97F4A7C15UL;

    // [SpacetimeDB.Reducer(ReducerKind.Init)]
    // public static void Init(ReducerContext ctx)
    // {
    //     var config = EnsureConfig(ctx);
    //     ResetTickTimer(ctx, config.TickRateTps);
    // }

    // [SpacetimeDB.Reducer]
    // public static void Add(ReducerContext ctx, int x, int y)
    // {
    //     ctx.Db.Entity.Insert(new Entity { Position = new DBVector2(x, y) });
    // }

    // [SpacetimeDB.Reducer]
    // public static void SayHello(ReducerContext ctx)
    // {
    //     Log.Info("Hello, World!");
    // }

    // [SpacetimeDB.Reducer]
    // public static void SpawnBoids(ReducerContext ctx, int count, float spawnRadius)
    // {
    //     if (count <= 0)
    //     {
    //         throw new Exception("count must be > 0");
    //     }

    //     if (spawnRadius < 0f)
    //     {
    //         throw new Exception("spawnRadius must be >= 0");
    //     }

    //     var config = EnsureConfig(ctx);
    //     var rngState = config.RngState;
    //     var initialSpeed = MathF.Max(0.1f, config.MaxSpeed * 0.5f);

    //     for (var i = 0; i < count; i++)
    //     {
    //         var angle = NextRandomFloat01(ref rngState) * MathF.PI * 2f;
    //         var radius = MathF.Sqrt(NextRandomFloat01(ref rngState)) * spawnRadius;
    //         var direction = new DBVector2(MathF.Cos(angle), MathF.Sin(angle));
    //         var position = direction * radius;

    //         var velocityAngle = NextRandomFloat01(ref rngState) * MathF.PI * 2f;
    //         var velocity = new DBVector2(MathF.Cos(velocityAngle), MathF.Sin(velocityAngle)) * initialSpeed;

    //         ctx.Db.Boid.Insert(new Boid
    //         {
    //             BoidId = 0,
    //             Position = new DBVector2(277.21f, 364.438f),
    //             Velocity = velocity,
    //             Team = 0
    //         });
    //     }

    //     config.RngState = rngState;
    //     ctx.Db.SimConfig.Id.Update(config);
    // }

    // [SpacetimeDB.Reducer]
    // public static void ClearBoids(ReducerContext ctx)
    // {
    //     var boids = new List<Boid>();
    //     foreach (var boid in ctx.Db.Boid.Iter())
    //     {
    //         boids.Add(boid);
    //     }

    //     foreach (var boid in boids)
    //     {
    //         ctx.Db.Boid.BoidId.Delete(boid.BoidId);
    //     }
    // }

    // [SpacetimeDB.Reducer]
    // public static void SetTickRate(ReducerContext ctx, uint tps)
    // {
    //     if (tps == 0)
    //     {
    //         throw new Exception("tps must be > 0");
    //     }

    //     var config = EnsureConfig(ctx);
    //     config.TickRateTps = tps;
    //     ctx.Db.SimConfig.Id.Update(config);
    //     ResetTickTimer(ctx, tps);
    // }

    // [SpacetimeDB.Reducer]
    // public static void SetBoidsParams(
    //     ReducerContext ctx,
    //     float separationRadius,
    //     float alignmentRadius,
    //     float cohesionRadius,
    //     float separationWeight,
    //     float alignmentWeight,
    //     float cohesionWeight,
    //     float maxSpeed,
    //     float maxForce)
    // {
    //     if (separationRadius <= 0f || alignmentRadius <= 0f || cohesionRadius <= 0f)
    //     {
    //         throw new Exception("all radii must be > 0");
    //     }

    //     if (maxSpeed <= 0f || maxForce <= 0f)
    //     {
    //         throw new Exception("maxSpeed and maxForce must be > 0");
    //     }

    //     var config = EnsureConfig(ctx);
    //     config.SeparationRadius = separationRadius;
    //     config.AlignmentRadius = alignmentRadius;
    //     config.CohesionRadius = cohesionRadius;
    //     config.SeparationWeight = separationWeight;
    //     config.AlignmentWeight = alignmentWeight;
    //     config.CohesionWeight = cohesionWeight;
    //     config.MaxSpeed = maxSpeed;
    //     config.MaxForce = maxForce;
    //     ctx.Db.SimConfig.Id.Update(config);
    // }

    // [SpacetimeDB.Reducer]
    // public static void Tick(ReducerContext ctx, SimTickTimer timer)
    // {
    //     var config = EnsureConfig(ctx);
    //     var tickRate = Math.Max(1u, config.TickRateTps);
    //     var dt = 1.0f / tickRate;
    //     var maxSpeed = MathF.Max(0f, config.MaxSpeed);
    //     var maxForce = MathF.Max(0f, config.MaxForce);

    //     var boids = new List<Boid>();
    //     foreach (var boid in ctx.Db.Boid.Iter())
    //     {
    //         boids.Add(boid);
    //     }

    //     for (var i = 0; i < boids.Count; i++)
    //     {
    //         var updated = boids[i];

    //         var steerForce = ComputeSteeringForce(boids, i, config, maxSpeed);
    //         steerForce = LimitMagnitude(steerForce, maxForce);

    //         var velocity = updated.Velocity + steerForce * dt;
    //         velocity = LimitMagnitude(velocity, maxSpeed);

    //         updated.Velocity = velocity;
    //         updated.Position = updated.Position + velocity * dt;

    //         ctx.Db.Boid.BoidId.Update(updated);
    //     }
    // }

    // [Table(Accessor = "Entity", Public = true)]
    // public partial struct Entity
    // {
    //     [PrimaryKey, AutoInc]
    //     public int EntityId;
    //     public DBVector2 Position;
    // }

    // [Table(Accessor = "Boid", Public = true)]
    // public partial struct Boid
    // {
    //     [PrimaryKey, AutoInc]
    //     public int BoidId;
    //     public DBVector2 Position;
    //     public DBVector2 Velocity;
    //     public int Team;
    // }

    // [Table(Accessor = "SimConfig", Public = true)]
    // public partial struct SimConfig
    // {
    //     [PrimaryKey]
    //     public int Id;
    //     public uint TickRateTps;
    //     public float SeparationRadius;
    //     public float AlignmentRadius;
    //     public float CohesionRadius;
    //     public float SeparationWeight;
    //     public float AlignmentWeight;
    //     public float CohesionWeight;
    //     public float MaxSpeed;
    //     public float MaxForce;
    //     public ulong RngState;
    // }

    // [Table(Accessor = "SimTickTimer", Scheduled = nameof(Tick))]
    // public partial struct SimTickTimer
    // {
    //     [PrimaryKey, AutoInc]
    //     public ulong TimerId;
    //     public ScheduleAt ScheduledAt;
    // }

    // private static SimConfig EnsureConfig(ReducerContext ctx)
    // {
    //     if (ctx.Db.SimConfig.Id.Find(0) is SimConfig config)
    //     {
    //         return config;
    //     }

    //     var inserted = ctx.Db.SimConfig.Insert(new SimConfig
    //     {
    //         Id = 0,
    //         TickRateTps = DefaultTickRateTps,
    //         SeparationRadius = DefaultSeparationRadius,
    //         AlignmentRadius = DefaultAlignmentRadius,
    //         CohesionRadius = DefaultCohesionRadius,
    //         SeparationWeight = DefaultSeparationWeight,
    //         AlignmentWeight = DefaultAlignmentWeight,
    //         CohesionWeight = DefaultCohesionWeight,
    //         MaxSpeed = DefaultMaxSpeed,
    //         MaxForce = DefaultMaxForce,
    //         RngState = DefaultRngState
    //     });

    //     return inserted;
    // }

    // private static void ResetTickTimer(ReducerContext ctx, uint tps)
    // {
    //     var timers = new List<SimTickTimer>();
    //     foreach (var timer in ctx.Db.SimTickTimer.Iter())
    //     {
    //         timers.Add(timer);
    //     }

    //     foreach (var timer in timers)
    //     {
    //         ctx.Db.SimTickTimer.TimerId.Delete(timer.TimerId);
    //     }

    //     var clampedTps = Math.Max(1u, tps);
    //     var intervalMs = 1000.0 / clampedTps;
    //     ctx.Db.SimTickTimer.Insert(new SimTickTimer
    //     {
    //         TimerId = 0,
    //         ScheduledAt = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(intervalMs))
    //     });
    // }

    // private static DBVector2 LimitMagnitude(DBVector2 vector, float maxMagnitude)
    // {
    //     if (maxMagnitude <= 0f)
    //     {
    //         return new DBVector2();
    //     }

    //     var sqrMagnitude = vector.SqrMagnitude;
    //     if (sqrMagnitude <= maxMagnitude * maxMagnitude)
    //     {
    //         return vector;
    //     }

    //     if (sqrMagnitude <= MinVectorSqrMagnitude)
    //     {
    //         return new DBVector2();
    //     }

    //     var scale = maxMagnitude / MathF.Sqrt(sqrMagnitude);
    //     return vector * scale;
    // }

    // private static DBVector2 SetMagnitude(DBVector2 vector, float magnitude)
    // {
    //     if (magnitude <= 0f)
    //     {
    //         return new DBVector2();
    //     }

    //     var sqrMagnitude = vector.SqrMagnitude;
    //     if (sqrMagnitude <= MinVectorSqrMagnitude)
    //     {
    //         return new DBVector2();
    //     }

    //     var scale = magnitude / MathF.Sqrt(sqrMagnitude);
    //     return vector * scale;
    // }

    // private static uint NextRandomU32(ref ulong state)
    // {
    //     if (state == 0)
    //     {
    //         state = DefaultRngState;
    //     }

    //     state ^= state >> 12;
    //     state ^= state << 25;
    //     state ^= state >> 27;
    //     state *= 2685821657736338717UL;
    //     return (uint)(state >> 32);
    // }

    // private static float NextRandomFloat01(ref ulong state)
    // {
    //     return NextRandomU32(ref state) / (float)uint.MaxValue;
    // }

    // private static DBVector2 ComputeSteeringForce(
    //     List<Boid> boids,
    //     int boidIndex,
    //     SimConfig config,
    //     float maxSpeed)
    // {
    //     var boid = boids[boidIndex];
    //     var position = boid.Position;
    //     var velocity = boid.Velocity;

    //     var separationRadiusSq = config.SeparationRadius * config.SeparationRadius;
    //     var alignmentRadiusSq = config.AlignmentRadius * config.AlignmentRadius;
    //     var cohesionRadiusSq = config.CohesionRadius * config.CohesionRadius;

    //     var separationForce = new DBVector2();
    //     var alignmentVelocity = new DBVector2();
    //     var cohesionCenter = new DBVector2();
    //     var separationCount = 0;
    //     var alignmentCount = 0;
    //     var cohesionCount = 0;

    //     for (var j = 0; j < boids.Count; j++)
    //     {
    //         if (j == boidIndex)
    //         {
    //             continue;
    //         }

    //         var neighbor = boids[j];
    //         var offset = neighbor.Position - position;
    //         var distSq = offset.SqrMagnitude;
    //         if (distSq <= MinVectorSqrMagnitude)
    //         {
    //             continue;
    //         }

    //         var sameTeam = neighbor.Team == boid.Team;
    //         if (!sameTeam)
    //         {
    //             continue;
    //         }

    //         var dist = MathF.Sqrt(distSq);
    //         var invDist = 1f / dist;
    //         var dir = offset * invDist;

    //         if (distSq < separationRadiusSq && config.SeparationRadius > 0f)
    //         {
    //             var t = 1f - (dist / config.SeparationRadius);
    //             var weight = t * t;
    //             separationForce = separationForce - dir * weight;
    //             separationCount++;
    //         }

    //         if (distSq < alignmentRadiusSq)
    //         {
    //             alignmentVelocity = alignmentVelocity + neighbor.Velocity;
    //             alignmentCount++;
    //         }

    //         if (distSq < cohesionRadiusSq)
    //         {
    //             cohesionCenter = cohesionCenter + neighbor.Position;
    //             cohesionCount++;
    //         }
    //     }

    //     var finalizedSeparation = new DBVector2();
    //     if (separationCount > 0)
    //     {
    //         finalizedSeparation = (separationForce / separationCount) * config.SeparationWeight;
    //     }

    //     var finalizedAlignment = new DBVector2();
    //     if (alignmentCount > 0)
    //     {
    //         var avgVelocity = alignmentVelocity / alignmentCount;
    //         finalizedAlignment = SetMagnitude(avgVelocity - velocity, config.AlignmentWeight);
    //     }

    //     var finalizedCohesion = new DBVector2();
    //     if (cohesionCount > 0)
    //     {
    //         var center = cohesionCenter / cohesionCount;
    //         var toCenter = center - position;
    //         if (toCenter.SqrMagnitude > 0.01f)
    //         {
    //             finalizedCohesion = SetMagnitude(toCenter, config.CohesionWeight);
    //         }
    //     }

    //     var seekForce = new DBVector2();
    //     if (maxSpeed > 0f &&
    //         BakedFlowField.TryGetDirection(boid.Team, position.x, position.y, out var flowDirection))
    //     {
    //         var flowTarget = position + flowDirection * FlowLookAheadDistance;
    //         var desiredVelocity = SetMagnitude(flowTarget - position, maxSpeed);
    //         seekForce = desiredVelocity - velocity;
    //     }

    //     return seekForce + finalizedSeparation + finalizedAlignment + finalizedCohesion;
    // }
}
