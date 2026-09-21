using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using Fika.Core.Networking;
using Fika.Core.Networking.Packets.Hideout;
using HarmonyLib;

namespace Fika.Core.Main.Components;

/// <summary>
/// 藏身处环境走 Fika 发包：挂在灯光/供电/区域/装饰的变更点上，按帧合并一次快照。
/// 客人只改场景视觉，不写自己的 HideoutLocalData / 档案。
/// </summary>
public static class HideoutWorldSync
{
    public static bool IsApplying { get; private set; }

    private static readonly ManualLogSource _logger = Logger.CreateLogSource("Fika.HideoutWorld");
    private static readonly FieldInfo LightingField =
        AccessTools.Field(typeof(HideoutController), "_currentLightingLevel");
    private static readonly EHideoutCustomizationType[] GlobalCustomizationTypes =
    [
        EHideoutCustomizationType.Floor,
        EHideoutCustomizationType.Wall,
        EHideoutCustomizationType.Ceiling,
        EHideoutCustomizationType.Light,
        EHideoutCustomizationType.ShootingRangeMark
    ];

    private static readonly List<Action> _unsubs = [];
    private static HideoutRepresentation _bound;
    private static HideoutController _controller;
    private static bool _dirty;
    private static string _lastSentFingerprint = "";
    private static string _lastAppliedFingerprint = "";

    public static void MarkDirty()
    {
        if (IsApplying || !FikaHideoutCoop.IsHosting)
        {
            return;
        }

        _dirty = true;
    }

    public static void Reset()
    {
        IsApplying = false;
        _dirty = false;
        _lastSentFingerprint = "";
        _lastAppliedFingerprint = "";
        _controller = null;
        Unsubscribe();
    }

    /// <summary>
    /// 挂上藏身处自己的变更事件，有脏标记才按帧发一次。不扫描场景。
    /// </summary>
    public static void Pump()
    {
        if (!FikaHideoutCoop.IsHosting)
        {
            return;
        }

        EnsureSubscribed();
        if (!_dirty || !Singleton<HideoutRepresentation>.Instantiated)
        {
            return;
        }

        SendToAll();
    }

    public static void SendToPeer(NetPeer peer)
    {
        if (peer == null || !Singleton<FikaServer>.Instantiated)
        {
            return;
        }

        var packet = Capture();
        if (packet == null)
        {
            _dirty = true;
            return;
        }

        _lastSentFingerprint = packet.Fingerprint();
        _dirty = false;
        Singleton<FikaServer>.Instance.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
        _logger.LogInfo($"Sent hideout world state to peer {peer.Id}");
    }

    private static void SendToAll()
    {
        if (!Singleton<FikaServer>.Instantiated
            || (Singleton<FikaServer>.Instance.NetServer?.ConnectedPeersCount ?? 0) <= 0)
        {
            return;
        }

        var packet = Capture();
        if (packet == null)
        {
            return;
        }

        var fingerprint = packet.Fingerprint();
        _dirty = false;
        if (fingerprint == _lastSentFingerprint)
        {
            return;
        }

        _lastSentFingerprint = fingerprint;
        Singleton<FikaServer>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
        _logger.LogInfo($"Broadcast hideout world state lighting={packet.LightingLevel} energy={packet.EnergyOn} areas={packet.Areas.Length}");
    }

    public static void Apply(HideoutWorldStatePacket packet)
    {
        if (packet == null || FikaHideoutCoop.IsHosting)
        {
            return;
        }

        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            return;
        }

        var fingerprint = packet.Fingerprint();
        if (fingerprint == _lastAppliedFingerprint)
        {
            return;
        }

        var representation = Singleton<HideoutRepresentation>.Instance;
        IsApplying = true;
        try
        {
            TryApply("areas", () => ApplyAreas(representation, packet));
            TryApply("energy", () => ApplyEnergy(representation, packet.EnergyOn));
            TryApply("lighting", () => ApplyLighting(packet.LightingLevel));
            TryApply("customization", () => ApplyCustomization(representation, packet));
            _lastAppliedFingerprint = fingerprint;
            _logger.LogInfo($"Applied hideout world state lighting={packet.LightingLevel} energy={packet.EnergyOn}");
        }
        finally
        {
            IsApplying = false;
        }
    }

    private static void TryApply(string step, Action apply)
    {
        try
        {
            apply();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Apply hideout world state failed at {step}: {ex}");
        }
    }

    private static HideoutWorldStatePacket Capture()
    {
        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            return null;
        }

        var representation = Singleton<HideoutRepresentation>.Instance;
        var packet = new HideoutWorldStatePacket
        {
            LightingLevel = CaptureLightingLevel(),
            EnergyOn = representation.EnergyController != null && representation.EnergyController.IsEnergyGenerationOn,
            Areas = CaptureAreas(representation)
        };

        var customization = representation.CustomizationController;
        if (customization != null)
        {
            for (var i = 0; i < GlobalCustomizationTypes.Length; i++)
            {
                var type = GlobalCustomizationTypes[i];
                var itemId = customization.GetSelectedCustomizationItemId(type);
                packet.SetCustomizationId(type, itemId.HasValue ? itemId.Value.ToString() : "");
            }
        }

        return packet;
    }

    private static HideoutAreaWorldState[] CaptureAreas(HideoutRepresentation representation)
    {
        var datas = representation.AreaDatas;
        if (datas == null || datas.Count == 0)
        {
            return [];
        }

        var areas = new HideoutAreaWorldState[datas.Count];
        for (var i = 0; i < datas.Count; i++)
        {
            var data = datas[i];
            areas[i] = new HideoutAreaWorldState
            {
                Type = data.Template.Type,
                Level = data.CurrentLevel,
                Status = data.Status,
                IsActive = data.IsActive,
                LightStatus = data.LightStatus
            };
        }

        return areas;
    }

    public static void BindController(HideoutController controller)
    {
        if (controller != null)
        {
            _controller = controller;
        }
    }

    private static ELightingLevel CaptureLightingLevel()
    {
        if (_controller != null && LightingField != null)
        {
            return (ELightingLevel)LightingField.GetValue(_controller);
        }

        return HideoutLocalData.GlobalLightingLevel;
    }

    private static void ApplyAreas(HideoutRepresentation representation, HideoutWorldStatePacket packet)
    {
        if (packet.Areas == null || representation.AreaDatas == null)
        {
            return;
        }

        var byType = new Dictionary<EAreaType, HideoutAreaWorldState>(packet.Areas.Length);
        for (var i = 0; i < packet.Areas.Length; i++)
        {
            byType[packet.Areas[i].Type] = packet.Areas[i];
        }

        foreach (var data in representation.AreaDatas)
        {
            if (data?.Template == null)
            {
                continue;
            }

            try
            {
                if (!byType.TryGetValue(data.Template.Type, out var snap))
                {
                    continue;
                }

                var busy = data.CurrentStage != null && (data.CurrentStage.ActionGoing || data.CurrentStage.Waiting);
                if (!busy && data.CurrentLevel != snap.Level)
                {
                    data.SetCurrentLevelDumb(snap.Level, silent: true);
                }

                if (!busy && data.Status != snap.Status)
                {
                    data.Status = snap.Status;
                }

                if (data.IsActive != snap.IsActive)
                {
                    data.IsActive = snap.IsActive;
                }

                if (data.LightStatus != snap.LightStatus)
                {
                    data.LightStatus = snap.LightStatus;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Apply hideout area {data.Template.Type} failed: {ex.Message}");
            }
        }
    }

    private static void ApplyEnergy(HideoutRepresentation representation, bool energyOn)
    {
        var energy = representation.EnergyController;
        if (energy == null)
        {
            return;
        }

        if (energyOn)
        {
            energy.SetInfinityGeneration(true);
            energy.SetSwitchedStatus(true);
            return;
        }

        var generators = Traverse.Create(energy).Field("_generators").GetValue<List<IGenerator>>();
        Traverse.Create(energy).Field("_switchedOn").SetValue(false);
        energy.SetInfinityGeneration(false);
        if (generators != null)
        {
            energy.SetSwitchedStatus(false);
        }
    }

    private static void EnsureSubscribed()
    {
        if (!Singleton<HideoutRepresentation>.Instantiated)
        {
            return;
        }

        var representation = Singleton<HideoutRepresentation>.Instance;
        if (_bound == representation)
        {
            return;
        }

        Unsubscribe();
        _bound = representation;
        var energy = representation.EnergyController;
        if (energy != null)
        {
            Action<bool> onEnergy = OnEnergyChanged;
            energy.OnEnergyGenerationChanged += onEnergy;
            _unsubs.Add(() => energy.OnEnergyGenerationChanged -= onEnergy);
        }

        var areas = representation.AreaDatas;
        if (areas == null)
        {
            return;
        }

        foreach (var data in areas)
        {
            if (data == null)
            {
                continue;
            }

            _unsubs.Add(data.StatusUpdated.Subscribe(MarkDirty));
            _unsubs.Add(data.LevelUpdated.Subscribe(OnLevelChanged));
            _unsubs.Add(data.LightStatusChanged.Subscribe(OnLightStatusChanged));
        }
    }

    private static void Unsubscribe()
    {
        for (var i = 0; i < _unsubs.Count; i++)
        {
            try
            {
                _unsubs[i]?.Invoke();
            }
            catch
            {
                // ignored
            }
        }

        _unsubs.Clear();
        _bound = null;
    }

    private static void OnEnergyChanged(bool _)
    {
        MarkDirty();
    }

    private static void OnLevelChanged(bool _)
    {
        MarkDirty();
    }

    private static void OnLightStatusChanged(ELightStatus _)
    {
        MarkDirty();
    }

    private static void ApplyLighting(ELightingLevel level)
    {
        var controller = _controller;
        if (controller == null)
        {
            return;
        }

        var traverse = Traverse.Create(controller);
        traverse.Field("_currentLightingLevel").SetValue(level);
        traverse.Field("_lightInitialized").SetValue(true);

        var camera = traverse.Field("_hideoutCameraController").GetValue<HideoutCameraController>();
        var ambiance = traverse.Field("_ambianceController").GetValue<AmbianceController>();
        if (camera != null && ambiance != null)
        {
            controller.UpdateCameraFlashlight();
        }

        if (controller.Areas != null)
        {
            foreach (var pair in controller.Areas)
            {
                pair.Value?.SetLightingLevel(level);
            }
        }

        var overlay = traverse.Field("_hideoutScreenOverlay").GetValue<HideoutScreenOverlay>();
        overlay?.SetCurrentLightingLevel(level);

        var globalLighting = traverse.Field("_globalLighting").GetValue<Dictionary<ELightingLevel, GameObject>>();
        if (globalLighting == null)
        {
            return;
        }

        foreach (var pair in globalLighting)
        {
            if (pair.Value != null && pair.Key != level)
            {
                pair.Value.SetActive(false);
            }
        }

        foreach (var pair in globalLighting)
        {
            if (pair.Value != null && pair.Key == level)
            {
                pair.Value.SetActive(true);
            }
        }
    }

    private static void ApplyCustomization(HideoutRepresentation representation, HideoutWorldStatePacket packet)
    {
        var customization = representation.CustomizationController;
        if (customization == null)
        {
            return;
        }

        if (!Singleton<CustomizationSolver>.Instantiated
            || Singleton<CustomizationSolver>.Instance?.HideoutCustomizationItems == null)
        {
            return;
        }

        for (var i = 0; i < GlobalCustomizationTypes.Length; i++)
        {
            var type = GlobalCustomizationTypes[i];
            var itemId = packet.CustomizationId(type);
            if (string.IsNullOrEmpty(itemId) || itemId.Length != 24)
            {
                continue;
            }

            try
            {
                var selected = customization.GetSelectedCustomizationItemId(type);
                if (selected.HasValue && selected.Value.ToString() == itemId)
                {
                    continue;
                }

                customization.InstallCustomization(new MongoID(itemId), type);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Apply hideout customization {type} failed: {ex.Message}");
            }
        }
    }
}
