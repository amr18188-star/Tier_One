using Tier1.Application.Collection;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Tests;

/// <summary>قواعد جمع البيانات: التحقق من القيم، ومسار الاعتماد وفصل المهام، والتجميع وحكم الأداء، وقبول الملفات.</summary>
public class CollectionRulesTests
{
    private static readonly ValueSpec Percent = new("%", Aggregation.Central, false);
    private static readonly ValueSpec Survey = new("%", Aggregation.WeightedAverage, false);
    private static readonly ValueSpec Ratio = new("%", Aggregation.RatioOfSums, false);
    private static readonly ValueSpec Count = new("عدد", Aggregation.Sum, true);

    private static ValueInput Num(decimal? v = null, decimal? n = null, decimal? d = null, string? explanation = null) =>
        new(ValueKind.Number, v, n, d, null, explanation);

    // ---------------------------------------------------------------- القيم

    [Fact]
    public void Ratio_value_is_computed_from_numerator_and_denominator()
    {
        var (value, issues) = ValueRules.Check(Ratio, Num(n: 45, d: 50), null);
        Assert.Empty(issues);
        Assert.Equal(90m, value);
    }

    [Theory]
    [InlineData(60, 50, "numerator_exceeds")]
    [InlineData(5, 0, "denominator_zero")]
    [InlineData(-1, 10, "negative")]
    public void Invalid_ratios_are_rejected(int n, int d, string code)
    {
        var (value, issues) = ValueRules.Check(Ratio, Num(n: n, d: d), null);
        Assert.Null(value);
        Assert.Contains(issues, i => i.Code == code && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Ratio_needs_both_parts()
    {
        var (_, issues) = ValueRules.Check(Ratio, Num(v: 90), null);
        Assert.Equal("numerator_denominator", Assert.Single(issues).Code);
    }

    [Fact]
    public void Percent_over_100_and_fractional_counts_are_rejected()
    {
        Assert.Equal("percent_range", Assert.Single(ValueRules.Check(Percent, Num(150), null).Issues).Code);
        Assert.Equal("integer", Assert.Single(ValueRules.Check(Count, Num(12.5m), null).Issues).Code);
        Assert.Empty(ValueRules.Check(Count, Num(12), null).Issues);
    }

    [Fact]
    public void Weighted_average_needs_its_weight_so_the_university_value_is_exact()
    {
        Assert.Equal("weight_required", Assert.Single(ValueRules.Check(Survey, Num(80), null).Issues).Code);
        var (value, issues) = ValueRules.Check(Survey, Num(80, d: 1200), null);
        Assert.Empty(issues);
        Assert.Equal(80m, value);
    }

    [Fact]
    public void Not_available_needs_a_real_reason()
    {
        Assert.Equal("reason_required", Assert.Single(ValueRules.Check(Count, new ValueInput(ValueKind.NotAvailable, null, null, null, "لا", null), null).Issues).Code);
        var (value, issues) = ValueRules.Check(Count, new ValueInput(ValueKind.NotApplicable, 5, null, null, "لا توجد برامج دراسات عليا بالكلية", null), null);
        Assert.Empty(issues);
        Assert.Null(value);
    }

    [Fact]
    public void Big_change_from_previous_is_a_warning_not_an_error()
    {
        var (value, issues) = ValueRules.Check(Count, Num(30), previous: 10);
        Assert.Equal(30m, value);
        var w = Assert.Single(issues);
        Assert.Equal(("big_change", IssueSeverity.Warning), (w.Code, w.Severity));
        Assert.Empty(ValueRules.Check(Count, Num(12), previous: 10).Issues);
    }

    [Fact]
    public void Submit_is_blocked_by_missing_values_unexplained_warnings_and_missing_evidence()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var required = new List<(Guid, string, ValueSpec)> { (a, "K1", Count), (b, "K2", Count), (c, "K3", Percent) };
        var values = new Dictionary<Guid, SubmittedValue>
        {
            [a] = new() { Kind = ValueKind.Number, Value = 30 },
            [c] = new() { Kind = ValueKind.Number, Value = 50 },
        };
        var blocking = ValueRules.ReadyToSubmit(required, values, new Dictionary<Guid, int>(), new Dictionary<Guid, decimal?> { [a] = 10 });
        var codes = blocking.Select(x => (x.IndicatorId, x.Issue.Code)).ToList();
        Assert.Contains((a, "explanation_required"), codes);
        Assert.Contains((a, "evidence_required"), codes);
        Assert.Contains((b, "missing"), codes);
        Assert.DoesNotContain(codes, x => x.IndicatorId == c);

        values[a].Explanation = "افتتاح برنامجين جديدين";
        values[b] = new() { Kind = ValueKind.NotApplicable, Reason = "لا ينطبق على الكلية" };
        Assert.Empty(ValueRules.ReadyToSubmit(required, values, new Dictionary<Guid, int> { [a] = 1 }, new Dictionary<Guid, decimal?> { [a] = 10 }));
    }

    // ---------------------------------------------------------------- مسار الاعتماد

    [Fact]
    public void Approval_path_depends_on_unit_kind()
    {
        Assert.Equal(ApprovalStep.DepartmentReview, ApprovalRules.Next(OrgUnitKind.Department, ApprovalStep.Entry));
        Assert.Equal(ApprovalStep.CollegeCommittee, ApprovalRules.Next(OrgUnitKind.College, ApprovalStep.Entry));
        Assert.Equal(ApprovalStep.Quality, ApprovalRules.Next(OrgUnitKind.Center, ApprovalStep.Entry));
        Assert.Equal(ApprovalStep.Done, ApprovalRules.Next(OrgUnitKind.College, ApprovalStep.Quality));
        Assert.Throws<InvalidOperationException>(() => ApprovalRules.Next(OrgUnitKind.College, ApprovalStep.DepartmentReview));
        Assert.Equal(ApprovalRules.ApproveCollege, ApprovalRules.SpecialFor(ApprovalStep.Dean));
    }

    [Fact]
    public void Editor_cannot_review_and_one_person_approves_one_step()
    {
        var editor = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        Assert.Equal("sod_sod_c1", Assert.Throws<DomainException>(() => ApprovalRules.EnsureSeparation(editor, [editor], [])).Code);
        var history = new[] { new SubmissionEvent { Action = SubmissionAction.Approved, By = reviewer } };
        Assert.Equal("sod_sod_c2", Assert.Throws<DomainException>(() => ApprovalRules.EnsureSeparation(reviewer, [editor], history)).Code);
        ApprovalRules.EnsureSeparation(Guid.NewGuid(), [editor], history);
    }

    [Fact]
    public void An_approval_from_an_earlier_round_does_not_bar_the_reviewer_CR4()
    {
        var reviewer = Guid.NewGuid();
        var resent = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var earlier = new SubmissionEvent { Action = SubmissionAction.Approved, By = reviewer, At = resent.AddDays(-2) };
        var thisRound = new SubmissionEvent { Action = SubmissionAction.Approved, By = reviewer, At = resent.AddMinutes(5) };
        Assert.Null(ApprovalRules.SeparationBreach(reviewer, [], [earlier], resent));
        Assert.Equal("SOD-C2", ApprovalRules.SeparationBreach(reviewer, [], [earlier, thisRound], resent));
        // Without a submission time every approval counts, the strict reading.
        Assert.Equal("SOD-C2", ApprovalRules.SeparationBreach(reviewer, [], [earlier], null));
        Assert.Equal("SOD-C1", ApprovalRules.SeparationBreach(reviewer, [reviewer], [], resent));
        ApprovalRules.EnsureSeparation(reviewer, [], [earlier], resent);
    }

    [Theory]
    [InlineData(SubmissionAction.Submitted, ApprovalStep.Entry, ApprovalStep.CollegeCommittee, "dc.form_submitted")]
    [InlineData(SubmissionAction.Approved, ApprovalStep.DepartmentReview, ApprovalStep.CollegeCommittee, "dc.form_approved")]
    [InlineData(SubmissionAction.Approved, ApprovalStep.DepartmentReview, ApprovalStep.Dean, "dc.form_approved_department_directly")]
    [InlineData(SubmissionAction.Approved, ApprovalStep.DepartmentReview, ApprovalStep.Quality, "dc.form_approved_as_dean_directly")]
    [InlineData(SubmissionAction.Approved, ApprovalStep.CollegeCommittee, ApprovalStep.Quality, "dc.form_approved_as_dean_directly")]
    [InlineData(SubmissionAction.Approved, ApprovalStep.Dean, ApprovalStep.Quality, "dc.form_approved")]
    [InlineData(SubmissionAction.Approved, ApprovalStep.Quality, ApprovalStep.Done, "dc.form_approved_final")]
    [InlineData(SubmissionAction.Returned, ApprovalStep.Dean, ApprovalStep.Entry, "dc.form_returned")]
    [InlineData(SubmissionAction.Reopened, ApprovalStep.Done, ApprovalStep.Entry, "dc.form_reopened")]
    public void Audit_verb_for_each_transition(SubmissionAction action, ApprovalStep from, ApprovalStep to, string verb) =>
        Assert.Equal(verb, ApprovalRules.AuditVerb(action, from, to).Verb);

    [Fact]
    public void Form_is_editable_only_at_entry_before_review()
    {
        Assert.True(new Submission { Step = ApprovalStep.Entry, Status = SubmissionStatus.Returned }.IsEditable);
        Assert.False(new Submission { Step = ApprovalStep.Dean, Status = SubmissionStatus.InReview }.IsEditable);
        Assert.False(new Submission { Step = ApprovalStep.Done, Status = SubmissionStatus.Approved }.IsEditable);
    }

    // ---------------------------------------------------------------- التجميع والأداء

    private static UnitValue V(decimal? value, decimal? n = null, decimal? d = null, ValueKind kind = ValueKind.Number) => new(Guid.NewGuid(), kind, value, n, d);

    [Fact]
    public void Percentages_combine_as_ratio_of_sums_never_average()
    {
        // 9/10 = 90% و 10/100 = 10%: المتوسط 50% خطأ، والصحيح 19/110.
        var a = ResultsRules.Combine(Aggregation.RatioOfSums, true, [V(90, 9, 10), V(10, 10, 100)], 2);
        Assert.Equal(decimal.Round(19m / 110m * 100, 4), a.Value);
        Assert.Equal((2, 2), (a.Reported, a.Expected));
    }

    [Fact]
    public void Sums_skip_not_applicable_and_count_coverage()
    {
        var a = ResultsRules.Combine(Aggregation.Sum, false, [V(5), V(7), V(null, kind: ValueKind.NotApplicable)], 4);
        Assert.Equal(12m, a.Value);
        // D6: «غير منطبق» يخرج الجهة من المطلوب، والمبلغ عنه هو الأرقام وحدها: رقمان من ثلاث جهات مطلوبة.
        Assert.Equal((2, 3, 1), (a.Reported, a.Expected, a.NotApplicable));
    }

    [Fact]
    public void Distinct_counts_are_not_summed_from_units()
    {
        var a = ResultsRules.Combine(Aggregation.DistinctCount, false, [V(5), V(7)], 2);
        Assert.Null(a.Value);
        Assert.NotNull(a.Note);
    }

    [Fact]
    public void Weighted_average_uses_denominators()
    {
        var a = ResultsRules.Combine(Aggregation.WeightedAverage, true, [V(80, d: 10), V(50, d: 30)], 2);
        Assert.Equal(57.5m, a.Value);
    }

    [Theory]
    [InlineData(Direction.HigherIsBetter, 95, 100, Performance.Near)]
    [InlineData(Direction.HigherIsBetter, 100, 100, Performance.Achieved)]
    [InlineData(Direction.HigherIsBetter, 50, 100, Performance.Behind)]
    [InlineData(Direction.LowerIsBetter, 20, 25, Performance.Achieved)]
    [InlineData(Direction.LowerIsBetter, 30, 25, Performance.Behind)]
    public void Performance_follows_direction(Direction direction, int actual, int target, Performance expected) =>
        Assert.Equal(expected, ResultsRules.Judge(direction, actual, target));

    [Fact]
    public void Missing_data_is_not_judged_as_weak_performance()
    {
        Assert.Equal(Performance.NoData, ResultsRules.Judge(Direction.HigherIsBetter, null, 10));
        Assert.Equal(Performance.NoTarget, ResultsRules.Judge(Direction.HigherIsBetter, 10, null));
    }

    [Fact]
    public void Summary_reads_as_a_sentence()
    {
        var s = ResultsRules.Summary("الجامعة", "%", Direction.HigherIsBetter, 45, 50, 42);
        Assert.Contains("90% من المستهدف", s);
        Assert.Contains("بتحسن", s);
    }

    // ---------------------------------------------------------------- الملفات والاستيراد

    [Fact]
    public void File_type_comes_from_content_signature()
    {
        Assert.Equal("application/pdf", FileRules.ContentTypeFor("كشف.pdf", "%PDF-1.7"u8));
        Assert.Null(FileRules.ContentTypeFor("كشف.pdf", "hello"u8));
        Assert.Null(FileRules.ContentTypeFor("run.exe", "MZ"u8));
        Assert.Equal("image/png", FileRules.ContentTypeFor("a.PNG", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }));
    }

    [Fact]
    public void File_names_are_cleaned()
    {
        Assert.Equal("evil.pdf", FileRules.CleanName("../../evil.pdf"));
        Assert.Equal("ab.pdf", FileRules.CleanName("a<b>.pdf"));
        Assert.True(FileRules.CleanName(new string('x', 300) + ".pdf").Length <= 200);
    }

    [Theory]
    [InlineData("١٣", 13)]
    [InlineData("12.5", 12.5)]
    [InlineData("٤٥٫٥", 45.5)]
    [InlineData("1,250", 1250)]
    [InlineData("85%", 85)]
    public void Excel_cells_accept_arabic_digits(string text, double expected)
    {
        var issues = new List<IssueDto>();
        Assert.Equal((decimal)expected, FormEntryService.Parse(text, "القيمة", issues));
        Assert.Empty(issues);
    }

    [Fact]
    public void Non_numeric_cells_are_reported()
    {
        var issues = new List<IssueDto>();
        Assert.Null(FormEntryService.Parse("abc", "القيمة", issues));
        Assert.Equal("not_a_number", Assert.Single(issues).Code);
    }
}
