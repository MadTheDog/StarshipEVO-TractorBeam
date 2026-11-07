using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using Sevo.Framework.Runtime;
using Sevo.Framework.Sdk;
using UnityEngine;
using VoxelEngine;

namespace TractorBeam
{
    #if KEEL_WEAPON_ATTACHMENTS
    [BepInPlugin(Owner, "Tractor Beam", "0.3.6")]
    [BepInDependency(Sevo.Framework.Runtime.Plugin.Id, "0.1.5")]
    #else
    [BepInPlugin(Owner, "Tractor Beam", "0.2.2")]
    [BepInDependency(Sevo.Framework.Runtime.Plugin.Id, "0.1.2")]
    #endif
    public sealed partial class LogicTractorPlugin : BaseUnityPlugin
    {
        private const string Owner = "local.sevo.tractorbeam";
        private readonly List<Emitter> active = new List<Emitter>();
        private readonly HashSet<ParentEntity> affected = new HashSet<ParentEntity>();
        private readonly HashSet<ParentEntity> usedSources = new HashSet<ParentEntity>();
        private ConfigEntry<bool> retrieve;
        private bool available;
        private Shader feedbackShader;
        private bool feedbackChecked;

        private void Awake()
        {
            try
            {
                TractorActuator.ValidateContracts();
                retrieve = Config.Bind("Behaviour", "Retrieve", true,
                    "When enabled by logic, brake and tow. Set false to brake without towing.");
#if !KEEL_WEAPON_ATTACHMENTS
                RegisterEmitter();
#endif
                RegisterModularContent();
                available = true;
                Logger.LogInfo("Declared the logic-controlled Tractor Beam. Start through a managed Keel profile with experimental content enabled.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Tractor Beam disabled: " + ex);
            }
        }

#if !KEEL_WEAPON_ATTACHMENTS
        private void RegisterEmitter() =>
            ContentRegistration.RegisterFunctionalBlock(Owner, "beam-emitter", 1, "Tractor Beam",
                () => TabWeapons.REPAIR_BEAM,
                context => new Emitter(this, context));
#endif

        partial void RegisterModularContent();

        private void Update()
        {
            if (!available || active.Count == 0) return;
            affected.Clear();
            usedSources.Clear();
            foreach (var emitter in active)
            {
                if (!emitter.Context.TryGetPlacement(out var placement) || !placement.IsValid)
                {
                    emitter.Actuator.Release();
                    emitter.Feedback.Hide();
                    continue;
                }
                var source = placement.Parent;
                if (source == null || usedSources.Contains(source))
                {
                    emitter.Actuator.Release();
                    emitter.Feedback.Hide();
                    continue;
                }
                try
                {
                    if (emitter.Actuator.Tick(placement, retrieve.Value, affected, out var frame, emitter.Modules?.Invoke()))
                        usedSources.Add(source);
                    ShowFeedback(emitter, frame);
                    emitter.ReportStatus();
                }
                catch (Exception ex)
                {
                    emitter.Actuator.Release();
                    available = false;
                    foreach (var other in active) other.Feedback.Hide();
                    Logger.LogError("Tractor Beam force disabled for this session: " + ex);
                    break;
                }
            }
        }

        private void ShowFeedback(Emitter emitter, BeamFrame frame)
        {
            try
            {
                if (!feedbackChecked && frame.State != BeamState.Off)
                {
                    feedbackChecked = true;
                    feedbackShader = Shader.Find("Sprites/Default");
                    if (feedbackShader == null || !feedbackShader.isSupported)
                        feedbackShader = Shader.Find("Unlit/Color");
                    if (feedbackShader == null || !feedbackShader.isSupported)
                    {
                        feedbackShader = null;
                        Logger.LogWarning("Tractor Beam visuals unavailable: no supported unlit shader.");
                    }
                }
                if (feedbackShader != null) emitter.Feedback.Show(frame, feedbackShader);
            }
            catch (Exception ex)
            {
                feedbackShader = null;
                foreach (var other in active) other.Feedback.Hide();
                Logger.LogWarning("Tractor Beam visuals disabled: " + ex);
            }
        }

        private sealed class Emitter : FunctionalBlockBehavior
        {
            private readonly LogicTractorPlugin owner;
            internal readonly FunctionalBlockContext Context;
            internal readonly TractorActuator Actuator = new TractorActuator();
            internal readonly BeamFeedback Feedback = new BeamFeedback();
            internal Func<BeamModules> Modules { get; }
            private string lastStatus;
            private float nextStatusTime;

            internal Emitter(LogicTractorPlugin owner, FunctionalBlockContext context)
                : this(owner, context, null)
            {
            }

            internal Emitter(LogicTractorPlugin owner, FunctionalBlockContext context, Func<BeamModules> modules)
            {
                this.owner = owner;
                Context = context;
                Modules = modules;
            }

            internal void ReportStatus()
            {
                string status = Actuator.Status;
                if (status == lastStatus || Time.unscaledTime < nextStatusTime) return;
                lastStatus = status;
                nextStatusTime = Time.unscaledTime + 0.5f;
                owner.Logger.LogInfo(status);
            }

            public override void OnSustainedInput(bool enabled)
            {
                if (enabled)
                {
                    if (!owner.active.Contains(this))
                    {
                        owner.active.Add(this);
                        owner.Logger.LogDebug("Tractor Beam received a held logic input.");
                    }
                }
                else
                {
                    owner.active.Remove(this);
                    lastStatus = null;
                    nextStatusTime = 0;
                    Actuator.Release();
                    Feedback.Hide();
                }
            }

            public override void OnDespawn()
            {
                OnSustainedInput(false);
                Feedback.Dispose();
            }
        }
    }
}
