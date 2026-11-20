using Microsoft.AspNetCore.Http.Features;
using Tier1.Api.Security;
using Tier1.Api.Startup;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Collection;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;

namespace Tier1.Api.Endpoints;

/// <summary>جمع البيانات: الدورات، ونماذج الجهات ومسار اعتمادها، والأدلة، والاستيراد من Excel، والنتائج.</summary>
public static class CollectionEndpoints
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void MapCollection(this RouteGroupBuilder api)
    {
        MapCycles(api.MapGroup("/cycles"));
        MapForms(api.MapGroup("/forms"));
        MapDocuments(api);
        MapResults(api.MapGroup("/results"));
        MapScorecard(api);
    }

    private static void MapCycles(RouteGroupBuilder g)
    {
        g.MapGet("/", async (CyclesService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(ct)))
            .RequireAnyPermission($"{Features.Cycles}:V", $"{Features.Forms}:V", $"{Features.Results}:V");
        g.MapPost("/", async (SaveCycleRequest req, CyclesService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateAsync(req, ct)))
            .RequirePermission(Features.Cycles, Cap.Add);
        g.MapPut("/{id:guid}", async (Guid id, SaveCycleRequest req, CyclesService svc, CancellationToken ct) => TypedResults.Ok(await svc.UpdateAsync(id, req, ct)))
            .RequirePermission(Features.Cycles, Cap.Edit);
        g.MapGet("/{id:guid}/preview", async (Guid id, CyclesService svc, CancellationToken ct) => TypedResults.Ok(await svc.PreviewOpenAsync(id, ct)))
            .RequirePermission(Features.Cycles, Cap.Edit);
        g.MapPost("/{id:guid}/open", async (Guid id, CyclesService svc, CancellationToken ct) => TypedResults.Ok(await svc.OpenAsync(id, ct)))
            .RequirePermission(Features.Cycles, Cap.Edit);
        g.MapPost("/{id:guid}/close", async (Guid id, NoteRequest req, CyclesService svc, CancellationToken ct) => TypedResults.Ok(await svc.CloseAsync(id, req.Note, ct)))
            .RequirePermission(Features.Cycles, Cap.Delete);
    }

    private static void MapForms(RouteGroupBuilder g)
    {
        g.MapGet("/", async (Guid? cycleId, bool? awaitingMe, SubmissionStatus? status, string? q, FormsService svc, CancellationToken ct) =>
                TypedResults.Ok(await svc.ListAsync(cycleId, awaitingMe ?? false, status, q, ct)))
            .RequirePermission(Features.Forms, Cap.View);
        g.MapGet("/{id:guid}", async (Guid id, FormsService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(id, ct)))
            .RequirePermission(Features.Forms, Cap.View);
        g.MapPut("/{id:guid}/values", async (Guid id, SaveValueRequest req, FormEntryService svc, CancellationToken ct) => TypedResults.Ok(await svc.SaveValueAsync(id, req, ct)))
            .RequirePermission(Features.Forms, Cap.Edit);
        g.MapDelete("/{id:guid}/values/{indicatorId:guid}", async (Guid id, Guid indicatorId, FormEntryService svc, CancellationToken ct) =>
            {
                await svc.ClearValueAsync(id, indicatorId, ct);
                return Results.NoContent();
            })
            .RequirePermission(Features.Forms, Cap.Edit);
        // The body is optional on the wire: a submit without one is refused by the service (409 if locked, else 428), not by routing.
        g.MapPost("/{id:guid}/submit", async (Guid id, HttpRequest http, FormWorkflowService svc, CancellationToken ct) =>
                TypedResults.Ok(await svc.SubmitAsync(id, (await OptionalBodyAsync<FormTransitionRequest>(http, ct))?.Version, ct)))
            .RequirePermission(Features.Forms, Cap.Edit);
        // المراجعة والاعتماد: الخدمة تفحص الإجراء الخاص لخطوة النموذج الحالية وفصل المهام.
        g.MapPost("/{id:guid}/approve", async (Guid id, ApproveFormRequest req, FormWorkflowService svc, CancellationToken ct) => TypedResults.Ok(await svc.ApproveAsync(id, req, ct)))
            .RequireAnyPermission([.. ReviewSpecials, $"{Features.Forms}:{ApprovalRules.ApproveDepartmentDirectly}", $"{Features.Forms}:{ApprovalRules.ApproveAsDeanDirectly}"]);
        g.MapPost("/{id:guid}/return", async (Guid id, ReturnRequest req, FormWorkflowService svc, CancellationToken ct) => TypedResults.Ok(await svc.ReturnAsync(id, req, ct)))
            .RequireAnyPermission(ReviewSpecials);
        // إعادة فتح المعتمد بطلب وقرار (D4): الطلب ممن يملك «إعادة الفتح»، والقرار للجنة العليا برقم المحضر.
        g.MapPost("/{id:guid}/reopen", async (Guid id, FormTransitionRequest req, FormWorkflowService svc, CancellationToken ct) => TypedResults.Ok(await svc.RequestReopenAsync(id, req, ct)))
            .RequireSpecial(Features.Cycles, ApprovalRules.Reopen);
        g.MapPost("/{id:guid}/reopen/approve", async (Guid id, Tier1.Application.Kpi.CommitteeDecisionRequest req, FormWorkflowService svc, CancellationToken ct) => TypedResults.Ok(await svc.ApproveReopenAsync(id, req, ct)))
            .RequireSpecial(Features.Cycles, ApprovalRules.ApproveReopen);
        g.MapPost("/{id:guid}/reopen/reject", async (Guid id, NoteRequest req, FormWorkflowService svc, CancellationToken ct) => TypedResults.Ok(await svc.RejectReopenAsync(id, req, ct)))
            .RequireSpecial(Features.Cycles, ApprovalRules.ApproveReopen);

        g.MapPost("/{id:guid}/extend", async (Guid id, ExtendRequest req, FormWorkflowService svc, CancellationToken ct) => TypedResults.Ok(await svc.ExtendAsync(id, req, ct)))
            .RequirePermission(Features.Cycles, Cap.Edit);

        g.MapGet("/{id:guid}/template.xlsx", async (Guid id, FormEntryService svc, ISpreadsheets sheets, CancellationToken ct) =>
            {
                var (content, name) = await svc.TemplateAsync(id, sheets, ct);
                return Results.File(content, Xlsx, name);
            })
            .RequirePermission(Features.Forms, Cap.View);
        g.MapPost("/{id:guid}/import", async (Guid id, bool? apply, HttpRequest request, FormEntryService svc, ISpreadsheets sheets, CancellationToken ct) =>
            {
                const long max = 5 * 1024 * 1024;
                if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = max + 64 * 1024;
                var file = await SingleFileAsync(request, max, ct);
                await using var stream = file.OpenReadStream();
                return TypedResults.Ok(await svc.ImportAsync(id, stream, sheets, apply ?? false, ct));
            })
            // SEC-1: reading a workbook is heavy; repeated uploads are limited like the other sensitive actions.
            .RequirePermission(Features.Forms, Cap.Edit).RequireRateLimiting(ApiSetup.SensitiveLimit);
    }

    private static readonly string[] ReviewSpecials =
    [
        $"{Features.Forms}:{ApprovalRules.ReviewDepartment}", $"{Features.Forms}:{ApprovalRules.ReviewCollege}",
        $"{Features.Forms}:{ApprovalRules.ApproveCollege}", $"{Features.Forms}:{ApprovalRules.ApproveFinal}",
    ];

    private static void MapDocuments(RouteGroupBuilder api)
    {
        api.MapPost("/forms/{id:guid}/values/{indicatorId:guid}/evidence", async (Guid id, Guid indicatorId, HttpRequest request, DocumentsService svc, CancellationToken ct) =>
            {
                var (file, description) = await UploadAsync(request, svc.MaxBytes, ct);
                await using var stream = file.OpenReadStream();
                return TypedResults.Ok(await svc.UploadEvidenceAsync(id, indicatorId, new UploadedFile(file.FileName, file.Length, stream, description), ct));
            })
            .RequirePermission(Features.Forms, Cap.Edit);

        api.MapGet("/structure/colleges/{id:guid}/documents", async (Guid id, DocumentsService svc, CancellationToken ct) => TypedResults.Ok(await svc.StructureDocsAsync(id, ct)))
            .RequirePermission(Features.Structure, Cap.View);
        api.MapPost("/structure/colleges/{id:guid}/documents", async (Guid id, HttpRequest request, DocumentsService svc, CancellationToken ct) =>
            {
                var (file, description) = await UploadAsync(request, svc.MaxBytes, ct);
                await using var stream = file.OpenReadStream();
                return TypedResults.Ok(await svc.UploadStructureAsync(id, new UploadedFile(file.FileName, file.Length, stream, description), ct));
            })
            .RequirePermission(Features.Structure, Cap.Add);

        // وثيقة الخطة الاستراتيجية: يطلع عليها كل من يرى الخطة، ويرفع إصداراتها من يضيف إلى الخطة.
        api.MapGet("/strategy/documents", async (DocumentsService svc, CancellationToken ct) => TypedResults.Ok(await svc.PlanDocsAsync(ct)))
            .RequirePermission(Features.Strategy, Cap.View);
        api.MapPost("/strategy/documents", async (HttpRequest request, DocumentsService svc, CancellationToken ct) =>
            {
                var (file, description) = await UploadAsync(request, DocumentsService.PlanMaxBytes, ct);
                await using var stream = file.OpenReadStream();
                return TypedResults.Ok(await svc.UploadPlanAsync(new UploadedFile(file.FileName, file.Length, stream, description), ct));
            })
            .RequirePermission(Features.Strategy, Cap.Add);
        // العرض داخل صفحة الخطة: الإطار مسموح من المنظومة نفسها فقط لهذا الرد.
        api.MapGet("/strategy/documents/{id:guid}/view", async (Guid id, HttpContext http, DocumentsService svc, CancellationToken ct) =>
            {
                var file = await svc.OpenAsync(id, ct);
                if (file.ContentType != "application/pdf") return Results.NotFound();
                http.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                http.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self'";
                http.Response.Headers.ContentDisposition = "inline";
                return Results.Stream(file.Content, file.ContentType);
            })
            .RequirePermission(Features.Strategy, Cap.View);

        // التنزيل والإبطال بصلاحية مالك المستند (نموذج الجهة أو هيكل الكلية أو الخطة)، تفحصها الخدمة.
        api.MapGet("/documents/{id:guid}", async (Guid id, bool? inline, DocumentsService svc, CancellationToken ct) =>
            {
                var file = await svc.OpenAsync(id, ct);
                var show = inline == true && file.ContentType is "application/pdf" or "image/png" or "image/jpeg";
                return show ? Results.Stream(file.Content, file.ContentType) : Results.File(file.Content, file.ContentType, file.FileName);
            })
            .RequireAnyPermission($"{Features.Forms}:V", $"{Features.Structure}:V", $"{Features.Strategy}:V");
        api.MapPost("/documents/{id:guid}/void", async (Guid id, NoteRequest req, DocumentsService svc, CancellationToken ct) => TypedResults.Ok(await svc.VoidAsync(id, req.Note, ct)))
            .RequireAnyPermission($"{Features.Forms}:E", $"{Features.Structure}:D", $"{Features.Strategy}:D");
    }

    private static void MapResults(RouteGroupBuilder g)
    {
        g.MapGet("/", async (Guid? cycleId, Guid? unitId, ResultsService svc, CancellationToken ct) =>
                await svc.OverviewAsync(cycleId, unitId, ct) is { } r ? TypedResults.Ok(r) : Results.NoContent())
            .RequirePermission(Features.Results, Cap.View);
        g.MapGet("/{cycleId:guid}/indicators/{indicatorId:guid}", async (Guid cycleId, Guid indicatorId, Guid? unitId, ResultsService svc, CancellationToken ct) =>
                TypedResults.Ok(await svc.IndicatorAsync(cycleId, indicatorId, unitId, ct)))
            .RequirePermission(Features.Results, Cap.View);
        g.MapGet("/{cycleId:guid}/export.xlsx", async (Guid cycleId, Guid? unitId, ResultsService svc, ISpreadsheets sheets, CancellationToken ct) =>
            {
                var (content, name) = await svc.ExportAsync(cycleId, unitId, sheets, ct);
                return Results.File(content, Xlsx, name);
            })
            .RequireSpecial(Features.Results, "export").RequireRateLimiting(ApiSetup.SensitiveLimit);
    }

    private static async Task<(IFormFile File, string? Description)> UploadAsync(HttpRequest request, long maxBytes, CancellationToken ct)
    {
        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = maxBytes + 64 * 1024;
        var file = await SingleFileAsync(request, maxBytes, ct);
        var form = await request.ReadFormAsync(ct);
        return (file, form["description"].FirstOrDefault());
    }

    /// <summary>ملف واحد من نموذج multipart بحد حجم؛ يرفض ما عدا ذلك برسالة واضحة.</summary>
    private static async Task<IFormFile> SingleFileAsync(HttpRequest request, long maxBytes, CancellationToken ct)
    {
        if (!request.HasFormContentType) throw new DomainException("file_required", "أرفق ملفًا.", "Attach a file.", 400) { Field = "file" };
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.Count == 1 ? form.Files[0] : throw new DomainException("file_required", "أرفق ملفًا واحدًا.", "Attach exactly one file.", 400) { Field = "file" };
        if (file.Length > maxBytes) throw new DomainException("file_too_large", $"الملف أكبر من {maxBytes / 1024 / 1024} ميجابايت.", $"The file is larger than {maxBytes / 1024 / 1024} MB.", 413);
        return file;
    }

    /// <summary>بطاقة أداء الجامعة وفئتها، وتقويم الأرباع، وملف KPI.xlsx معبأ بقيم الأرباع لبوابة المجلس.</summary>
    private static void MapScorecard(RouteGroupBuilder api)
    {
        api.MapGet("/scorecard", async (Tier1.Application.Scorecard.ScorecardService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(ct)))
            .RequirePermission(Features.Results, Cap.View);
        api.MapGet("/scorecard/kpi.xlsx", async (Tier1.Application.Scorecard.ScorecardService svc, IKpiWorkbook workbook, CancellationToken ct) =>
            {
                var (rows, label) = await svc.ExportValuesAsync(ct);
                return Results.File(workbook.Fill(rows), Xlsx, $"KPI-{label}.xlsx");
            })
            // ARCH-5: the endpoint declares what the service requires (the export special), as the results export does.
            .RequireSpecial(Features.Results, "export").RequireRateLimiting(ApiSetup.SensitiveLimit);
        // مراجعة مسئول ملف Tier 1 ونقل القيم إلى مؤشرات الجامعة.
        api.MapGet("/review", async (Guid? cycleId, Tier1.Application.Scorecard.ReviewService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(cycleId, ct)))
            .RequirePermission(Features.University, Cap.View);
        api.MapPost("/review/transfer", async (Tier1.Application.Scorecard.TransferRequest req, Tier1.Application.Scorecard.ReviewService svc, CancellationToken ct) =>
                TypedResults.Ok(await svc.TransferAsync(req, ct)))
            .RequirePermission(Features.University, Cap.Edit);
        api.MapPost("/review/transfer-all", async (Tier1.Application.Scorecard.TransferAllRequest req, Tier1.Application.Scorecard.ReviewService svc, CancellationToken ct) =>
                TypedResults.Ok(new { transferred = await svc.TransferAllAsync(req, ct) }))
            .RequirePermission(Features.University, Cap.Edit);
        // تقدم الكلية مقابل مستهدف الجامعة، في نطاق المستخدم.
        api.MapGet("/progress", async (Guid? unitId, Tier1.Application.Scorecard.ProgressService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(unitId, ct)))
            .RequirePermission(Features.Results, Cap.View);
        // تقويم الأرباع يلزم من ينشئ الدورات ومن يطالع النتائج والنماذج.
        api.MapGet("/plan/calendar", async (Tier1.Application.Scorecard.ScorecardService svc, CancellationToken ct) => TypedResults.Ok(await svc.CalendarAsync(ct)))
            .RequireAnyPermission($"{Features.Cycles}:V", $"{Features.Forms}:V", $"{Features.Results}:V");
    }

    /// <summary>A JSON body when one was sent, else null (a body-less POST would otherwise miss the route and fall to the 404 fallback).</summary>
    private static async Task<T?> OptionalBodyAsync<T>(HttpRequest http, CancellationToken ct) where T : class
    {
        if (http.ContentLength is null or 0 && !http.Headers.ContainsKey("Transfer-Encoding")) return null;
        try { return await http.ReadFromJsonAsync<T>(ct); }
        catch (System.Text.Json.JsonException) { throw new DomainException("invalid_body", "صيغة الطلب غير صالحة.", "The request body is not valid JSON.", 400); }
        catch (InvalidOperationException) { throw new DomainException("json_required", "صيغة الطلب غير صالحة.", "The request body must be JSON.", 415); }
    }
}
