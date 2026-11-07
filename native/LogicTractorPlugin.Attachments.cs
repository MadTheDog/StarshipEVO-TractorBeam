#if KEEL_WEAPON_ATTACHMENTS
using System;
using System.Collections.Generic;
using System.Linq;
using Sevo.Framework.Runtime;
using UnityEngine;
using VoxelEngine;

namespace TractorBeam
{
    public sealed partial class LogicTractorPlugin
    {
        private static readonly Dictionary<string, BeamModules> moduleKeys = TractorUpgradeCatalog.Upgrades
            .ToDictionary(upgrade => Owner + ":" + upgrade.Key, upgrade => upgrade.Module, StringComparer.Ordinal);
        private readonly List<Mesh> submittedMeshes = new List<Mesh>();
        private const string WeaponsCategory = "WEAPONS";

        partial void RegisterModularContent()
        {
            foreach (var upgrade in TractorUpgradeCatalog.Upgrades)
            {
                string asset = upgrade.Key.Substring(0, upgrade.Key.Length - "-module".Length);
                ContentRegistration.RegisterWeaponAttachment(Owner, upgrade.Key, 1, upgrade.Name, upgrade.Template,
                    codexCategory: WeaponsCategory,
                    selectVisual: () => TractorUpgradeVisual.Load(asset, upgrade.Template(), submittedMeshes));
            }

            ContentRegistration.RegisterWeaponAttachmentReceiver(Owner, TractorUpgradeCatalog.EmitterKey, 1,
                "Modular Tractor Beam", TractorUpgradeCatalog.EmitterTemplate, moduleKeys.Keys,
                context => new Emitter(this, context, () => ReadModules(context)),
                codexCategory: WeaponsCategory,
                selectVisual: () => TractorUpgradeVisual.Load("recessed-optic-host", TractorUpgradeCatalog.EmitterTemplate(), submittedMeshes),
                repeatLocalName: "modular-emitter-repeat",
                selectRepeatVisual: () => TractorUpgradeVisual.Load("recessed-optic-section", TabWeapons.WEAPON_ADDON_LARGE, submittedMeshes),
                modularSections: true);
        }

        private void OnDestroy()
        {
            foreach (var mesh in submittedMeshes) UnityEngine.Object.Destroy(mesh);
            submittedMeshes.Clear();
        }

        private static BeamModules ReadModules(FunctionalBlockContext context)
        {
            BeamModules modules = BeamModules.None;
            var slots = context.GetWeaponAttachmentSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                var key = slots[i].AttachmentKey;
                if (key != null && moduleKeys.TryGetValue(key, out var module)) modules |= module;
            }
            return modules;
        }
    }
}
#endif
