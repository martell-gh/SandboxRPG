namespace MTEngine.Items;

/// <summary>
/// Size category for items. Containers use MaxItemSize to limit what fits.
/// </summary>
public enum ItemSize
{
    Tiny = 1,    // coins, keys, bullets, rings
    Small = 2,   // potions, daggers, ammo boxes
    Medium = 3,  // swords, bottles, books, tools
    Large = 4,   // rifles, shields, armor pieces
    Huge = 5     // two-handed weapons, furniture
}
