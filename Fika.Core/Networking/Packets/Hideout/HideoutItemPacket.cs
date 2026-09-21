using EFT.InventoryLogic;
using Fika.Core.Networking.Packets.Player.Common;

namespace Fika.Core.Networking.Packets.Hideout;

public enum EHideoutItemAction : byte
{
    Hands = 0,
    UnequipHands = 1,
    Drop = 2,
    Place = 3,
    Pickup = 4
}

/// <summary>
/// 藏身处手持、丢弃、摆放、拾取。带完整物品描述，不依赖战局 FikaHostWorld。
/// </summary>
public class HideoutItemPacket : INetSerializable
{
    public EHideoutItemAction Action;
    public int NetId;
    public EProceedType ProceedType;
    public bool FastDrop;
    public bool Patrol;
    public Item Item;
    public string LootId = "";
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;

    public HideoutItemPacket Clone()
    {
        return new HideoutItemPacket
        {
            Action = Action,
            NetId = NetId,
            ProceedType = ProceedType,
            FastDrop = FastDrop,
            Patrol = Patrol,
            Item = Item,
            LootId = LootId ?? "",
            Position = Position,
            Rotation = Rotation,
            Velocity = Velocity,
            AngularVelocity = AngularVelocity
        };
    }

    public void Deserialize(NetDataReader reader)
    {
        Action = (EHideoutItemAction)reader.GetByte();
        NetId = reader.GetInt();
        switch (Action)
        {
            case EHideoutItemAction.Hands:
                ProceedType = reader.GetEnum<EProceedType>();
                Patrol = reader.GetBool();
                Item = ProceedType is EProceedType.EmptyHands ? null : reader.GetItem();
                break;
            case EHideoutItemAction.UnequipHands:
                FastDrop = reader.GetBool();
                break;
            case EHideoutItemAction.Drop:
            case EHideoutItemAction.Place:
                Item = reader.GetItem();
                LootId = reader.GetString();
                Position = reader.GetUnmanaged<Vector3>();
                Rotation = reader.GetUnmanaged<Quaternion>();
                Velocity = reader.GetUnmanaged<Vector3>();
                AngularVelocity = reader.GetUnmanaged<Vector3>();
                break;
            case EHideoutItemAction.Pickup:
                LootId = reader.GetString();
                break;
        }
    }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((byte)Action);
        writer.Put(NetId);
        switch (Action)
        {
            case EHideoutItemAction.Hands:
                writer.PutEnum(ProceedType);
                writer.Put(Patrol);
                if (ProceedType is not EProceedType.EmptyHands)
                {
                    writer.PutItem(Item);
                }
                break;
            case EHideoutItemAction.UnequipHands:
                writer.Put(FastDrop);
                break;
            case EHideoutItemAction.Drop:
            case EHideoutItemAction.Place:
                writer.PutItem(Item);
                writer.Put(LootId ?? "");
                writer.PutUnmanaged(Position);
                writer.PutUnmanaged(Rotation);
                writer.PutUnmanaged(Velocity);
                writer.PutUnmanaged(AngularVelocity);
                break;
            case EHideoutItemAction.Pickup:
                writer.Put(LootId ?? "");
                break;
        }
    }
}
