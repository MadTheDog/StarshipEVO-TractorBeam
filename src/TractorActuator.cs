using System;
using System.Collections.Generic;
using HarmonyLib;
using Manager;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine;
using NVector = System.Numerics.Vector3;

namespace TractorBeam
{
    // Independent of the block provider. The provider supplies a fresh placement and a sustained input.
    internal sealed class TractorActuator
    {
        private ParentEntity target;
        internal string Status { get; private set; }

        internal static void ValidateContracts()
        {
            if (AccessTools.DeclaredMethod(typeof(SkyRigidbody.Handle), nameof(SkyRigidbody.Handle.AddForce),
                    new[] { typeof(float3) })?.ReturnType != typeof(void) ||
                AccessTools.DeclaredMethod(typeof(SkyPhysics.QueryRaycast), nameof(SkyPhysics.QueryRaycast.ExecuteHitOut),
                    new[] { typeof(VoxelRaycastHit).MakeByRefType() })?.ReturnType != typeof(bool) ||
                AccessTools.DeclaredMethod(typeof(FlowBalance), nameof(FlowBalance.AddStock),
                    new[] { typeof(float) })?.ReturnType != typeof(void) ||
                AccessTools.DeclaredProperty(typeof(EntitySystems), nameof(EntitySystems.Data))?.SetMethod == null ||
                AccessTools.DeclaredField(typeof(EntitySystemResources.EntitySystemData),
                    nameof(EntitySystemResources.EntitySystemData.powerConsumedFromJobs))?.FieldType != typeof(float))
                throw new MissingMethodException("Tractor Beam requires native force, raycast and power contracts.");
        }

        internal void Release() => target = null;

        internal bool Tick(BrickCoord emitter, bool retrieve, HashSet<ParentEntity> affectedThisTick,
            out BeamFrame feedback, BeamModules? modules = null)
        {
            feedback = default;
            Status = "Waiting for a simulated emitter.";
            if (!emitter.IsValid || !PlayerState.SystemEntered || PlayerState.InLoadingScreen ||
                PlayerState.InMenu || Time.deltaTime <= 0)
            {
                Release();
                return false;
            }

            var source = emitter.Parent;
            if (source == null || !source.IsInitialized || source.WillBeUnspawned ||
                !source.Rigidbody.SkyRigidbody.IsValid)
            {
                Release();
                return false;
            }

            var data = emitter.brickData;
            var mode = modules.HasValue ? ModularBeam.SelectMode(modules.Value) : BeamMode.Off;
            var rating = modules.HasValue ? ModularBeam.Rating(data.GridSize, modules.Value) : BeamRating.ForSize(data.GridSize);
            float powerMultiplier = modules.HasValue ? ModularBeam.PowerMultiplier(modules.Value) : 1f;
            // Same muzzle offset as the stock beam family, including vertical stretch.
            Vector3 localMuzzle = emitter.Position + (data.Scale.y + 1.02f) * data.GridSize * emitter.LocalUp;
            Vector3 origin = emitter.TransformData.LocalToWorld(localMuzzle);
            var ray = new SkyPhysics.SkyRay(emitter.TransformData.LocalToSystem(localMuzzle), emitter.WorldUp);
            bool shieldCoupled = (modules.GetValueOrDefault() & BeamModules.ShieldCoupling) != 0;
            bool hasHit = FindHit(ray, rating.Range, source, shieldCoupled, out var hit);
            var systems = source.Systems.Data;
            var heat = source.Systems.Heat;
            float step = Mathf.Min(Time.deltaTime, 0.05f);
            float standbyEnergy = 0.05f * rating.MaxForce / 20_000 * step * powerMultiplier;
            bool ready = CanSupply(systems, heat, standbyEnergy);
            feedback = new BeamFrame(origin, hasHit ? hit.WorldPoint : origin + emitter.WorldUp * rating.Range,
                emitter.WorldUp, data.GridSize, ready ? BeamState.Searching : BeamState.Unpowered);
            if (modules.HasValue && (mode == BeamMode.Off || mode == BeamMode.Conflict))
            {
                Status = mode == BeamMode.Off ? "Install a Pull, Push or Slowdown module." :
                    "Choose only one of Pull, Push or Slowdown.";
                Release();
                return false;
            }
            Status = !ready ? "Insufficient power or cooling, or emitter ship is warping." :
                !hasHit ? "No hull in range along the nozzle axis." :
                !hit.isHitBrick ? $"Beam intercepted by {hit.type}." : "Hull hit.";
            if (!ready || !hasHit || !hit.isHitBrick || hit.Entity == null)
            {
                Release();
                return false;
            }

            var candidate = hit.Entity.Parent;
            Status = target != null && target != candidate
                ? "Previous target lost. Checking new target."
                : Rejection(candidate, source, rating);
            if (Status != null)
            {
                Release();
                return false;
            }

            target = candidate;
            if (affectedThisTick.Contains(target))
            {
                Status = "Target already held by another Tractor Beam.";
                return false;
            }

            var body = target.Rigidbody;
            Vector3 center = target.TransformData.LocalToWorld(body.ownCenterOfMass);
            Vector3 offset = center - origin;
            float distance = offset.magnitude;
            if (!(distance > 0.01f && distance <= rating.Range + target.ParentRadius))
            {
                Status = "Target center of mass is outside the capture range.";
                Release();
                return false;
            }

            Vector3 relative = body.LinearVelocity - source.Rigidbody.VelocityAtPoint(origin);
            float standoff = Mathf.Max(5, target.ParentRadius + 3);
            var force = modules.HasValue
                ? ModularBeam.Force(rating, body.Mass, ToNumerics(offset / distance), distance, standoff, ToNumerics(relative), mode)
                : TractorController.Force(rating, body.Mass, ToNumerics(offset / distance), distance, standoff, ToNumerics(relative), retrieve);
            float load = force.Length() / rating.MaxForce;
            float energy = (0.05f + 0.45f * load) * rating.MaxForce / 20_000 * step * powerMultiplier;
            float thermal = energy * 0.5f;
            if (!CanSupply(systems, heat, energy))
            {
                Status = "Insufficient power or cooling for this lock.";
                feedback = feedback.WithState(BeamState.Unpowered);
                Release();
                return false;
            }

            systems.powerConsumedFromJobs += energy;
            source.Systems.Data = systems;
            heat.AddStock(thermal);
            if (load > 0)
            {
                body.WakeUpRigidbody();
                body.OwnSkyRigidbody.AddForce(new float3(force.X, force.Y, force.Z));
            }

            affectedThisTick.Add(target);
            Status = "Target locked.";
            feedback = feedback.WithState(BeamState.Locked);
            return true;
        }

        private static bool FindHit(SkyPhysics.SkyRay ray, float range, ParentEntity source,
            bool shieldCoupled, out VoxelRaycastHit hit)
        {
            if (!Cast(ray, range, true, 0, out hit)) return false;
            if (!shieldCoupled || hit.type != VoxelRaycastHit.Type.shield || hit.Entity != source)
                return true;

            // Keep third-party shields with the source skipped. Compare against a solid-only
            // query so the emitter still cannot fire through its own hull.
            bool shieldHit = Cast(ray, range, true, source.SkyTransform.id, out var shieldAware);
            bool solidHit = Cast(ray, range, false, 0, out var solid);
            if (solidHit && (!shieldHit || solid.distance < shieldAware.distance))
            {
                hit = solid;
                return true;
            }
            hit = shieldAware;
            return shieldHit;
        }

        private static bool Cast(SkyPhysics.SkyRay ray, float range, bool shields, int excludedParent,
            out VoxelRaycastHit hit)
        {
            var query = new SkyPhysics.QueryRaycast(ray, range, Allocator.None, SkyPhysics.CONTEXT)
            {
                includeShield = shields,
                includeWater = true,
                ignoreParentTransformId = excludedParent
            };
            return query.ExecuteHitOut(out hit);
        }

        private static bool CanSupply(EntitySystemResources.EntitySystemData systems, FlowBalance heat, float energy) =>
            !systems.IsWarping && !systems.IsStalled &&
            systems.powerStored - systems.powerConsumedFromJobs >= energy &&
            heat.MaxStock > 0 && heat.stock + energy * 0.5f < heat.MaxStock;

        private static string Rejection(ParentEntity ship, ParentEntity source, BeamRating rating)
        {
            if (ship == source) return "Beam intercepted by its own ship.";
            if (ship == null || ship.Type != EntityType.spaceship) return "Hit is not a spaceship.";
            if (!ship.IsInitialized || ship.WillBeUnspawned) return "Target is not currently simulated.";
            if (ship.Systems.IsNPCStation || ship.EntityStation.IsStationParent) return "Target is a station.";
            if (ship.Systems.Data.IsWarping) return "Target is warping.";
            var body = ship.Rigidbody;
            if (!body.OwnSkyRigidbody.IsValid || body.IsForcedStaticSelf.Value) return "Target is fixed or has no active rigid body.";
            if (body.isParented || body.dockedEntities.Count != 0) return "Target is docked or has a docked ship.";
            // The creative build stasis flag changes damping/gravity. It does not fix the body in place.
            foreach (var anchor in ship.Systems.antiGravAnchors)
                if (anchor.State > 0) return "Target has an active antigravity anchor.";
            var size = ship.ParentBounds.size;
            float span = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (!(body.Mass > 0 && body.Mass <= rating.MaxMass)) return "Target exceeds the mass limit or has invalid mass.";
            if (!rating.Allows(body.Mass, span)) return "Target exceeds the hull span limit or has invalid bounds.";
            return null;
        }

        private static NVector ToNumerics(Vector3 vector) => new NVector(vector.x, vector.y, vector.z);
    }
}
