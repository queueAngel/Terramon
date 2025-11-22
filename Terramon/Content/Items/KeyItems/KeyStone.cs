namespace Terramon.Content.Items.KeyItems;

public sealed class KeyStone : KeyItem
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.useStyle = ItemUseStyleID.Swing;
        Item.useTime = Item.useAnimation = 12;
    }
    public override bool? UseItem(Player player)
    {
        var modPlayer = player.Terramon();

        if (modPlayer.ActivePetProjectile is null)
            return null;

        modPlayer.ActivePetProjectile.ToggleMegaEvolution();
        return true;
    }
}
