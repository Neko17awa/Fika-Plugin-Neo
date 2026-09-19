using EFT;
using Fika.Core.Networking;
using Fika.Core.Networking.Packets.Player;

namespace Fika.Core.Main.PacketHandlers;

/// <summary>
/// 从 <see cref="HideoutPlayer"/> 打包 <see cref="PlayerStateData"/>，复用战局同一条状态通道。
/// </summary>
public sealed class HideoutPacketSender : MonoBehaviour
{
    public bool SendState { get; set; }
    public IFikaNetworkManager NetworkManager { get; set; }
    public ushort NetId { get; set; }

    private Player _player;
    private int _animHash;
    private float _updateCount;
    private float _updatesPerTick;

    public static HideoutPacketSender Create(Player player, ushort netId, IFikaNetworkManager manager)
    {
        var sender = player.gameObject.GetComponent<HideoutPacketSender>();
        if (sender == null)
        {
            sender = player.gameObject.AddComponent<HideoutPacketSender>();
        }

        sender._player = player;
        sender.NetId = netId;
        sender.NetworkManager = manager;
        sender.ConfigureRate(manager.SendRate);
        sender._animHash = PlayerAnimator.INERT_PARAM_HASH;
        sender.SendState = true;
        sender.enabled = true;
        return sender;
    }

    private void ConfigureRate(int sendRate)
    {
        var rate = sendRate > 0 ? sendRate : 20;
        _updatesPerTick = 1f / rate;
        _updateCount = 0f;
    }

    private bool IsMoving
    {
        get
        {
            var animator = _player?.MovementContext?.PlayerAnimator?.Animator;
            return animator != null && animator.GetBool(_animHash);
        }
    }

    private void Update()
    {
        if (!SendState || NetworkManager == null || _player == null)
        {
            return;
        }

        _updateCount += Time.unscaledDeltaTime;
        if (_updateCount < _updatesPerTick)
        {
            return;
        }

        _updateCount -= _updatesPerTick;
        var state = new PlayerStateData(_player, NetId, IsMoving);
        NetworkManager.SendPlayerState(ref state);
    }

    public void DestroyThis()
    {
        SendState = false;
        NetworkManager = null;
        _player = null;
        Destroy(this);
    }
}
