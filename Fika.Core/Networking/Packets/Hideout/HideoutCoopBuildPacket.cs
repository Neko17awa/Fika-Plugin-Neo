namespace Fika.Core.Networking.Packets.Hideout;

public enum EHideoutCoopBuildKind : byte
{
    Request = 0,
    Ack = 1,
    Spent = 2,
    ApplyOk = 3,
    Refund = 4,
    Complete = 5,
    CompleteAck = 6
}

public class HideoutCoopBuildItem
{
    public string Id = "";
    public string TemplateId = "";
    public int Count;
    public bool IsTool;
}

/// <summary>
/// 访客物品 → 主人藏身处档案的两阶段建造协议。独立 INetSerializable，不进战局 Generic 池。
/// </summary>
public class HideoutCoopBuildPacket : INetSerializable
{
    public EHideoutCoopBuildKind Kind;
    public int RequestId;
    public int AreaType;
    public bool Ok;
    public string Error = "";
    public HideoutCoopBuildItem[] Items = [];

    public void Deserialize(NetDataReader reader)
    {
        Kind = (EHideoutCoopBuildKind)reader.GetByte();
        RequestId = reader.GetInt();
        AreaType = reader.GetInt();
        Ok = reader.GetBool();
        Error = reader.GetString() ?? "";
        var count = reader.GetUShort();
        Items = new HideoutCoopBuildItem[count];
        for (var i = 0; i < count; i++)
        {
            Items[i] = new HideoutCoopBuildItem
            {
                Id = reader.GetString() ?? "",
                TemplateId = reader.GetString() ?? "",
                Count = reader.GetInt(),
                IsTool = reader.GetBool()
            };
        }
    }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((byte)Kind);
        writer.Put(RequestId);
        writer.Put(AreaType);
        writer.Put(Ok);
        writer.Put(Error ?? "");
        var count = (ushort)(Items?.Length ?? 0);
        writer.Put(count);
        for (var i = 0; i < count; i++)
        {
            var item = Items[i] ?? new HideoutCoopBuildItem();
            writer.Put(item.Id ?? "");
            writer.Put(item.TemplateId ?? "");
            writer.Put(item.Count);
            writer.Put(item.IsTool);
        }
    }
}
