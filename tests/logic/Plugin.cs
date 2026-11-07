// Adapted from the SEVO Mod Platform functional-block sample for the real Tractor Beam registration.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using BepInEx;
using Game.Player;
using Manager;
using MessagePack;
using Mirror;
using Sevo.Framework.Runtime;
using Sevo.Framework.Sdk;
using UI;
using UI.Codex;
using UnityEngine;
using UnityEngine.UI;
using VoxelEngine;

namespace TractorBeam.LogicProbe;

[BepInPlugin(Owner, "Keel Functional Block Lab Probe", "0.1.0")]
[BepInDependency("local.sevo.tractorbeam")]
public sealed class Plugin : BaseUnityPlugin
{
    private const string Owner = "local.sevo.tractorbeam.logicprobe";
    private const string Key = "local.sevo.tractorbeam:beam-emitter";
    private const string LabCompany = "SEVO Tractor SandboxXX";
    private const string LabProduct = "SEVO TB Test";
    private readonly Stopwatch clock = new();
    private ProfileSession? pendingProfile;
    private string session = "";
    private string stage = "opt-in";
    private string? lastTitleState;

    private void Awake()
    {
        if (Environment.GetEnvironmentVariable("SEVO_TRACTOR_LOGIC_PROBE") != "1") return;
        try
        {
            var statePath = Environment.GetEnvironmentVariable("SEVO_PLATFORM_STATE_DIR");
            session = Environment.GetEnvironmentVariable("SEVO_PLATFORM_SESSION") ?? "";
            Require(Environment.GetEnvironmentVariable("SEVO_PLATFORM_EXPERIMENTAL_CONTENT") == "1",
                "Managed experimental content must be enabled.");
            Require(Application.companyName == LabCompany && Application.productName == LabProduct,
                "The probe requires the isolated SEVO Platform Unity identity.");
            Require(Guid.TryParseExact(session, "N", out _) && statePath != null,
                "A managed launcher session and state directory are required.");
            var profile = ProfileProtocol.Open(statePath!, Paths.GameRootPath, session);
            clock.Start();
            pendingProfile = profile;
            Step("waiting-for-title");
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }


    private IEnumerator Guard(IEnumerator steps)
    {
        try
        {
            while (true)
            {
                object? current;
                try
                {
                    if (!steps.MoveNext()) break;
                    current = steps.Current;
                }
                catch (Exception exception)
                {
                    ReportFailure(exception);
                    break;
                }

                yield return current;
            }
        }
        finally
        {
            (steps as IDisposable)?.Dispose();
        }
    }

    private void Update()
    {
        var profile=pendingProfile;
        if(profile==null) return;
        try
        {
            // BepInEx can run Awake before Unity can start coroutines or load stock prefabs.
            if(WaitingFor(()=>ReadyAtTitle(profile))) return;
            pendingProfile=null;
            StartCoroutine(Guard(Run(profile)));
        }
        catch(Exception exception)
        {
            pendingProfile=null;
            ReportFailure(exception);
        }
    }

    private IEnumerator Run(ProfileSession profile)
    {
        var registryJson = profile.ReadRegistry() ?? throw new InvalidOperationException("Managed registry is missing.");
        using var registryStream = new MemoryStream(Encoding.UTF8.GetBytes(registryJson));
        var registry = (RegistryState)new DataContractJsonSerializer(typeof(RegistryState)).ReadObject(registryStream);
        if (Environment.GetEnvironmentVariable("SEVO_TRACTOR_MESH_ONLY") == "1")
        {
            CheckModularMeshes(registry);
            Logger.LogInfo($"TRACTOR_LOGIC_PROBE PASS session={session} emitter, repeat fitting and five module meshes");
            yield break;
        }
        var sinkDefinition=ReadProbeDefinition(registry,Key);
        if (Environment.GetEnvironmentVariable("SEVO_TRACTOR_VISUAL_ONLY") == "1")
        {
            CheckVisual();
            Logger.LogInfo($"TRACTOR_LOGIC_PROBE PASS session={session} title visual component check");
            yield break;
        }
        var switchDefinition = TabLogic.SWITCH_BUTTON;
        Step("caller-selected-template-geometry");
        CheckTemplate(sinkDefinition,TabWeapons.REPAIR_BEAM);

        Step("starting-solo");
        UI_TitleScreen.instance.ShowPlayScreen();
        yield return null;
        var selection=UnityEngine.Object.FindAnyObjectByType<UI_CharacterSelection>();
        Require(selection!=null && UI_CharacterSelection.IsActive.Value,"The stock character selection did not open.");
        selection!.ShowCharacterCreationWindow();
        yield return null;
        var creation=UnityEngine.Object.FindAnyObjectByType<UI_CharacterCreation>();
        Require(creation!=null && UI_CharacterCreation.IsActive.Value,"The stock character creation did not open.");
        var nameField=ReadCreationField<InputField>(creation!,"input");
        var seedField=ReadCreationField<InputField>(creation!,"inputSeed");
        nameField.text="TractorProbe-"+session[..8];
        seedField.text="11062645";
        creation!.OnInput();
        creation.OnSeedUpdated();
        if(!ReadCreationField<bool>(creation,"isCreative")) creation.OnGameModeButton();
        Require(ReadCreationField<bool>(creation,"isCreative") && !ReadCreationField<bool>(creation,"isInvalid"),
            "Stock character creation rejected the disposable creative fixture.");
        NetworkServer.dontListen = true;
        creation.PlayWithNewCharacter();
        string? lastWorldState=null;
        while (true)
        {
            var local=Dependency.PlayerManager;
            bool hasLocal=local!=null && local.IsLocal;
            var worldState=$"mode={PlayerState.sessionMode} loading={PlayerState.InLoadingScreen} host={NetworkServer.active} client={NetworkClient.active} ready={NetworkClient.ready} localIdentity={NetworkClient.localPlayer!=null} localManager={hasLocal}";
            if(worldState!=lastWorldState)
            {
                lastWorldState=worldState;
                Logger.LogInfo($"TRACTOR_LOGIC_PROBE world {worldState} session={session}");
            }
            if(!WaitingFor(()=>PlayerState.sessionMode==PlayerState.SessionMode.solo
                && !PlayerState.InLoadingScreen && NetworkServer.active && hasLocal)) break;
            yield return null;
        }

        var player = Dependency.PlayerManager;
        var position = new WorldTransform(player.TransformData);
        position.systemPosition += new Vector3l(new Vector3(0f, 25f, 0f));
        var switchData = switchDefinition.PreviewBrickData;
        var sinkData = sinkDefinition.PreviewBrickData;
        sinkData.gridPosition = new Vector3i(32, 0, 0);

        Step("placing-switch");
        var grid = player.NetworkSetup.ServerCreateEntity(switchData, position, EntityType.spaceship);
        if (grid == null) throw new InvalidOperationException("Stock ServerCreateEntity returned no grid.");
        while (WaitingFor(() => grid.IsInitialized && FindCommand(grid, switchData) is CommandSwitch)) yield return null;
        var source = (CommandSwitch)FindCommand(grid, switchData)!;
        Require(source.State == 0f, "The newly placed Switch started high.");

        Step("placing-functional-block");
        PlaceOrDelete(grid, sinkData, VoxelActionType.build);
        while (WaitingFor(() => FindCommand(grid, sinkData) != null)) yield return null;
        var sink = FindCommand(grid, sinkData)!;
        Require(ActiveCount() == 0, "Tractor started active before input.");
        var yokeData = TabLogic.YOKE.PreviewBrickData;
        yokeData.gridPosition = new Vector3i(64, 0, 0);
        var gateData = TabLogic.EVENT_GATE.PreviewBrickData;
        gateData.gridPosition = new Vector3i(96, 0, 0);
        Step("placing-native-event-gate");
        PlaceOrDelete(grid, yokeData, VoxelActionType.build);
        PlaceOrDelete(grid, gateData, VoxelActionType.build);
        while (WaitingFor(() => FindCommand(grid, yokeData) is CommandYoke
            && FindCommand(grid, gateData) is CommandEventGate && !GameloopManager.jobStarted)) yield return null;
        var yoke = (CommandYoke)FindCommand(grid, yokeData)!;
        var gate = (CommandEventGate)FindCommand(grid, gateData)!;
        Require(CommandCommon.CanLinkToCommand(yoke, gate), "The stock Yoke cannot link to the stock Event Gate.");
        CommandCommon.LinkToCommand(yoke, gate, false, false);
        ConfigureGate(gate, CommandEventGate.SignalType.toggle);
        Require(CommandCommon.CanLinkToCommand(gate, sink), "The held Event Gate cannot link to the repair preset.");
        CommandCommon.LinkToCommand(gate, sink, false, false);
        Require(ReferenceEquals(gate.predecessors.Command, yoke) && ReferenceEquals(sink.predecessors.Command, gate),
            "The Yoke → Event Gate → repair preset link is incomplete.");

        Step("event-gate-held-high");
        yoke.isFirePrimary.Set(true, player);
        while (WaitingFor(() => gate.State == 1f && ActiveCount() == 1 && !GameloopManager.jobStarted)) yield return null;
        Step("visible-feedback-without-target");
        while (WaitingFor(() => FeedbackLine()?.enabled == true)) yield return null;
        var visual = FeedbackLine()!;
        Require(Vector3.Distance(visual.GetPosition(0), visual.GetPosition(1)) > 0.1f,
            "The held input did not create a visible-length muzzle pulse.");
        Step("event-gate-held-low");
        yoke.isFirePrimary.Set(false, player);
        while (WaitingFor(() => gate.State == 0f && ActiveCount() == 0 && !GameloopManager.jobStarted)) yield return null;
        Require(!visual.enabled, "Releasing the input left the beam effect visible.");
        Step("event-gate-disconnect");
        yoke.isFirePrimary.Set(true, player);
        while (WaitingFor(() => gate.State == 1f && ActiveCount() == 1 && !GameloopManager.jobStarted)) yield return null;
        CommandCommon.RemoveTarget(yoke, gate, false);
        while (WaitingFor(() => ActiveCount() == 0)) yield return null;
        Logger.LogInfo($"TRACTOR_LOGIC_PROBE PASS session={session} id={sinkDefinition.brickID}");
    }

    private static int ActiveCount()
    {
        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["local.sevo.tractorbeam"].Instance;
        var list = plugin.GetType().GetField("active", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(plugin) as IList;
        return list?.Count ?? throw new InvalidOperationException("Tractor active state unavailable.");
    }

    private static LineRenderer? FeedbackLine() =>
        GameObject.Find("Tractor Beam Feedback")?.GetComponent<LineRenderer>();

    private static void CheckVisual()
    {
        var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos["local.sevo.tractorbeam"].Instance;
        var type = plugin.GetType();
        var assembly = type.Assembly;
        var emitterType = type.GetNestedType("Emitter", BindingFlags.NonPublic)!;
        var frameType = assembly.GetType("TractorBeam.BeamFrame", true)!;
        var stateType = assembly.GetType("TractorBeam.BeamState", true)!;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var emitter = Activator.CreateInstance(emitterType, flags, null, new object?[] { plugin, null }, null)!;
        var feedback = emitterType.GetField("Feedback", flags)!.GetValue(emitter)!;
        try
        {
            foreach (var state in new[] { "Searching", "Locked", "Unpowered" })
            {
                var frame = Activator.CreateInstance(frameType, flags, null,
                    new object[] { Vector3.zero, Vector3.forward * 20f, Vector3.forward, 1f, Enum.Parse(stateType, state) }, null)!;
                type.GetMethod("ShowFeedback", flags)!.Invoke(plugin, new[] { emitter, frame });
                var line = FeedbackLine();
                Require(line != null && line.enabled && line.sharedMaterial.shader.isSupported,
                    "The game could not create the beam renderer for " + state);
                var color = line!.sharedMaterial.color;
                Require(state == "Locked" ? color.b > color.r : color.r > color.b, "Wrong beam color for " + state);
                Require(state == "Unpowered" ? line.GetPosition(1).z < 4f : line.GetPosition(1).z == 20f,
                    "Wrong beam length for " + state);
            }
            var off = Activator.CreateInstance(frameType);
            type.GetMethod("ShowFeedback", flags)!.Invoke(plugin, new[] { emitter, off });
            Require(FeedbackLine()?.enabled == false, "Beam renderer remained enabled when off.");
        }
        finally
        {
            feedback.GetType().GetMethod("Dispose", flags)!.Invoke(feedback, null);
        }
    }

    private static T ReadCreationField<T>(UI_CharacterCreation creation,string name) =>
        typeof(UI_CharacterCreation).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(creation) is T value
            ? value : throw new InvalidOperationException("Missing stock character creation field: "+name);

    private static Brick ReadProbeDefinition(RegistryState registry,string key)
    {
        var mapping=registry.Mappings.Single(entry=>entry.Key==key && !entry.Tombstone);
        Require(mapping.Id>0 && mapping.Id<Brick.BRICK_INDEX.Length,"Probe content ID is outside the native table.");
        var definition=Brick.BRICK_INDEX[mapping.Id];
        Require(definition!=null && definition.IsCommand,"The registered probe is missing from the native command-brick table.");
        return definition!;
    }

    private static void CheckModularMeshes(RegistryState registry)
    {
        var emitter = ReadProbeDefinition(registry, "local.sevo.tractorbeam:modular-emitter");
        var repeatMapping = registry.Mappings.Single(entry => entry.Key == "local.sevo.tractorbeam:modular-emitter-repeat" && !entry.Tombstone);
        var repeat = Brick.BRICK_INDEX[repeatMapping.Id];
        Require(emitter.repeatBrickId == repeat.brickID && repeat.brickID != TabWeapons.BEAM.repeatBrickId,
            "The modular emitter did not bind its own repeat fitting.");
        CheckMesh(emitter, TabWeapons.BEAM, 264);
        CheckMesh(repeat, TabWeapons.WEAPON_ADDON_LARGE, 312);
        CheckPaint(emitter, new Color32(163, 153, 140, 255));
        CheckPaint(repeat, new Color32(163, 153, 140, 255));
        CheckWeaponsListing(emitter, 1);
        CheckWeaponsListing(repeat, 0);
        foreach (var (asset, triangles, stock) in new[]
        {
            ("pull", 628, TabWeapons.ADDON_CHARGE),
            ("push", 248, TabWeapons.ADDON_SUPERCHARGE),
            ("slowdown", 424, TabWeapons.ADDON_OVERHEAT),
            ("shield-coupling", 340, TabWeapons.ADDON_DAMAGE),
            ("capacity", 576, TabWeapons.ADDON_CHARGE)
        })
        {
            string key = "local.sevo.tractorbeam:" + asset + "-module";
            var mapping = registry.Mappings.Single(entry => entry.Key == key && !entry.Tombstone);
            var definition = Brick.BRICK_INDEX[mapping.Id];
            CheckMesh(definition, stock, triangles);
            CheckPaint(definition, new Color32(163, 153, 140, 255));
            CheckWeaponsListing(definition, 1);
        }
    }

    private static void CheckWeaponsListing(Brick definition, int expected)
    {
        Require(CodexTab.WEAPONS.items.OfType<Brick>().Count(brick => brick.brickID == definition.brickID) == expected
            && !CodexTab.COMMANDS.items.OfType<Brick>().Any(brick => brick.brickID == definition.brickID),
            "Unexpected Weapons or Commands entry: " + definition.name);
    }

    private static void CheckPaint(Brick definition, Color32 expected)
    {
        Require(definition.defaultPaint.Equals(expected) && definition.PreviewPaint.Equals(expected),
            "Native placement or preview paint differs from the authored default: " + definition.name);
    }

    private static void CheckMesh(Brick definition, Brick stock, int triangles)
    {
        var render = definition.VoxelMesh?.Mesh;
        var preview = definition.VoxelMesh?.MeshPreview;
        Require(render != null && preview != null && render != preview &&
            render.name.StartsWith("Keel attachment visual ", StringComparison.Ordinal) &&
            render.GetIndexCount(0) == triangles * 3 && preview.GetIndexCount(0) == triangles * 3,
            "Native render or preview fell back to stock geometry: " + definition.name);
        Require(definition.VoxelCollider.bounds.Equals(stock.VoxelCollider.bounds),
            "Collider bounds changed: " + definition.name);
    }

    private static void CheckTemplate(Brick definition,Brick stock)
    {
        Require(definition.brickID != stock.brickID && definition.nameMesh == stock.nameMesh
            && definition.IsFacingForward == stock.IsFacingForward
            && definition.isDynamicGridSize == stock.isDynamicGridSize
            && definition.boundsOverride.Equals(stock.boundsOverride)
            && definition.minGridSize == stock.minGridSize && definition.maxGridSize == stock.maxGridSize,
            "The template does not match the stock mesh, facing, and size contract.");
        Require(definition.VoxelMesh?.Mesh != null && stock.VoxelMesh?.Mesh != null
            && definition.VoxelMesh.Mesh.vertexCount == stock.VoxelMesh.Mesh.vertexCount
            && definition.VoxelMesh.ResizeMode.Equals(stock.VoxelMesh.ResizeMode)
            && definition.VoxelCollider?.subShapes?.Length == 1 && definition.colliderShape == stock.colliderShape
            && ReferenceEquals(definition.VoxelMaterials, stock.VoxelMaterials),
            "The template did not load the stock geometry, collider, and baked materials.");
    }

    private static void ConfigureGate(CommandEventGate gate,CommandEventGate.SignalType mode,bool inverted=false)
    {
        var eventField = typeof(CommandYoke).GetField(nameof(CommandYoke.isFirePrimary));
        var marker = eventField?.GetCustomAttribute<CommandStateData.CommandStateDataField>()
            ?? throw new InvalidOperationException("The stock Yoke no longer declares its primary fire signal.");
        var config = MessagePackSerializer.Deserialize<CommandEventGate.EventGateDataSave>(gate.GetCustomData(false));
        config.eventSelected = marker.eventId;
        config.signalType = (int)mode;
        config.fireGroupMode = 0;
        config.isInverted = inverted;
        config.pulseLength = 0.75f;
        gate.OnSaveLoaded(MessagePackSerializer.Serialize(config), false);
        var applied = MessagePackSerializer.Deserialize<CommandEventGate.EventGateDataSave>(gate.GetCustomData(false));
        Require(applied.eventSelected == marker.eventId && applied.signalType == (int)mode && applied.isInverted==inverted,
            "The Event Gate did not retain the requested native configuration.");
    }

    private static Command? FindCommand(ParentEntity grid, BrickInstanceData expected)
    {
        int instanceId = grid.Bricks.GetBrickInstanceId(expected);
        var found = grid.Bricks.GetBrickData(instanceId);
        if (found.brickId != expected.brickId || !found.gridPosition.Equals(expected.gridPosition)) return null;
        return new BrickCoord(grid.ID, found).Command as Command;
    }

    private static void PlaceOrDelete(ParentEntity grid, BrickInstanceData data, VoxelActionType type)
    {
        var action = new VoxelActionData { entityID = grid.ID, brickData = data, type = type };
        Dependency.PlayerManager.NetworkedBuilder.LocalVoxelAction(action, false, 0);
    }

    private bool ReadyAtTitle(ProfileSession profile)
    {
        string path = Path.Combine(profile.DirectoryPath, "runtime-status.json");
        if (!File.Exists(path)) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var status = (RuntimeSnapshot)new DataContractJsonSerializer(typeof(RuntimeSnapshot)).ReadObject(stream);
        using var process = Process.GetCurrentProcess();
        if (status.Session != session || status.ProcessId != process.Id) return false;
        Require(status.SchemaVersion == 1 && status.ProfileId == profile.ProfileId.ToString("D"),
            "The runtime status did not identify this managed profile.");
        Require(!status.Failed, status.Reason);
        if(!status.ResourcesReady || !status.RegistrationAdmitted) return false;
        bool managerReady=NetworkManager.singleton!=null;
        bool variantsReady=SkinDataCommon.RaceVariants is { Length: > 0 };
        var titleState=$"mode={PlayerState.sessionMode} loading={PlayerState.InLoadingScreen} manager={managerReady} server={NetworkServer.active} variants={variantsReady}";
        if(titleState!=lastTitleState)
        {
            lastTitleState=titleState;
            Logger.LogInfo($"TRACTOR_LOGIC_PROBE title {titleState} session={session}");
        }
        return PlayerState.sessionMode==PlayerState.SessionMode.mainMenu && managerReady && !NetworkServer.active
            && variantsReady && UI_TitleScreen.instance!=null && UI_TitleScreen.IsActive.Value;
    }

    private bool WaitingFor(Func<bool> ready)
    {
        if (clock.Elapsed > TimeSpan.FromMinutes(4)) throw new TimeoutException($"Timed out at {stage}.");
        return !ready();
    }

    private void Step(string value)
    {
        stage = value;
        Logger.LogInfo($"TRACTOR_LOGIC_PROBE stage={value} session={session}");
    }

    private void ReportFailure(Exception exception) =>
        Logger.LogError($"TRACTOR_LOGIC_PROBE FAIL stage={stage} session={session} {exception}");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

}
