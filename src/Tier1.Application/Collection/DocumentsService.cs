using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;

namespace Tier1.Application.Collection;

/// <summary>
/// قبول الملفات: النوع من توقيع المحتوى لا من الامتداد وحده، والاسم المعروض منظف، والتخزين بمعرّف لا باسم الملف.
/// </summary>
public static class FileRules
{
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    private static readonly Dictionary<string, (string ContentType, byte[] Magic)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", "%PDF-"u8.ToArray()),
        [".png"] = ("image/png", [0x89, 0x50, 0x4E, 0x47]),
        [".jpg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        [".jpeg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", Zip),
        [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Zip),
        [".pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", Zip),
    };

    public static string Accepted => "PDF وصور PNG/JPG وملفات Word وExcel وPowerPoint";

    public static string? ContentTypeFor(string fileName, ReadOnlySpan<byte> head)
    {
        var ext = Path.GetExtension(fileName ?? "");
        if (!Allowed.TryGetValue(ext, out var a)) return null;
        return head.Length >= a.Magic.Length && head[..a.Magic.Length].SequenceEqual(a.Magic) ? a.ContentType : null;
    }

    public static string CleanName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? "file");
        var clean = new string(name.Where(c => !char.IsControl(c) && c is not ('/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')).ToArray()).Trim();
        if (clean.Length == 0) clean = "file" + Path.GetExtension(name);
        return clean.Length <= 200 ? clean : clean[..(200 - Path.GetExtension(clean).Length)] + Path.GetExtension(clean);
    }
}

public sealed record UploadedFile(string FileName, long Length, Stream Content, string? Description);

public sealed record OpenedFile(Stream Content, string ContentType, string FileName);

public sealed record PlanDocDto(DocDto Document, string? UploadedBy);

/// <summary>
/// المستندات: أدلة قيم المؤشرات (في نموذج الجهة)، ومستندات هيكل الكلية (الهيكل المعتمد وقرارات التشكيل).
/// لا حذف نهائيًا: الإبطال بسبب يبقي المستند في السجل، والتنزيل بصلاحية الاطلاع على مالكه.
/// </summary>
public sealed class DocumentsService(ICollectionStore db, IReadModel read, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, IFileStore files, FormReader forms)
{
    public long MaxBytes => files.MaxBytes;

    public async Task<DocDto> UploadEvidenceAsync(Guid formId, Guid indicatorId, UploadedFile file, CancellationToken ct)
    {
        var ctx = await forms.LoadAsync(formId, Cap.Edit, tracking: false, ct);
        if (ctx.Cycle.Status != CycleStatus.Open || !ctx.Submission.IsEditable)
            throw new DomainException("form_locked", "النموذج مقفل؛ الأدلة ترفق أثناء الإدخال.", "The form is locked.", 409);
        var value = ctx.Values.GetValueOrDefault(indicatorId)
                    ?? throw new DomainException("value_first", "احفظ قيمة المؤشر أولًا ثم أرفق دليلها.", "Save the value first.", 400);
        var doc = await StoreAsync(StoredDocument.ValueOwner, value.Id, file, ct);
        audit.Record(AuditEvents.Audit("dc", Features.Forms, "dc.evidence_uploaded", "إرفاق دليل لقيمة مؤشر", "Uploaded evidence") with
        {
            Target = new AuditTarget("document", doc.Id.ToString(), doc.FileName, ctx.Unit.Code),
            Related = [new AuditTarget("submission", formId.ToString(), ctx.Unit.Code)],
        });
        await db.SaveChangesAsync(ct);
        return FormReader.ToDoc(doc);
    }

    public async Task<IReadOnlyList<DocDto>> StructureDocsAsync(Guid collegeId, CancellationToken ct)
    {
        var me = await access.RequireAsync(Features.Structure, Cap.View, ct);
        var college = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == collegeId, ct) ?? throw DomainException.NotFound();
        UnitScope.For(me, Features.Structure, Cap.View).Require(college);
        return await db.Documents.AsNoTracking().Where(d => d.OwnerType == StoredDocument.StructureOwner && d.OwnerId == collegeId)
            .OrderByDescending(d => d.UploadedAt).Select(d => FormReader.ToDoc(d)).ToListAsync(ct);
    }

    public async Task<DocDto> UploadStructureAsync(Guid collegeId, UploadedFile file, CancellationToken ct)
    {
        var me = await access.RequireAsync(Features.Structure, Cap.Add, ct);
        var college = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == collegeId && (u.Kind == OrgUnitKind.College || u.Kind == OrgUnitKind.Institute), ct)
                      ?? throw DomainException.NotFound();
        UnitScope.For(me, Features.Structure, Cap.Add).Require(college);
        var doc = await StoreAsync(StoredDocument.StructureOwner, college.Id, file, ct);
        audit.Record(AuditEvents.Audit("org", Features.Structure, "org.document_uploaded", "رفع مستند هيكل كلية", "Uploaded a structure document") with
        {
            Target = new AuditTarget("document", doc.Id.ToString(), doc.FileName, college.Code),
        });
        await db.SaveChangesAsync(ct);
        return FormReader.ToDoc(doc);
    }

    /// <summary>وثيقة الخطة الاستراتيجية بإصداراتها، الأحدث أولًا؛ يطلع عليها كل من يرى الخطة.</summary>
    public async Task<IReadOnlyList<PlanDocDto>> PlanDocsAsync(CancellationToken ct)
    {
        var me = await access.RequireAsync(Features.Strategy, Cap.View, ct);
        var docs = await db.Documents.AsNoTracking().Where(d => d.OwnerType == StoredDocument.PlanOwner)
            .OrderByDescending(d => d.UploadedAt).ToListAsync(ct);
        // الإصدارات المبطلة لمن يدير الوثيقة فقط؛ غيره يرى الساري.
        var manager = me.Has(Features.Strategy, Cap.Delete);
        var users = docs.Select(d => d.UploadedBy).Distinct().ToList();
        var names = await read.Query<Domain.Identity.AppUser>().AsNoTracking().Where(u => users.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return docs.Where(d => manager || d.VoidedAt is null)
            .Select(d => new PlanDocDto(FormReader.ToDoc(d), names.GetValueOrDefault(d.UploadedBy))).ToList();
    }

    /// <summary>رفع إصدار من وثيقة الخطة (PDF فقط، لأنها تعرض في الصفحة). الإصدار الأحدث هو المعروض.</summary>
    public async Task<DocDto> UploadPlanAsync(UploadedFile file, CancellationToken ct)
    {
        await access.RequireAsync(Features.Strategy, Cap.Add, ct);
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("pdf_only", "وثيقة الخطة تعرض في المنصة، فترفع بصيغة PDF.", "The plan document must be a PDF.", 400) { Field = "file" };
        var root = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().FirstAsync(u => u.Kind == OrgUnitKind.University, ct);
        var doc = await StoreAsync(StoredDocument.PlanOwner, root.Id, file, ct, PlanMaxBytes);
        audit.Record(AuditEvents.Audit("kpi", Features.Strategy, "kpi.plan_document_uploaded", "رفع وثيقة الخطة الاستراتيجية", "Uploaded the strategic plan document") with
        {
            Target = new AuditTarget("document", doc.Id.ToString(), doc.FileName),
        });
        await db.SaveChangesAsync(ct);
        return FormReader.ToDoc(doc);
    }

    /// <summary>وثيقة الخطة قد تكون أكبر من المستندات المعتادة.</summary>
    public const long PlanMaxBytes = 60L * 1024 * 1024;

    public async Task<OpenedFile> OpenAsync(Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound();
        var unitCode = await AuthorizeAsync(doc, Cap.View, ct);
        await audit.RecordNowAsync(AuditEvents.Access("dc", doc.OwnerType switch
            {
                StoredDocument.ValueOwner => Features.Forms,
                StoredDocument.PlanOwner => Features.Strategy,
                _ => Features.Structure,
            },
            doc.OwnerType == StoredDocument.PlanOwner ? "kpi.plan_document_viewed" : "dc.document_downloaded",
            doc.OwnerType == StoredDocument.PlanOwner ? "اطلاع على وثيقة الخطة" : "تنزيل مستند", "Opened a document") with
        {
            Target = new AuditTarget("document", doc.Id.ToString(), doc.FileName, unitCode),
        }, ct);
        return new OpenedFile(files.OpenRead(doc.StorageKey), doc.ContentType, doc.FileName);
    }

    public async Task<DocDto> VoidAsync(Guid id, string? reason, CancellationToken ct)
    {
        var why = Require.Reason(reason, field: "note"); // The endpoint takes it as NoteRequest.Note.
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound();
        // G-16 (system integration review 2026-11-24): authorize first, so the document's state is told only to who may act on it.
        var unitCode = await AuthorizeAsync(doc, doc.OwnerType == StoredDocument.ValueOwner ? Cap.Edit : Cap.Delete, ct);
        if (doc.VoidedAt is not null) throw new DomainException("already_voided", "المستند مبطل بالفعل.", "Already voided.");
        if (doc.OwnerType == StoredDocument.PlanOwner)
            audit.Record(AuditEvents.Audit("kpi", Features.Strategy, "kpi.plan_document_voided", "إبطال إصدار من وثيقة الخطة", "Voided a plan document") with
            {
                Target = new AuditTarget("document", doc.Id.ToString(), doc.FileName),
                Reason = why,
            });
        if (doc.OwnerType == StoredDocument.ValueOwner)
        {
            var sub = await (from v in db.SubmittedValues join s in db.Submissions on v.SubmissionId equals s.Id where v.Id == doc.OwnerId select s).AsNoTracking().FirstAsync(ct);
            // G-13 (system integration review 2026-11-24): the evidence of a closed cycle stays as it was, like its values.
            var cycleOpen = await read.Query<CollectionCycle>().AsNoTracking().AnyAsync(c => c.Id == sub.CycleId && c.Status == CycleStatus.Open, ct);
            if (!cycleOpen) throw new DomainException("cycle_not_open", "الدورة غير مفتوحة؛ أدلتها تبقى كما هي.", "The cycle is not open.");
            if (!sub.IsEditable) throw new DomainException("form_locked", "النموذج مقفل؛ لا يبطل دليل قيمة أرسلت أو اعتمدت.", "The form is locked.", 409);
        }
        doc.VoidedAt = clock.UtcNow;
        doc.VoidReason = why;
        if (doc.OwnerType != StoredDocument.PlanOwner)
            audit.Record(AuditEvents.Audit("dc", Features.Forms, "dc.document_voided", "إبطال مستند", "Voided a document") with
            {
                Target = new AuditTarget("document", doc.Id.ToString(), doc.FileName, unitCode),
                Reason = why,
            });
        await db.SaveChangesAsync(ct);
        return FormReader.ToDoc(doc);
    }

    /// <summary>صلاحية المستند من صلاحية مالكه: نموذج الجهة أو هيكل الكلية، في نطاق المستخدم.</summary>
    private async Task<string> AuthorizeAsync(StoredDocument doc, Cap cap, CancellationToken ct)
    {
        if (doc.OwnerType == StoredDocument.ValueOwner)
        {
            var unit = await (from v in db.SubmittedValues
                              join s in db.Submissions on v.SubmissionId equals s.Id
                              join u in read.Query<Domain.Organization.OrgUnit>() on s.UnitId equals u.Id
                              where v.Id == doc.OwnerId
                              select u).AsNoTracking().FirstAsync(ct);
            var me = await access.RequireAsync(Features.Forms, cap, ct);
            UnitScope.For(me, Features.Forms, cap).Require(unit);
            return unit.Code;
        }
        if (doc.OwnerType == StoredDocument.PlanOwner)
        {
            // وثيقة الجامعة كلها: الاطلاع لكل من يرى الخطة بلا نطاق جهة، والمبطل لا يفتحه إلا من يديرها.
            var me = await access.RequireAsync(Features.Strategy, cap, ct);
            if (doc.VoidedAt is not null && !me.Has(Features.Strategy, Cap.Delete)) throw DomainException.NotFound();
            return ReferenceCode;
        }
        var college = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().FirstAsync(u => u.Id == doc.OwnerId, ct);
        var access2 = await access.RequireAsync(Features.Structure, cap, ct);
        UnitScope.For(access2, Features.Structure, cap).Require(college);
        return college.Code;
    }

    private const string ReferenceCode = "CU";

    private async Task<StoredDocument> StoreAsync(string ownerType, Guid ownerId, UploadedFile file, CancellationToken ct, long? maxBytes = null)
    {
        var max = maxBytes ?? files.MaxBytes;
        if (file.Length <= 0) throw new DomainException("empty_file", "الملف فارغ.", "The file is empty.", 400) { Field = "file" };
        if (file.Length > max)
            throw new DomainException("file_too_large", $"الملف أكبر من {max / 1024 / 1024} ميجابايت.", "The file is too large.", 413);
        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var name = FileRules.CleanName(file.FileName);
        var type = FileRules.ContentTypeFor(name, bytes.AsSpan(0, Math.Min(bytes.Length, 16)))
                   ?? throw new DomainException("file_type", $"نوع الملف غير مقبول. المقبول: {FileRules.Accepted}.", "File type not accepted.", 400) { Field = "file" };
        // BE-8: the same file attached twice to the same owner (a double click, or a second try after a slow upload) is refused,
        // so evidence is not duplicated and clearing the value is not blocked by a copy nobody meant to keep.
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (await db.Documents.AnyAsync(d => d.OwnerType == ownerType && d.OwnerId == ownerId && d.Sha256 == sha256 && d.VoidedAt == null, ct))
            throw new DomainException("duplicate_file", "هذا الملف مرفق بالفعل.", "This file is already attached.", 409);
        // المفتاح بصيغة مخزن الملفات: النوع/السنة/معرّف، بلا اسم الملف ولا امتداده (النوع محفوظ في السجل).
        var key = $"{ownerType}/{clock.UtcNow:yyyy}/{Guid.CreateVersion7():N}";
        buffer.Position = 0;
        await files.SaveAsync(key, buffer, ct);
        var doc = new StoredDocument
        {
            OwnerType = ownerType, OwnerId = ownerId, FileName = name, ContentType = type, SizeBytes = bytes.Length,
            Sha256 = sha256, StorageKey = key, Description = Require.Optional(file.Description, 300),
            UploadedBy = current.UserId!.Value, UploadedAt = clock.UtcNow,
        };
        db.Documents.Add(doc);
        return doc;
    }
}
