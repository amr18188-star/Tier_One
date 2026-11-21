namespace Tier1.IntegrationTests;

/// <summary>ADR-0017: the college structure template: the management card first, and the college quality officer.</summary>
[Collection("app")]
public sealed class StructureTemplateTests(Tier1App app)
{
    private static readonly string[] Management =
    [
        "عميد الكلية", "وكيل الكلية للدراسات العليا والبحوث", "وكيل الكلية لشئون التعليم والطلاب", "وكيل الكلية لشئون خدمة المجتمع وتنمية البيئة",
    ];

    /// <summary>AC-S1, AC-S2: the first card is «إدارة الكلية» with the dean and the three vice deans; the careers centre holds the quality officer.</summary>
    [Fact]
    public async Task The_first_card_is_college_management_and_the_careers_centre_has_the_quality_officer()
    {
        var template = (await (await app.As("planner")).Get("/api/structure/template")).Items().ToList();

        var head = template.First();
        Assert.Equal(("DEAN", "إدارة الكلية", true), (head["code"].Str(), head["nameAr"].Str(), head["isHead"].Truthy()));
        var positions = head["positions"].Items().ToList();
        Assert.Equal(Management, positions.Select(p => p["titleAr"].Str()));
        // AC-S3: each is one person, and the structure is not complete without them.
        Assert.All(positions, p => Assert.True(p["single"].Truthy() && p["required"].Truthy(), p.ToJsonString()));
        // The dean keeps the dean's suggested role; the vice deans' roles are created by the super administrator.
        Assert.Equal("DEAN", positions[0]["suggestedRoleCode"].Str());
        Assert.All(positions.Skip(1), p => Assert.Null(p["suggestedRoleCode"].Str()));

        var careers = template.Single(u => u["code"].Str() == "CAREERS");
        Assert.Equal("مركز التوظيف والتدريب ومتابعة الخريجين والجودة", careers["nameAr"].Str());
        var quality = careers["positions"].Items().ElementAt(1);
        Assert.Equal(("مسئول الجودة بالكلية", true, true), (quality["titleAr"].Str(), quality["single"].Truthy(), quality["required"].Truthy()));
    }

    /// <summary>AC-S3: a college with only its dean appointed lists the vice deans and the quality officer as missing.</summary>
    [Fact]
    public async Task A_college_without_its_vice_deans_and_quality_officer_is_incomplete()
    {
        var planner = await app.As("planner");
        var college = (await planner.Get("/api/structure/completeness")).Items().First();
        var chart = (await planner.Get($"/api/structure/colleges/{college["collegeId"].Str()}"))!;
        var issues = string.Join("\n", chart["completeness"]!["issues"].Items().Select(i => i["messageAr"].Str()));
        foreach (var title in Management.Skip(1).Append("مسئول الجودة بالكلية"))
            Assert.Contains(title, issues, StringComparison.Ordinal);
    }
}
