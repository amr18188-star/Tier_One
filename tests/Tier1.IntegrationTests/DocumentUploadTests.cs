namespace Tier1.IntegrationTests;

/// <summary>BE-8: the same file attached twice to the same owner is refused; after voiding it, it can be attached again.</summary>
[Collection("app")]
public sealed class DocumentUploadTests(Tier1App app)
{
    [Fact]
    public async Task A_second_identical_evidence_file_is_refused_until_the_first_is_voided()
    {
        var cycle = await Flow.OpenCycleAsync(app);
        var form = await Flow.FormAsync(app, cycle, "ENG", "entry");
        var item = form.ItemsOf().First();
        var entry = await app.As("entry");
        await entry.Ok("PUT", $"/api/forms/{form["id"].Str()}/values", Calls.InputFor(item));
        var path = $"/api/forms/{form["id"].Str()}/values/{item["indicatorId"].Str()}/evidence";
        var file = Env.UniquePdf();

        var (first, doc) = await entry.Upload(path, "evidence.pdf", file);
        Assert.Equal(200, first);
        var (second, error) = await entry.Upload(path, "evidence-copy.pdf", file);
        Assert.Equal((409, "duplicate_file"), (second, error.Code()));
        var (other, _) = await entry.Upload(path, "other.pdf", Env.UniquePdf());
        Assert.Equal(200, other);

        await entry.Ok("POST", $"/api/documents/{doc!["id"].Str()}/void", Scenario.O(("note", "رفع بالخطأ للاختبار")));
        var (again, _) = await entry.Upload(path, "evidence.pdf", file);
        Assert.Equal(200, again);
    }
}
