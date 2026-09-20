using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using Fika.Core.Networking;
using Fika.Core.Networking.Packets.Hideout;
using HarmonyLib;

namespace Fika.Core.Main.Components;

/// <summary>
/// 主人采集藏身处世界环境并广播；客人只改场景视觉，不写自己的 HideoutLocalData / 档案。
/// </summary>
public static class HideoutWorldSync
{
    public static bool IsApplying { get; private set; }

    private static readonly ManualLogSource _logger = Logger.CreateLogSource("Fika.HideoutWorld");
    private static readonly EHideoutCustomizationType[] GlobalCustomizationTypes =
    [
        EHideoutCustomizationType.Floor,
        EHideoutCustomizationType.Wall,
        EHideoutCustomizationType.Ceiling,
        EHideoutCustomizationType.Light,
        EHideoutCustomizationType.ShootingRangeMark
    ];

    private const float PollSeconds = 0.5f;
    private const float HeartbeatSeconds = 2f;

    private static bool _dirty;
    private static float _nextPoll = -999f;
    private static float _nextHeartbeat = -999f;
    private static string _lastSentFingerprint = "";
    private static string _lastAppliedFingerprint = "";

    public static void MarkDirty()
    {
        if (IsApplying || !FikaHideoutCoop.IsHosting)
        {
            return;
        }

        _dirty = true;
        if (Singleton<FikaServer>.Instantiated
            && (Singleton<FikaServer>.Instance.NetServer?.ConnectedPeersCount ?? 0) > 0)
        {
            SendToAll();
        }
    }

    public static void Reset()
    {
        IsApplying = false;
        _dirty = false;
        _nextPoll = -999f;
        _nextHeartbeat = -999f;
        _lastSentFingerprint = "";
        _lastAppliedFingerprint = "";
    }

    public static void Tick()
    {
        if (!FikaHideoutCoop.IsHosting || !Singleton<FikaServer>.Instantiated)
        {
            return;
        }

        var peers = Singleton<FikaServer>.Instance.NetServer?.ConnectedPeersCount ?? 0;
        if (peers <= 0)
        {
            return;
        }

        if (_dirty || Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + PollSeconds;
            SendToAll();
        }
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

        Singleton<FikaServer>.Instance.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, peer);
        _logger.LogInfo($"Sent hideout world state to peer {peer.Id}");
    }

    private static void SendToAll()
    {
        var packet = Capture();
        if (packet == null)
        {
            return;
        }

        var fingerprint = packet.Fingerprint();
        var changed = fingerprint != _lastSentFingerprint;
        if (!changed && !_dirty && Time.unscaledTime < _nextHeartbeat)
        {
            return;
        }

        _lastSentFingerprint = fingerprint;
        _dirty = false;
        _nextHeartbeat = Time.unscaledTime + HeartbeatSeconds;
        Singleton<FikaServer>.Instance.SendData(ref packet, DeliveryMethod.ReliableOrdered, true);
        if (changed)
        {
            _logger.LogInfo($"Broadcast hideout world state lighting={packet.LightingLevel} energy={packet.EnergyOn} areas={packet.Areas.Length}");
        }
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
            ApplyAreas(representation, packet);
            ApplyEnergy(representation, packet.EnergyOn);
            ApplyLighting(packet.LightingLevel);
            ApplyCustomization(representation, packet);
            _lastAppliedFingerprint = fingerprint;
            _logger.LogInfo($"Applied hideout world state lighting={packet.LightingLevel} energy={packet.EnergyOn}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Apply hideout world state failed: {ex.Message}");
        }
        finally
        {
            IsApplying = false;
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

    private static ELightingLevel CaptureLightingLevel()
    {
        var controller = UnityEngine.Object.FindObjectOfType<HideoutController>(true);
        if (controller != null)
        {
            return Traverse.Create(controller).Field("_currentLightingLevel").GetValue<ELightingLevel>();
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
            if (!byType.TryGetValue(data.Template.Type, out var snap))
            {
                continue;
            }

            if (data.CurrentLevel != snap.Level)
            {
                data.SetCurrentLevelDumb(snap.Level, silent: true);
            }

            if (data.Status != snap.Status)
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

        Traverse.Create(energy).Field("_switchedOn").SetValue(false);
        energy.SetInfinityGeneration(false);
        energy.SetSwitchedStatus(false);
    }

    private static void ApplyLighting(ELightingLevel level)
    {
        var controller = UnityEngine.Object.FindObjectOfType<HideoutController>(true);
        if (controller == null)
        {
            return;
        }

        var traverse = Traverse.Create(controller);
        traverse.Field("_currentLightingLevel").SetValue(level);
        traverse.Field("_lightInitialized").SetValue(true);
        controller.UpdateCameraFlashlight();

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

        for (var i = 0; i < GlobalCustomizationTypes.Length; i++)
        {
            var type = GlobalCustomizationTypes[i];
            var itemId = packet.CustomizationId(type);
            if (string.IsNullOrEmpty(itemId) || itemId.Length != 24)
            {
                continue;
            }

            var selected = customization.GetSelectedCustomizationItemId(type);
            if (selected.HasValue && selected.Value.ToString() == itemId)
            {
                continue;
            }

            customization.InstallCustomization(new MongoID(itemId), type);
        }
    }
}
