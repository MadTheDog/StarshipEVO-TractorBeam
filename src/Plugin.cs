using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using Common.Rendering;
using HarmonyLib;
using Manager;
using Sevo.Framework.Runtime;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine;
using NVector = System.Numerics.Vector3;

namespace TractorBeam
{
    [BepInPlugin(Id, "Tractor Beam", "0.1.0")]
    [BepInDependency(Sevo.Framework.Runtime.Plugin.Id)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "local.sevo.tractorbeam";
        private readonly Dictionary<(VoxelEntity entity, int instance), BrickCoord> emitters =
            new Dictionary<(VoxelEntity, int), BrickCoord>();
        private static Plugin current;
        private static Brick definition;
        private ConfigEntry<KeyboardShortcut> brakeKey;
        private ConfigEntry<KeyboardShortcut> retrieveKey;
        private bool available;
        private ParentEntity source;
        private VoxelEntity emitterEntity;
        private int emitterInstance;
        private ParentEntity target;
        private bool retrieve;

        private void Awake()
        {
            var harmony = new Harmony(Id);
            try
            {
                brakeKey = Config.Bind("Controls", "Brake", new KeyboardShortcut(KeyCode.F8),
                    "Press from the cockpit to acquire a ship and brake. Press again to release.");
                retrieveKey = Config.Bind("Controls", "Retrieve", new KeyboardShortcut(KeyCode.F9),
                    "Press from the cockpit to acquire and pull a ship. Press again to release.");
                var added = AccessTools.DeclaredMethod(typeof(Brick), nameof(Brick.OnBrickAddedToEntity),
                    new[] { typeof(BrickCoord), typeof(bool), typeof(Dictionary<int, byte[]>) });
                var removed = AccessTools.DeclaredMethod(typeof(Brick), nameof(Brick.OnBrickRemoved),
                    new[] { typeof(BrickCoord), typeof(BrickUnspawnMode) });
                if (added?.ReturnType != typeof(void) || removed?.ReturnType != typeof(void))
                    throw new MissingMethodException("Brick placement/removal signatures changed.");

                ValidatePhysicsContracts();
                current = this;
                harmony.Patch(added, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterBrickAdded)));
                harmony.Patch(removed, prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeBrickRemoved)));
                ContentRegistration.Register(Id, "emitter", 1, CreateEmitter);
                available = true;
                Logger.LogInfo("Declared Tractor Beam emitter for Keel. Experimental content mode is required.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                current = null;
                Logger.LogError("Tractor Beam disabled: " + ex);
            }
        }

        private static void ValidatePhysicsContracts()
        {
            // Explicitly inspect the methods whose absence would otherwise fail after block registration.
            if (AccessTools.DeclaredMethod(typeof(SkyRigidbody.Handle), "AddForce",
                    new[] { typeof(float3) })?.ReturnType != typeof(void) ||
                AccessTools.DeclaredMethod(typeof(SkyPhysics.QueryRaycast), "ExecuteHitOut",
                    new[] { typeof(VoxelRaycastHit).MakeByRefType() })?.ReturnType != typeof(bool) ||
                AccessTools.DeclaredMethod(typeof(FlowBalance), "AddStock",
                    new[] { typeof(float) })?.ReturnType != typeof(void) ||
                AccessTools.DeclaredProperty(typeof(EntitySystems), nameof(EntitySystems.Data))?.SetMethod == null ||
                AccessTools.DeclaredField(typeof(EntitySystemResources.EntitySystemData),
                    nameof(EntitySystemResources.EntitySystemData.powerConsumedFromJobs))?.FieldType != typeof(float))
                throw new MissingMethodException("Tractor Beam requires native force, raycast and power contracts.");
        }

        private static object CreateEmitter(int id)
        {
            definition = (Brick)StockShape.CreateHull(id, "Tractor Beam");
            return definition;
        }

        private static void AfterBrickAdded(Brick __instance, BrickCoord __0)
        {
            if (current == null || definition == null || !ReferenceEquals(__instance, definition) || !__0.IsValid) return;
            current.emitters[(__0.entity, __0.brickData.instanceId)] = __0;
        }

        private static void BeforeBrickRemoved(Brick __instance, BrickCoord __0)
        {
            if (current == null || !ReferenceEquals(__instance, definition) || !__0.IsValid) return;
            current.emitters.Remove((__0.entity, __0.brickData.instanceId));
            if (current.emitterEntity == __0.entity && current.emitterInstance == __0.brickData.instanceId)
                current.Release();
        }

        private void Update()
        {
            if (!available) return;
            try { UpdateBeam(); }
            catch (Exception ex)
            {
                Release();
                available = false;
                Logger.LogError("Tractor Beam force disabled for this session: " + ex);
            }
        }

        private void UpdateBeam()
        {
            var ship = ParentEntity.PlayerParentEntity;
            if (PlayerState.sessionMode != PlayerState.SessionMode.solo || !PlayerState.SystemEntered ||
                PlayerState.InLoadingScreen || PlayerState.InMenu || !Application.isFocused ||
                Time.deltaTime <= 0 || ship == null ||
                ship.controlMode != BaseVehicle.VehicleControlMode.localPlayer || ship.Rigidbody.isParented)
            {
                Release();
                return;
            }

            bool brake = brakeKey.Value.IsDown();
            bool pull = retrieveKey.Value.IsDown();
            if (brake || pull)
            {
                bool toggleOff = source == ship && retrieve == pull;
                Release();
                if (!toggleOff) Acquire(ship, pull);
            }

            if (source != ship || target == null ||
                !emitters.TryGetValue((emitterEntity, emitterInstance), out var emitter))
            {
                Release();
                return;
            }

            var rating = Rating(emitter);
            if (!CanAffect(target, ship, rating) || !Cast(emitter, rating, out var hit) ||
                !ReferenceEquals(hit.Entity.Parent, target))
            {
                Release();
                return;
            }

            Vector3 origin = Muzzle(emitter);
            var body = target.Rigidbody;
            Vector3 center = target.TransformData.LocalToWorld(body.ownCenterOfMass);
            Vector3 offset = center - origin;
            float distance = offset.magnitude;
            if (!(distance > 0.01f && distance <= rating.Range + target.ParentRadius))
            {
                Release();
                return;
            }

            Vector3 relative = body.LinearVelocity - ship.Rigidbody.VelocityAtPoint(origin);
            float standoff = Mathf.Max(5, target.ParentRadius + 3);
            var force = TractorController.Force(rating, body.Mass, ToNumerics(offset / distance),
                distance, standoff, ToNumerics(relative), retrieve);
            float load = force.Length() / rating.MaxForce;
            float step = Mathf.Min(Time.deltaTime, 0.05f);
            var systems = ship.Systems.Data;
            var heat = ship.Systems.Heat;
            float energy = (0.05f + 0.45f * load) * rating.MaxForce / 20_000 * step;
            float thermal = energy * 0.5f;
            if (systems.IsWarping || systems.IsStalled ||
                !(systems.powerStored - systems.powerConsumedFromJobs >= energy &&
                  heat.MaxStock > 0 && heat.stock + thermal < heat.MaxStock))
            {
                Release();
                return;
            }

            // Use the native pending power debit, as stock beam recharge does.
            systems.powerConsumedFromJobs += energy;
            ship.Systems.Data = systems;
            heat.AddStock(thermal);
            if (load > 0)
            {
                body.WakeUpRigidbody();
                body.OwnSkyRigidbody.AddForce(new float3(force.X, force.Y, force.Z));
            }

            if (VoxelRenderingManager.DebugBuffer != null)
                DrawDebugGizmo.DebugLine(VoxelRenderingManager.DebugBuffer, origin, hit.WorldPoint,
                    retrieve ? new Color32(80, 230, 255, 255) : new Color32(255, 175, 60, 255), 0.12f);
        }

        private void Acquire(ParentEntity ship, bool pull)
        {
            float nearest = float.PositiveInfinity;
            foreach (var entry in emitters)
            {
                var emitter = entry.Value;
                if (!emitter.IsValid || emitter.Parent != ship) continue;
                var rating = Rating(emitter);
                if (!Cast(emitter, rating, out var hit) || hit.distance >= nearest) continue;
                var candidate = hit.Entity.Parent;
                if (!CanAffect(candidate, ship, rating)) continue;
                nearest = hit.distance;
                emitterEntity = entry.Key.entity;
                emitterInstance = entry.Key.instance;
                target = candidate;
            }

            if (target == null) return;
            source = ship;
            retrieve = pull;
        }

        private void Release()
        {
            source = null;
            target = null;
            emitterEntity = null;
            emitterInstance = 0;
        }

        private static Vector3 LocalMuzzle(BrickCoord emitter) =>
            emitter.brickData.GetPositionAlongYAxis(1f) + emitter.LocalUp * 0.02f;

        private static Vector3 Muzzle(BrickCoord emitter) =>
            emitter.TransformData.LocalToWorld(LocalMuzzle(emitter));

        private static bool Cast(BrickCoord emitter, BeamRating rating, out VoxelRaycastHit hit)
        {
            var ray = new SkyPhysics.SkyRay(emitter.TransformData.LocalToSystem(LocalMuzzle(emitter)), emitter.WorldUp);
            var query = new SkyPhysics.QueryRaycast(ray, rating.Range, Allocator.None, SkyPhysics.CONTEXT)
            {
                includeShield = true,
                includeWater = true
            };
            return query.ExecuteHitOut(out hit) && hit.isHitBrick && hit.Entity != null;
        }

        private static bool CanAffect(ParentEntity ship, ParentEntity sourceShip, BeamRating rating)
        {
            if (ship == null || ship == sourceShip || ship.Type != EntityType.spaceship ||
                !ship.IsInitialized || ship.WillBeUnspawned ||
                ship.Systems.IsNPCStation || ship.EntityStation.IsStationParent || ship.Systems.Data.IsWarping)
                return false;
            var body = ship.Rigidbody;
            if (!body.OwnSkyRigidbody.IsValid || body.IsForcedStaticSelf.Value || body.isParented ||
                body.dockedEntities.Count != 0 || ship.EntityMetaData.isAntigravStatis)
                return false;
            foreach (var anchor in ship.Systems.antiGravAnchors)
                if (anchor.State > 0) return false;
            var size = ship.ParentBounds.size;
            return rating.Allows(body.Mass, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
        }

        private static BeamRating Rating(BrickCoord emitter) =>
            BeamRating.ForSize(emitter.brickData.GridSize);

        private static NVector ToNumerics(Vector3 vector) => new NVector(vector.x, vector.y, vector.z);
    }
}
