namespace MTEngine.Npc;

/// <summary>
/// Простой генератор имён для рождённых NPC. Пока встроенный пул;
/// позже можно заменить на чтение из Data/names_&lt;faction&gt;.json (см. §6.2).
/// </summary>
public static class ChildNameGenerator
{
    private static readonly string[] MaleNames =
    {
        "Игорь", "Олег", "Артём", "Михаил", "Никита", "Сергей", "Степан",
        "Лев", "Глеб", "Юрий", "Илья", "Тимофей", "Кирилл", "Антон",
        "Святослав", "Богдан", "Всеволод", "Захар", "Прохор", "Роман"
    };

    private static readonly string[] FemaleNames =
    {
        "Лада", "Заря", "Млада", "Веда", "Светла", "Любава", "Олеся",
        "Дарина", "Злата", "Мила", "Ярина", "Олена", "Мирослава",
        "Радмила", "Снежана", "Власта", "Беляна", "Купава", "Лукерья", "Дарья"
    };

    public static string Pick(Gender gender, Random rng)
    {
        var pool = gender == Gender.Female ? FemaleNames : MaleNames;
        return pool[rng.Next(pool.Length)];
    }
}
