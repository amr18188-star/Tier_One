using Tier1.Domain.Common;

namespace Tier1.Domain.Identity;

public enum TitleGroup
{
    /// <summary>أعضاء هيئة التدريس ومعاونوهم.</summary>
    Faculty = 1,

    /// <summary>الموظفون.</summary>
    Staff = 2,

    /// <summary>الرتب العسكرية.</summary>
    Officer = 3,
}

/// <summary>لقب يسبق اسم صاحب الحساب: نصه العربي (وهو ما يحفظ)، ومقابله الإنجليزي، ومعناه لقائمة الاختيار.</summary>
public sealed record PersonTitle(string Ar, string En, TitleGroup Group, string MeaningAr, string MeaningEn);

/// <summary>
/// الألقاب المتاحة لأصحاب الحسابات (ADR-0019). قائمة مغلقة: يختار مدير الحسابات منها، ولا يكتب لقبًا حرًا، فيبقى شكل الأسماء واحدًا
/// في الشاشات والسجلات. السيد والسيدة يحددهما من ينشئ الحساب بحسب النوع؛ لا يحفظ النوع حقلًا مستقلًا.
/// </summary>
public static class PersonTitles
{
    public const int MaxLength = 20;

    public static readonly IReadOnlyList<PersonTitle> All =
    [
        new("أ.د.", "Prof.", TitleGroup.Faculty, "أستاذ دكتور", "Professor"),
        new("أ.م.د.", "Assoc. Prof.", TitleGroup.Faculty, "أستاذ مساعد دكتور", "Associate professor"),
        new("د.", "Dr.", TitleGroup.Faculty, "دكتور (مدرس)", "Lecturer"),
        new("م.م.", "Asst. Lect.", TitleGroup.Faculty, "مدرس مساعد", "Assistant lecturer"),
        new("م.", "TA", TitleGroup.Faculty, "معيد", "Demonstrator"),
        new("السيد", "Mr.", TitleGroup.Staff, "موظف", "Staff member (male)"),
        new("السيدة", "Ms.", TitleGroup.Staff, "موظفة", "Staff member (female)"),
        new("ملازم", "Lt.", TitleGroup.Officer, "ملازم", "Lieutenant"),
        new("ملازم أول", "1st Lt.", TitleGroup.Officer, "ملازم أول", "First lieutenant"),
        new("نقيب", "Capt.", TitleGroup.Officer, "نقيب", "Captain"),
        new("رائد", "Maj.", TitleGroup.Officer, "رائد", "Major"),
        new("مقدم", "Lt. Col.", TitleGroup.Officer, "مقدم", "Lieutenant colonel"),
        new("عقيد", "Col.", TitleGroup.Officer, "عقيد", "Colonel"),
        new("عميد", "Brig. Gen.", TitleGroup.Officer, "عميد", "Brigadier general"),
        new("لواء", "Maj. Gen.", TitleGroup.Officer, "لواء", "Major general"),
    ];

    public static PersonTitle? Find(string? ar) => string.IsNullOrWhiteSpace(ar) ? null : All.FirstOrDefault(t => t.Ar == ar.Trim());
}

/// <summary>
/// اسم صاحب الحساب كما يعرض (ADR-0019): اللقب ثم الاسم **ثنائيًا** (الاسم الأول واسم الأب). الحساب بلا لقب يعرض اسمه كاملًا كما
/// كتب، لأن حسابات قديمة أسماؤها وصف وظيفة («عميد كلية الهندسة») لا اسم شخص، واختصارها يفسدها.
/// </summary>
public static class PersonName
{
    // كلمات تبدأ اسمًا مركبًا مع ما بعدها (عبد الله، أبو بكر)، وكلمات تتمه مع ما قبلها (نور الدين، عطا الله).
    private static readonly HashSet<string> Leading = ["عبد", "أبو", "ابو", "أم", "ام", "آل", "ذو", "بن", "ابن"];
    private static readonly HashSet<string> Trailing = ["الدين", "الله", "الإسلام", "الاسلام", "الحق"];

    /// <summary>أول اسمين من الاسم الكامل، والاسم المركب (عبد الرحمن، نور الدين، عبد رب النبي) يعد اسمًا واحدًا.</summary>
    public static string TwoPart(string full)
    {
        var words = full.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        for (var i = 0; i < words.Length && parts.Count < 2;)
        {
            var take = 1;
            if (Leading.Contains(words[i]) && i + 1 < words.Length)
                take = words[i] == "عبد" && words[i + 1] == "رب" && i + 2 < words.Length ? 3 : 2;
            if (i + take < words.Length && Trailing.Contains(words[i + take])) take++;
            parts.Add(string.Join(' ', words.Skip(i).Take(take)));
            i += take;
        }
        return string.Join(' ', parts);
    }

    public static string Display(string? title, string fullAr, string? fullEn)
    {
        if (PersonTitles.Find(title) is not { } t) return Lang.Name(fullAr, fullEn);
        if (Lang.IsEnglish && !string.IsNullOrWhiteSpace(fullEn))
            return $"{t.En} {string.Join(' ', fullEn.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(2))}";
        return $"{t.Ar} {TwoPart(fullAr)}";
    }
}
