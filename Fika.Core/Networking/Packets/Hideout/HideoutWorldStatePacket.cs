using System.Text;
using EFT;
using EFT.Hideout;

namespace Fika.Core.Networking.Packets.Hideout;

/// <summary>
/// 藏身处世界环境快照：灯光、供电、区域等级、全局装饰。
/// 独立 INetSerializable，不进战局 GenericSubPacket 池。
/// </summary>
public class HideoutWorldStatePacket : INetSerializable
{
    public ELightingLevel LightingLevel;
    public bool EnergyOn;
    public HideoutAreaWorldState[] Areas = [];
    public string FloorId = "";
    public string WallId = "";
    public string CeilingId = "";
    public string LightId = "";
    public string ShootingRangeMarkId = "";

    public string Fingerprint()
    {
        var sb = new StringBuilder(128);
        sb.Append((int)LightingLevel).Append('|').Append(EnergyOn ? '1' : '0');
        if (Areas != null)
        {
            for (var i = 0; i < Areas.Length; i++)
            {
                var area = Areas[i];
                sb.Append('|').Append((int)area.Type)
                    .Append(':').Append(area.Level)
                    .Append(':').Append((int)area.Status)
                    .Append(':').Append(area.IsActive ? '1' : '0')
                    .Append(':').Append((int)area.LightStatus);
            }
        }

        sb.Append('|').Append(FloorId)
            .Append('|').Append(WallId)
            .Append('|').Append(CeilingId)
            .Append('|').Append(LightId)
            .Append('|').Append(ShootingRangeMarkId);
        return sb.ToString();
    }

    public string CustomizationId(EHideoutCustomizationType type)
    {
        return type switch
        {
            EHideoutCustomizationType.Floor => FloorId,
            EHideoutCustomizationType.Wall => WallId,
            EHideoutCustomizationType.Ceiling => CeilingId,
            EHideoutCustomizationType.Light => LightId,
            EHideoutCustomizationType.ShootingRangeMark => ShootingRangeMarkId,
            _ => ""
        };
    }

    public void SetCustomizationId(EHideoutCustomizationType type, string itemId)
    {
        var value = itemId ?? "";
        switch (type)
        {
            case EHideoutCustomizationType.Floor:
                FloorId = value;
                break;
            case EHideoutCustomizationType.Wall:
                WallId = value;
                break;
            case EHideoutCustomizationType.Ceiling:
                CeilingId = value;
                break;
            case EHideoutCustomizationType.Light:
                LightId = value;
                break;
            case EHideoutCustomizationType.ShootingRangeMark:
                ShootingRangeMarkId = value;
                break;
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        LightingLevel = (ELightingLevel)reader.GetInt();
        EnergyOn = reader.GetBool();
        var count = reader.GetUShort();
        Areas = new HideoutAreaWorldState[count];
        for (var i = 0; i < count; i++)
        {
            Areas[i] = new HideoutAreaWorldState
            {
                Type = (EAreaType)reader.GetInt(),
                Level = reader.GetInt(),
                Status = (EAreaStatus)reader.GetByte(),
                IsActive = reader.GetBool(),
                LightStatus = (ELightStatus)reader.GetByte()
            };
        }

        FloorId = reader.GetString();
        WallId = reader.GetString();
        CeilingId = reader.GetString();
        LightId = reader.GetString();
        ShootingRangeMarkId = reader.GetString();
    }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((int)LightingLevel);
        writer.Put(EnergyOn);
        var count = (ushort)(Areas?.Length ?? 0);
        writer.Put(count);
        for (var i = 0; i < count; i++)
        {
            var area = Areas[i];
            writer.Put((int)area.Type);
            writer.Put(area.Level);
            writer.Put((byte)area.Status);
            writer.Put(area.IsActive);
            writer.Put((byte)area.LightStatus);
        }

        writer.Put(FloorId ?? "");
        writer.Put(WallId ?? "");
        writer.Put(CeilingId ?? "");
        writer.Put(LightId ?? "");
        writer.Put(ShootingRangeMarkId ?? "");
    }
}

public struct HideoutAreaWorldState
{
    public EAreaType Type;
    public int Level;
    public EAreaStatus Status;
    public bool IsActive;
    public ELightStatus LightStatus;
}
