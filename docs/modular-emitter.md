# Modular Tractor Beam

Tractor Beam 0.3.5 is a solo-play preview for Starship EVO. The modular emitter uses native weapon sockets and renders a recessed-optic bay at each stretch segment.

## Requirements

- Windows x64 Mono, Steam build **26451593 / 26w45a**, Unity **6000.6.6f1**.
- Starship Keel **0.1.4 Codex categories candidate** with experimental content and functional blocks enabled in an isolated managed profile. The published 0.1.4 prerelease predates the category API.

## Use

1. Open **Codex → Items → Weapons** and place the **Modular Tractor Beam**. Stretch it vertically to add sockets.
2. Mount one force module. **Pull** draws a target inward, **Push** repels it, and **Slowdown** brakes relative movement in either direction. Duplicate modules do not stack. Conflicting force modes disable the beam.
3. Optionally add **Shield Coupling** to let the beam pass through its own ship's shield, or **Capacity** to increase the eligible target mass and span by 50% at 50% higher power and heat cost. Capacity leaves the force cap unchanged.
4. Link **Yoke → Event Gate → Modular Tractor Beam**. Set the gate to **Primary Fire / Input Signal** and hold the input to operate the beam.

The emitter and addons share paintable warm beige armor. Use the paint eraser to apply that default to an existing part. The stretched hitbox still uses the stock Beam Weapon collider. Its reported alignment issue remains under investigation.

## Build and test

Place the verified candidate SDK and runtime in the mod's local Keel reference directory expected by the build script. Run `pwsh -File .\Build.ps1 -WeaponAttachments` with the game and .NET SDK available. The ZIP contains the mod DLL and Uplink manifest. Use `pwsh -File .\Start-Test.ps1 -Modular` with the prepared isolated test install.

Before upgrading from a version with the original non-modular emitter, remove that part from saved ships and blueprints using the previous build. Modular content keys are unchanged. Back up the profile and its Keel registry together before switching builds.
