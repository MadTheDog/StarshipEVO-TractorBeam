using System;
using System.Numerics;

namespace TractorBeam
{
    internal readonly struct BeamRating
    {
        internal readonly float MaxMass, MaxSpan, MaxForce, Range, TowSpeed;

        private BeamRating(float size, float capacity)
        {
            MaxMass = 15_000 * size * size * capacity;
            MaxSpan = 12 * size * capacity;
            MaxForce = 20_000 * size * size;
            Range = 40 + 20 * size;
            TowSpeed = 6;
        }

        internal static BeamRating ForSize(float size, float capacity = 1) =>
            new BeamRating(Math.Clamp(size, 0.25f, 8), capacity);

        internal bool Allows(float mass, float span) =>
            mass > 0 && mass <= MaxMass && span > 0 && span <= MaxSpan;
    }

    internal static class TractorController
    {
        internal static Vector3 Force(BeamRating rating, float mass, Vector3 outward,
            float distance, float standoff, Vector3 relativeVelocity, bool retrieve)
        {
            if (!(mass > 0 && mass <= rating.MaxMass) || !Finite(relativeVelocity) ||
                !Finite(outward) || !float.IsFinite(distance) || !float.IsFinite(standoff))
                return Vector3.Zero;

            // Taper the requested closing speed so a drifting ship settles at standoff.
            float gap = Math.Max(0, distance - standoff);
            float requestedSpeed = retrieve ? Math.Min(rating.TowSpeed, gap * 0.65f) : 0;
            Vector3 velocityError = -requestedSpeed * outward - relativeVelocity;
            float error = velocityError.Length();
            if (!(error > 0) || !float.IsFinite(error)) return Vector3.Zero;

            const float response = 0.45f;
            return velocityError / error * Math.Min(rating.MaxForce, mass * error / response);
        }

        internal static bool Finite(Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }

    [Flags]
    internal enum BeamModules
    {
        None = 0, Pull = 1, Push = 2, Slowdown = 4, ShieldCoupling = 8, Capacity = 16
    }

    internal enum BeamMode { Off, Pull, Push, Slowdown, Conflict }

    internal static class ModularBeam
    {
        internal static BeamMode SelectMode(BeamModules modules)
        {
            switch (modules & (BeamModules.Pull | BeamModules.Push | BeamModules.Slowdown))
            {
                case BeamModules.None: return BeamMode.Off;
                case BeamModules.Pull: return BeamMode.Pull;
                case BeamModules.Push: return BeamMode.Push;
                case BeamModules.Slowdown: return BeamMode.Slowdown;
                default: return BeamMode.Conflict;
            }
        }

        internal static BeamRating Rating(float size, BeamModules modules) =>
            BeamRating.ForSize(size, (modules & BeamModules.Capacity) != 0 ? 1.5f : 1f);

        internal static float PowerMultiplier(BeamModules modules) =>
            (modules & BeamModules.Capacity) != 0 ? 1.5f : 1f;

        internal static Vector3 Force(BeamRating rating, float mass, Vector3 outward, float distance,
            float standoff, Vector3 relativeVelocity, BeamMode mode)
        {
            if (mode == BeamMode.Slowdown)
                return TractorController.Force(rating, mass, outward, distance, standoff, relativeVelocity, false);
            if (!(mass > 0 && mass <= rating.MaxMass) || !TractorController.Finite(outward) ||
                !TractorController.Finite(relativeVelocity) || !float.IsFinite(distance) || !float.IsFinite(standoff))
                return Vector3.Zero;

            float speed = Vector3.Dot(relativeVelocity, outward);
            float requestedAcceleration;
            if (mode == BeamMode.Pull && distance > standoff)
                requestedAcceleration = Math.Min(0, -Math.Min(rating.TowSpeed, (distance - standoff) * 0.65f) - speed);
            else if (mode == BeamMode.Push)
                requestedAcceleration = Math.Max(0, rating.TowSpeed - speed);
            else
                return Vector3.Zero;

            return outward * Math.Clamp(mass * requestedAcceleration / 0.45f, -rating.MaxForce, rating.MaxForce);
        }
    }
}
