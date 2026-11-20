using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Collection;

/// <summary>ARCH-1: value entry in a unit form (save, clear, the Excel template and import), each change audited.</summary>
public sealed partial class FormEntryService(ICollectionStore db, IClock clock, ICurrentUser current, IAuditLog audit, Settings.SettingsService settings, FormReader reader)
{
    private const string Feature = Features.Forms;

    public async Task<FormItem> SaveValueAsync(Guid id, SaveValueRequest req, CancellationToken ct)
    {
        var ctx = await reader.LoadAsync(id, Cap.Edit, tracking: true, ct);
        FormReader.EnsureEditable(ctx);
        var ci = ctx.Indicators.FirstOrDefault(i => i.Ci.IndicatorId == req.IndicatorId)
                 ?? throw new DomainException("not_in_form", "هذا المؤشر ليس مطلوبًا من هذه الجهة.", "This indicator is not required from this unit.", 400) { Field = "indicatorId" };
        var spec = CycleSpec.Of(ci.Version);
        var bigChange = await settings.IntAsync(SettingKeys.BigChangePercent, ct) / 100m;
        var (value, issues) = ValueRules.Check(spec, new ValueInput(req.Kind, req.Value, req.Numerator, req.Denominator, req.Reason, req.Explanation),
            ctx.Previous.GetValueOrDefault(req.IndicatorId), bigChange);
        if (issues.FirstOrDefault(i => i.Severity == IssueSeverity.Error) is { } error)
            throw new DomainException(error.Code, error.MessageAr, error.MessageAr, 400);

        var row = Upsert(ctx, ci, spec, req, value, "dc.value_saved");
        await SaveValuesAsync(ctx, ct);
        ctx.Values[req.IndicatorId] = row;
        return await reader.ItemAsync(ctx, ci, bigChange, ct);
    }

    /// <summary>
    /// يحفظ القيم المكتوبة بـ Upsert ويلمس النموذج (آخر تعديل، ومن «لم يبدأ» إلى «قيد الإدخال») في معاملة واحدة.
    /// النموذج يحدث بعبارة شرطية لا برمز الإصدار: الحفظ التلقائي يرسل صفوف المؤشرات متوازية، فكان الحفظ الثاني في النموذج نفسه
    /// يفشل بتعارض زائف (409) ولا تحفظ قيمته؛ الآن ينتظر قفل الصف. والشرط يرفض الكتابة إن أرسل النموذج أو قفل بعد تحميله.
    /// </summary>
    private async Task SaveValuesAsync(FormContext ctx, CancellationToken ct)
    {
        var id = ctx.Submission.Id;
        Guid? me = current.UserId!.Value;
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var touched = await db.Submissions
            .Where(s => s.Id == id && s.Step == ApprovalStep.Entry
                        && (s.Status == SubmissionStatus.NotStarted || s.Status == SubmissionStatus.InProgress || s.Status == SubmissionStatus.Returned))
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.LastEditedBy, me)
                .SetProperty(s => s.LastEditedAt, now)
                .SetProperty(s => s.Status, s => s.Status == SubmissionStatus.NotStarted ? SubmissionStatus.InProgress : s.Status), ct);
        if (touched == 0)
            throw new DomainException("form_locked", "أرسل النموذج أو قفل أثناء الحفظ؛ حدّث الصفحة.", "The form was submitted or locked while saving; refresh the page.", 409);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>كتابة قيمة فحصت مسبقًا في سطرها (جديد أو تعديل بإصدار)، مع سجل التدقيق بالتغييرات؛ الحفظ على المستدعي.</summary>
    private SubmittedValue Upsert(FormContext ctx, IndicatorInCycle ci, ValueSpec spec, SaveValueRequest req, decimal? value, string verb)
    {
        var now = clock.UtcNow;
        var row = ctx.Values.GetValueOrDefault(req.IndicatorId);
        var created = row is null;
        if (row is null)
        {
            row = new SubmittedValue { SubmissionId = ctx.Submission.Id, IndicatorId = req.IndicatorId };
            db.SubmittedValues.Add(row);
        }
        else
        {
            Concurrency.ExpectUpsert(db, row, req.Version);
        }
        var changes = new ChangeSet().Add("kind", created ? null : row.Kind.ToString(), req.Kind.ToString()).Add("value", row.Value, value)
            .Add("numerator", row.Numerator, req.Numerator).Add("denominator", row.Denominator, req.Denominator);
        row.Kind = req.Kind;
        row.Value = req.Kind == ValueKind.Number ? value : null;
        row.Numerator = req.Kind == ValueKind.Number && ValueRules.UsesRatio(spec) ? req.Numerator : null;
        row.Denominator = req.Kind == ValueKind.Number && (ValueRules.UsesRatio(spec) || spec.Aggregation == Aggregation.WeightedAverage) ? req.Denominator : null;
        row.Reason = req.Kind == ValueKind.Number ? null : Require.Optional(req.Reason, 500);
        row.Explanation = Require.Optional(req.Explanation, 1000);
        row.ReviewNote = null;
        row.EditedBy = current.UserId!.Value;
        row.EditedAt = now;
        // آخر تعديل وحالة «قيد الإدخال» على النموذج يكتبهما SaveValuesAsync بعبارة شرطية، لا هنا.

        var imported = verb == "dc.value_imported";
        audit.Record(AuditEvents.Audit("dc", Feature, verb, imported ? "استيراد قيمة مؤشر من Excel" : "حفظ قيمة مؤشر في نموذج جهة",
            imported ? "Imported an indicator value" : "Saved an indicator value") with
        {
            Target = new AuditTarget("submitted_value", row.Id.ToString(), ci.Ci.Code, ctx.Unit.Code),
            Related = [new AuditTarget("submission", ctx.Submission.Id.ToString(), ctx.Unit.Code)],
            Changes = changes.ToList(),
        });
        return row;
    }

    /// <summary>مسح قيمة لم ترسل بعد (الإدخال الخاطئ قبل الإرسال)؛ لا يمس ما أرسل أو اعتمد.</summary>
    public async Task ClearValueAsync(Guid id, Guid indicatorId, CancellationToken ct)
    {
        var ctx = await reader.LoadAsync(id, Cap.Edit, tracking: true, ct);
        FormReader.EnsureEditable(ctx);
        if (!ctx.Values.TryGetValue(indicatorId, out var row)) return;
        if (await db.Documents.AnyAsync(d => d.OwnerType == StoredDocument.ValueOwner && d.OwnerId == row.Id && d.VoidedAt == null, ct))
            throw new DomainException("has_evidence", "أبطل الأدلة المرفقة بهذه القيمة أولًا.", "Void the attached evidence first.");
        db.SubmittedValues.Remove(row);
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.value_cleared", "مسح قيمة غير مرسلة", "Cleared an unsent value") with
        {
            Target = new AuditTarget("submission", ctx.Submission.Id.ToString(), ctx.Unit.Code),
        });
        await db.SaveChangesAsync(ct);
    }
}
