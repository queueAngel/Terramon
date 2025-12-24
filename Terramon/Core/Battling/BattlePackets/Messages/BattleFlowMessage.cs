namespace Terramon.Core.Battling.BattlePackets.Messages;

public sealed class SetHPMessage : BattleMessage
{
    public byte Slot;
    public ushort TargetHP;
    public SetHPMessage() { }
    public SetHPMessage(byte slot, ushort targetHP)
    {
        Slot = slot;
        TargetHP = targetHP;
    }
    public override void Write(BinaryWriter w)
    {
        w.Write(Slot);
        w.Write(TargetHP);
    }
    public override void Read(BinaryReader r)
    {
        Slot = r.ReadByte();
        TargetHP = r.ReadUInt16();
    }
}
