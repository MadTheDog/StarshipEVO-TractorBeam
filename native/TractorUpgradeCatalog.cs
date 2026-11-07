using System;
using VoxelEngine;

namespace TractorBeam
{
    internal static class TractorUpgradeCatalog
    {
        internal const string EmitterKey = "modular-emitter";

        internal static readonly (string Key, string Name, BeamModules Module, Func<Brick> Template)[] Upgrades =
        {
            ("pull-module", "Tractor Pull Module", BeamModules.Pull, () => TabWeapons.ADDON_CHARGE),
            ("push-module", "Tractor Push Module", BeamModules.Push, () => TabWeapons.ADDON_SUPERCHARGE),
            ("slowdown-module", "Tractor Slowdown Module", BeamModules.Slowdown, () => TabWeapons.ADDON_OVERHEAT),
            ("shield-coupling-module", "Tractor Shield Coupling Module", BeamModules.ShieldCoupling, () => TabWeapons.ADDON_DAMAGE),
            ("capacity-module", "Tractor Capacity Module", BeamModules.Capacity, () => TabWeapons.ADDON_CHARGE)
        };

        internal static Brick EmitterTemplate() => TabWeapons.BEAM;
    }
}
