using System.Globalization;
using Tier1.Domain.Identity;

namespace Tier1.Tests;

/// <summary>
/// ADR-0019: the title before an account holder's name and the two-part name shown after it.
/// </summary>
public class PersonTitleTests
{
    /// <summary>AC-N1: the list holds the faculty titles, Mr and Ms for staff, and the military ranks the university asked for.</summary>
    [Fact]
    public void AC_N1_the_list_holds_faculty_staff_and_military_titles()
    {
        string[] Of(TitleGroup g) => [.. PersonTitles.All.Where(t => t.Group == g).Select(t => t.Ar)];
        Assert.Equal(["أ.د.", "أ.م.د.", "د.", "م.م.", "م."], Of(TitleGroup.Faculty));
        Assert.Equal(["السيد", "السيدة"], Of(TitleGroup.Staff));
        Assert.Equal(["ملازم", "ملازم أول", "نقيب", "رائد", "مقدم", "عقيد", "عميد", "لواء"], Of(TitleGroup.Officer));
        Assert.Equal(PersonTitles.All.Count, PersonTitles.All.Select(t => t.Ar).Distinct().Count());
        Assert.All(PersonTitles.All, t => Assert.True(t.Ar.Length <= PersonTitles.MaxLength));
        Assert.Null(PersonTitles.Find("فخامة"));
        Assert.Equal("أ.د.", PersonTitles.Find(" أ.د. ")?.Ar);
    }

    /// <summary>AC-N2: the name is shown in two parts: the first name and the father's. A compound name counts as one.</summary>
    [Theory]
    [InlineData("أحمد محمد علي حسن", "أحمد محمد")]
    [InlineData("منى السيد", "منى السيد")]
    [InlineData("  سارة   محمود   إبراهيم ", "سارة محمود")]
    [InlineData("عبد الرحمن محمد علي", "عبد الرحمن محمد")]
    [InlineData("محمد عبد الله إبراهيم", "محمد عبد الله")]
    [InlineData("عبد الله عبد الرحمن حسن", "عبد الله عبد الرحمن")]
    [InlineData("نور الدين أحمد مصطفى", "نور الدين أحمد")]
    [InlineData("أحمد صلاح الدين محمود", "أحمد صلاح الدين")]
    [InlineData("أبو بكر عمر خالد", "أبو بكر عمر")]
    [InlineData("عطا الله جاد الحق سليم", "عطا الله جاد الحق")]
    [InlineData("عبد رب النبي حسن علي", "عبد رب النبي حسن")]
    [InlineData("خالد", "خالد")]
    public void AC_N2_the_name_is_shown_in_two_parts(string full, string shown) => Assert.Equal(shown, PersonName.TwoPart(full));

    /// <summary>AC-N3: title then the two-part name; an account without a title keeps its full name as written.</summary>
    [Fact]
    public void AC_N3_the_title_comes_before_the_two_part_name()
    {
        Assert.Equal("أ.د. أحمد محمد", PersonName.Display("أ.د.", "أحمد محمد علي حسن", null));
        Assert.Equal("السيدة منى عبد الله", PersonName.Display("السيدة", "منى عبد الله إبراهيم", null));
        Assert.Equal("ملازم أول كريم سعيد", PersonName.Display("ملازم أول", "كريم سعيد محمود", null));
        // حساب قديم اسمه وصف وظيفة: يبقى كما هو.
        Assert.Equal("عميد كلية الهندسة بحلوان", PersonName.Display(null, "عميد كلية الهندسة بحلوان", null));
        Assert.Equal("عميد كلية الهندسة بحلوان", new AppUser { FullNameAr = "عميد كلية الهندسة بحلوان" }.DisplayName);
        Assert.Equal("أ.د. أحمد محمد", new AppUser { FullNameAr = "أحمد محمد علي", Title = "أ.د." }.DisplayName);
    }

    /// <summary>AC-N4: in the English interface the English title and the first two English names, when an English name exists.</summary>
    [Fact]
    public void AC_N4_the_english_interface_uses_the_english_title_and_name()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Prof. Ahmed Mohamed", PersonName.Display("أ.د.", "أحمد محمد علي", "Ahmed Mohamed Ali"));
            Assert.Equal("أ.د. أحمد محمد", PersonName.Display("أ.د.", "أحمد محمد علي", null));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }
}
