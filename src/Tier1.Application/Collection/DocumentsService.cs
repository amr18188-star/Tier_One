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

/// <summary>
/// المستندات: أدلة قيم المؤشرات (في نموذج الجهة)، ومستندات هيكل الكلية (الهيكل المعتمد وقرارات التشكيل).
/// لا حذف نهائيًا: الإبطال بسبب يبقي المستند في السجل، والتنزيل بصلاحية الاطلاع على مالكه.
/// </summary>
public sealed class DocumentsService(IAppDb db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, IFileStore files, FormsService forms)
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
        return FormsService.ToDoc(doc);
    }

    public async Task<IReadOnlyList<DocDto>> StructureDocsAsync(Guid collegeId, CancellationToken ct)
    {
        var me = await access.RequireAsync(Features.Structure, Cap.View, ct);
        var college = await db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Id == collegeId, ct) ?? throw DomainException.NotFound();
        UnitScope.For(me, Features.Structure, Cap.View).Require(college);
        return await db.Documents.AsNoTracking().Where(d => d.OwnerType == StoredDocument.StructureOwner && d.OwnerId == collegeId)
            .OrderByDescending(d => d.UploadedAt).Select(d => FormsService.ToDoc(d)).ToListAsync(ct);
    }

    public async Task<DocDto> UploadStructureAsync(Guid collegeId, UploadedFile file, CancellationToken ct)
    {
        var me = await access.RequireAsync(Features.Structure, Cap.Add, ct);
        var college = await db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Id == collegeId && (u.Kind == OrgUnitKind.College || u.Kind == OrgUnitKind.Institute), ct)
                      ?? throw DomainException.NotFound();
        UnitScope.For(me, Features.Structure, Cap.Add).Require(college);
        var doc = await StoreAsync(StoredDocument.StructureOwner, college.Id, file, ct);
        audit.Record(AuditEvents.Audit("org", Features.Structure, "org.document_uploaded", "رفع مستند هيكل كلية", "Uploaded a structure document") with
        {
            Target = new AuditTarget("document", doc.Id.ToString(), doc.FileName, college.Code),
        });
        await db.SaveChangesAsync(ct);
        return FormsService.ToDoc(doc);
    }

    public async Task<OpenedFile> OpenAsync(Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound();
        var unitCode = await AuthorizeAsync(doc, Cap.View, ct);
        await audit.RecordNowAsync(AuditEvents.Access("dc", doc.OwnerType == StoredDocument.ValueOwner ? Features.Forms : Features.Structure,
            "dc.document_downloaded", "تنزيل مستند", "Downloaded a document") with
        {
            Target = new AuditTarget("document", doc.Id.ToString(), doc.FileName, unitCode),
        }, ct);
        return new OpenedFile(files.OpenRead(doc.StorageKey), doc.ContentType, doc.FileName);
    }

    public async Task<DocDto> VoidAsync(Guid id, string? reason, CancellationToken ct)
    {
        var why = Require.Reason(reason);
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound();
        if (doc.VoidedAt is not null) throw new DomainException("already_voided", "المستند مبطل بالفعل.", "Already voided.");
        var unitCode = await AuthorizeAsync(doc, doc.OwnerType == StoredDocument.ValueOwner ? Cap.Edit : Cap.Delete, ct);
        if (doc.OwnerType == StoredDocument.ValueOwner)
        {
            var sub = await (from v in db.SubmittedValues join s in db.Submissions on v.SubmissionId equals s.Id where v.Id == doc.OwnerId select s).AsNoTracking().FirstAsync(ct);
            if (!sub.IsEditable) throw new DomainException("form_locked", "النموذج مقفل؛ لا يبطل دليل قيمة أرسلت أو اعتمدت.", "The form is locked.", 409);
        }
        doc.VoidedAt = clock.UtcNow;
        doc.VoidReason = why;
        audit.Record(AuditEvents.Audit("dc", Features.Forms, "dc.document_voided", "إبطال مستند", "Voided a document") with
        {
            Target = new AuditTarget("document", doc.Id.ToString(), doc.FileName, unitCode),
            Reason = why,
        });
        await db.SaveChangesAsync(ct);
        return FormsService.ToDoc(doc);
    }

    /// <summary>صلاحية المستند من صلاحية مالكه: نموذج الجهة أو هيكل الكلية، في نطاق المستخدم.</summary>
    private async Task<string> AuthorizeAsync(StoredDocument doc, Cap cap, CancellationToken ct)
    {
        if (doc.OwnerType == StoredDocument.ValueOwner)
        {
            var unit = await (from v in db.SubmittedValues
                              join s in db.Submissions on v.SubmissionId equals s.Id
                              join u in db.OrgUnits on s.UnitId equals u.Id
                              where v.Id == doc.OwnerId
                              select u).AsNoTracking().FirstAsync(ct);
            var me = await access.RequireAsync(Features.Forms, cap, ct);
            UnitScope.For(me, Features.Forms, cap).Require(unit);
            return unit.Code;
        }
        var college = await db.OrgUnits.AsNoTracking().FirstAsync(u => u.Id == doc.OwnerId, ct);
        var access2 = await access.RequireAsync(Features.Structure, cap, ct);
        UnitScope.For(access2, Features.Structure, cap).Require(college);
        return college.Code;
    }

    private async Task<StoredDocument> StoreAsync(string ownerType, Guid ownerId, UploadedFile file, CancellationToken ct)
    {
        if (file.Length <= 0) throw new DomainException("empty_file", "الملف فارغ.", "The file is empty.", 400);
        if (file.Length > files.MaxBytes)
            throw new DomainException("file_too_large", $"الملف أكبر من {files.MaxBytes / 1024 / 1024} ميجابايت.", "The file is too large.", 413);
        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var name = FileRules.CleanName(file.FileName);
        var type = FileRules.ContentTypeFor(name, bytes.AsSpan(0, Math.Min(bytes.Length, 16)))
                   ?? throw new DomainException("file_type", $"نوع الملف غير مقبول. المقبول: {FileRules.Accepted}.", "File type not accepted.", 400);
        // المفتاح بصيغة مخزن الملفات: النوع/السنة/معرّف، بلا اسم الملف ولا امتداده (النوع محفوظ في السجل).
        var key = $"{ownerType}/{clock.UtcNow:yyyy}/{Guid.CreateVersion7():N}";
        buffer.Position = 0;
        await files.SaveAsync(key, buffer, ct);
        var doc = new StoredDocument
        {
            OwnerType = ownerType, OwnerId = ownerId, FileName = name, ContentType = type, SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), StorageKey = key, Description = Require.Optional(file.Description, 300),
            UploadedBy = current.UserId!.Value, UploadedAt = clock.UtcNow,
        };
        db.Documents.Add(doc);
        return doc;
    }
}
