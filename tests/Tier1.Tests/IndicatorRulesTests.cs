using Tier1.Application.Kpi;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;

namespace Tier1.Tests;

/// <summary>دورة حياة تعريف المؤشر: الاكتمال قبل الإرسال، والإصدار الجديد للمعتمد، وفصل المهام في الاعتماد.</summary>
public class IndicatorRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Editor = Guid.NewGuid();
    private static readonly Guid Committee = Guid.NewGuid();

    private static IndicatorVersion Complete() => new()
    {
        Number = 1,
        NameAr = "نسبة الأبحاث المصنفة ضمن Q1",
        Definition = "نسبة الأبحاث المنشورة في مجلات الربع الأول",
        Numerator = "الأبحاث في Q1",
        Denominator = "إجمالي الأبحاث",
        Unit = "%",
        Direction = Direction.HigherIsBetter,
        Aggregation = Aggregation.RatioOfSums,
        DataSource = "Scopus",
        OwnerUnitId = Guid.NewGuid(),
        EditedBy = Editor,
    };

    private static Indicator With(params IndicatorVersion[] versions)
    {
        var i = new Indicator { Code = "S04-08" };
        i.Versions.AddRange(versions);
        return i;
    }

    [Fact]
    public void A_complete_definition_has_nothing_missing()
    {
        Assert.Empty(IndicatorRules.MissingForApproval(Complete()));
    }

    [Fact]
    public void Missing_fields_are_named_in_arabic()
    {
        var v = Complete();
        v.Definition = null;
        v.Unit = "غير محدد (kWh؟)";
        v.OwnerUnitId = null;
        v.Numerator = null;
        var missing = IndicatorRules.MissingForApproval(v);
        Assert.Equal(["التعريف", "وحدة القياس", "البسط والمقام", "الجهة المالكة"], missing);
    }

    [Fact]
    public void An_incomplete_draft_cannot_be_submitted()
    {
        var v = Complete();
        v.DataSource = "";
        var e = Assert.Throws<DomainException>(() => IndicatorRules.Submit(v, Editor, Now));
        Assert.Equal("definition_incomplete", e.Code);
        Assert.Contains("مصدر البيانات", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Whoever_prepared_the_definition_cannot_approve_it()
    {
        var v = Complete();
        var indicator = With(v);
        IndicatorRules.Submit(v, Editor, Now);
        var e = Assert.Throws<DomainException>(() => IndicatorRules.Approve(indicator, v, Editor, Now, "محضر 1"));
        Assert.Equal("sod_sod_k1", e.Code);
        Assert.Equal(DefinitionStatus.UnderReview, v.Status);
    }

    [Fact]
    public void Approval_needs_the_minutes_reference()
    {
        var v = Complete();
        IndicatorRules.Submit(v, Editor, Now);
        Assert.Equal("decision_ref_required", Assert.Throws<DomainException>(() => IndicatorRules.Approve(With(v), v, Committee, Now, " ")).Code);
    }

    [Fact]
    public void Editing_an_approved_definition_creates_a_new_version_and_keeps_the_approved_one_in_force()
    {
        var v1 = Complete();
        var indicator = With(v1);
        IndicatorRules.Submit(v1, Editor, Now);
        IndicatorRules.Approve(indicator, v1, Committee, Now, "محضر 1");

        Assert.Equal("change_reason_required", Assert.Throws<DomainException>(() => IndicatorRules.EditableVersion(indicator, Now, null)).Code);

        var v2 = IndicatorRules.EditableVersion(indicator, Now, "تعديل المقام بقرار اللجنة");
        v2.Denominator = "الأبحاث المفهرسة فقط";
        Assert.Equal(2, v2.Number);
        Assert.Equal(DefinitionStatus.Draft, v2.Status);
        Assert.Same(v1, indicator.Effective);
        Assert.Equal("إجمالي الأبحاث", indicator.Effective!.Denominator);
        Assert.Same(v2, indicator.Working);

        v2.EditedBy = Editor;
        IndicatorRules.Submit(v2, Editor, Now);
        IndicatorRules.Approve(indicator, v2, Committee, Now, "محضر 2");
        Assert.Equal(DefinitionStatus.Superseded, v1.Status);
        Assert.Same(v2, indicator.Effective);
        Assert.Null(indicator.Working);
    }

    [Fact]
    public void Editing_a_definition_under_review_returns_it_to_draft()
    {
        var v = Complete();
        var indicator = With(v);
        IndicatorRules.Submit(v, Editor, Now);
        var same = IndicatorRules.EditableVersion(indicator, Now, null);
        Assert.Same(v, same);
        Assert.Equal(DefinitionStatus.Draft, v.Status);
        Assert.Null(v.SubmittedAt);
    }

    [Fact]
    public void A_returned_definition_keeps_the_committee_note()
    {
        var v = Complete();
        IndicatorRules.Submit(v, Editor, Now);
        IndicatorRules.Return(v, "حدد هل الأبحاث المشتركة تحسب مرة واحدة");
        Assert.Equal(DefinitionStatus.Draft, v.Status);
        Assert.Equal("حدد هل الأبحاث المشتركة تحسب مرة واحدة", v.ReturnNote);
    }
}
