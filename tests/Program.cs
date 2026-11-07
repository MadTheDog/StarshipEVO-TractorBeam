using System;
using System.Numerics;
using TractorBeam;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var small = BeamRating.ForSize(1);
var large = BeamRating.ForSize(4);
Check(!small.Allows(small.MaxMass + 1, 1), "Overweight target admitted");
Check(!small.Allows(10, small.MaxSpan + 1), "Oversize target admitted");
Check(!small.Allows(float.NaN, 1), "Invalid target admitted");
Check(large.Allows(small.MaxMass + 1, small.MaxSpan + 1), "Larger emitter cannot pick up a larger target");
var outward = Vector3.UnitX;
var heavyBrake = TractorController.Force(small, small.MaxMass, outward, 100, 5, outward * 1000, false);
Check(heavyBrake.X < 0 && heavyBrake.Length() <= small.MaxForce, "Brake must oppose escape and obey the force limit");
Check(TractorController.Force(small, 100, outward, 30, 5, Vector3.Zero, false) == Vector3.Zero, "Brake mode must not tow");
Check(TractorController.Force(small, 100, outward, 30, 5, Vector3.Zero, true).X < 0, "Retrieve must pull");
Check(TractorController.Force(small, 100, outward, 4, 5, Vector3.Zero, true) == Vector3.Zero, "Retrieve must respect standoff");
Check(TractorController.Force(small, 100, outward, 30, 5, new Vector3(float.NaN), true) == Vector3.Zero, "Invalid velocity must fail closed");

float position = 30, speed = 10;
const float dt = 0.01f, mass = 1000;
for (int step = 0; step < 4000; step++)
{
    var force = TractorController.Force(small, mass, outward, position, 5, outward * speed, true);
    speed += force.X / mass * dt;
    position += speed * dt;
    Check(speed >= -small.TowSpeed - 0.001f, "Unopposed retrieval exceeded the tow speed");
    Check(position >= 4.99f, "Unopposed retrieval overshot the standoff");
}
Check(MathF.Abs(speed) < 0.02f && MathF.Abs(position - 5) < 0.03f, "Unopposed retrieval must settle");
Check(heavyBrake.X + small.MaxForce * 1.2f > 0, "Pilot with stronger opposing thrust must be able to escape");
Check(ModularBeam.SelectMode(BeamModules.Pull) == BeamMode.Pull, "Pull module did not select pull");
Check(ModularBeam.SelectMode(BeamModules.Push | BeamModules.Pull) == BeamMode.Conflict, "Opposing modules must conflict");
Check(ModularBeam.SelectMode(BeamModules.ShieldCoupling | BeamModules.Capacity) == BeamMode.Off, "Support modules must not select a force mode");
Check(ModularBeam.Force(small, 1000, outward, 30, 5, -10 * outward, BeamMode.Pull) == Vector3.Zero, "Pull must not push an incoming target");
Check(ModularBeam.Force(small, 1000, outward, 30, 5, Vector3.Zero, BeamMode.Pull).X < 0, "Pull must attract a stationary target");
Check(ModularBeam.Force(small, 1000, outward, 4, 5, 10 * outward, BeamMode.Pull) == Vector3.Zero, "Pull must stop inside standoff");
Check(ModularBeam.Force(small, 1000, outward, 30, 5, Vector3.Zero, BeamMode.Push).X > 0, "Push must repel a stationary target");
Check(ModularBeam.Force(small, 1000, outward, 30, 5, 10 * outward, BeamMode.Push) == Vector3.Zero, "Push must not pull a fast target");
Check(ModularBeam.Force(small, 1000, outward, 30, 5, -10 * outward, BeamMode.Slowdown).X > 0, "Slowdown must brake an incoming target");
Check(ModularBeam.Rating(1, BeamModules.Capacity).Allows(20_000, 15), "Capacity did not raise both target limits");
Check(ModularBeam.Rating(1, BeamModules.Capacity).MaxForce == small.MaxForce, "Capacity must preserve the force cap");
Check(ModularBeam.PowerMultiplier(BeamModules.Capacity) == 1.5f, "Capacity must increase power and heat demand");
Check(ModularBeam.Force(small, small.MaxMass, outward, 30, 5, -1000 * outward, BeamMode.Push).X == small.MaxForce,
    "Push must obey the force cap even for fast incoming targets");
Console.WriteLine("Tractor balance and retrieval checks passed.");
