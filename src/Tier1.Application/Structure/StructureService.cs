using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Common;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;
using Tier1.Domain.Structure;

namespace Tier1.Application.Structure;

public sealed record PositionDto(Guid Id, string TitleAr, bool Single, bool Required, string? SuggestedRoleCode);

public sealed record TemplateUnitDto(Guid Id, string Code, string NameAr, string RoleAr, IReadOnlyList<string> Duties, bool IsHead, bool PerDepartment, IReadOnlyList<PositionDto> Positions);

public sealed record MemberDto(
    Guid Id, Guid PositionId, Guid? DepartmentId, string FullName, string? Title, string? Employer,
    string? Email, string? Phone, DateOnly StartDate, DateOnly? EndDate, string? DecisionRef, string? Notes, uint Version);

public sealed record PartnerDto(
    Guid Id, Guid DepartmentId, string NameAr, string? Sector, string? CoordinatorName, string? CoordinatorEmail,
    string? CoordinatorPhone, DateOnly? AgreementDate, string? AgreementRef, DateOnly? EndedOn, uint Version);

public sealed record DepartmentDto(Guid Id, string Code, string NameAr);

public sealed record CollegeChartDto(
    Guid CollegeId, string Code, string NameAr, DateOnly AsOf, bool CanEdit, bool ContactsVisible,
    IReadOnlyList<TemplateUnitDto> Units, IReadOnlyList<MemberDto> Members, IReadOnlyList<DepartmentDto> Departments,
    IReadOnlyList<PartnerDto> Partners, Completeness Completeness);

public sealed record CollegeCompletenessDto(Guid CollegeId, string Code, string NameAr, int Percent, int IssueCount, string? FirstIssue);

public sealed record SaveMemberRequest(
    Guid CollegeId, Guid? DepartmentId, Guid PositionId, string FullName, string? Title, string? Employer, string? Email,
    string? Phone, DateOnly StartDate, string? DecisionRef, string? Notes, bool ReplaceCurrent, uint? Version);

public sealed record EndRequest(DateOnly EndDate, string? Reason);

public sealed record SavePartnerRequest(
    Guid DepartmentId, string NameAr, string? Sector, string? CoordinatorName, string? CoordinatorEmail, string? CoordinatorPhone,
    DateOnly? AgreementDate, string? AgreementRef, uint? Version);

/// <summary>
/// هيكل Tier 1 بالكليات: الرسم التنظيمي بأعضائه كما هو في أي تاريخ، والعضويات بتاريخ بداية ونهاية (لا حذف)،
/// والشريك الصناعي لكل قسم، ولوحة الاكتمال. كل ذلك في نطاق المستخدم على الكليات.
/// </summary>
public sealed class StructureService(IStructureStore db, IReadModel read,  IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit)
{
    private const string Feature = Features.Structure;

    public async Task<IReadOnlyList<TemplateUnitDto>> TemplateAsync(CancellationToken ct)
    {
        var units = await LoadTemplateAsync(ct);
        return units.Select(ToDto).ToList();
    }

    public async Task<CollegeChartDto> ChartAsync(Guid collegeId, DateOnly? asOf, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var college = await LoadCollegeAsync(collegeId, ct);
        UnitScope.For(me, Feature, Cap.View).Require(college);

        var day = asOf ?? CairoTime.DateOf(clock.UtcNow);
        var template = await LoadTemplateAsync(ct);
        var departments = await DepartmentsAsync(college.Id, ct);
        var members = (await db.StructureMembers.AsNoTracking().Where(m => m.CollegeId == college.Id).ToListAsync(ct)).Where(m => m.IsActiveOn(day)).ToList();
        var partners = (await db.IndustryPartners.AsNoTracking().Where(p => p.CollegeId == college.Id).ToListAsync(ct)).Where(p => p.IsActiveOn(day)).ToList();
        var contacts = me.HasSpecial(Feature, Specials.ViewContacts);
        var canEdit = me.Has(Feature, Cap.Edit) && UnitScope.For(me, Feature, Cap.Edit).Covers(college);

        return new CollegeChartDto(
            college.Id, college.Code, college.NameAr, day, canEdit, contacts,
            template.Select(ToDto).ToList(),
            members.OrderBy(m => m.StartDate).Select(m => ToDto(m, contacts)).ToList(),
            departments.Select(d => new DepartmentDto(d.Id, d.Code, d.NameAr)).ToList(),
            partners.Select(p => ToDto(p, contacts)).ToList(),
            StructureRules.Evaluate(template, members, departments.Select(d => (d.Id, d.NameAr)).ToList(), partners));
    }

    /// <summary>لوحة الاكتمال لكل الكليات في نطاق المستخدم، الأقل اكتمالًا أولًا.</summary>
    public async Task<IReadOnlyList<CollegeCompletenessDto>> CompletenessAsync(CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var scope = UnitScope.For(me, Feature, Cap.View);
        var today = CairoTime.DateOf(clock.UtcNow);
        var colleges = (await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => u.Active && (u.Kind == OrgUnitKind.College || u.Kind == OrgUnitKind.Institute)).ToListAsync(ct))
            .Where(scope.Covers).ToList();
        var ids = colleges.Select(c => c.Id).ToList();
        var template = await LoadTemplateAsync(ct);
        var departments = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => u.Active && u.Kind == OrgUnitKind.Department && u.ParentId != null && ids.Contains(u.ParentId.Value)).ToListAsync(ct);
        var members = (await db.StructureMembers.AsNoTracking().Where(m => ids.Contains(m.CollegeId)).ToListAsync(ct)).Where(m => m.IsActiveOn(today)).ToList();
        var partners = (await db.IndustryPartners.AsNoTracking().Where(p => ids.Contains(p.CollegeId)).ToListAsync(ct)).Where(p => p.IsActiveOn(today)).ToList();

        return colleges.Select(c =>
            {
                var result = StructureRules.Evaluate(
                    template,
                    members.Where(m => m.CollegeId == c.Id).ToList(),
                    departments.Where(d => d.ParentId == c.Id).Select(d => (d.Id, d.NameAr)).ToList(),
                    partners.Where(p => p.CollegeId == c.Id).ToList());
                return new CollegeCompletenessDto(c.Id, c.Code, c.NameAr, result.Percent, result.Issues.Count, result.Issues.FirstOrDefault()?.MessageAr);
            })
            .OrderBy(x => x.Percent).ThenBy(x => x.NameAr)
            .ToList();
    }

    public async Task<MemberDto> AddMemberAsync(SaveMemberRequest req, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.Add, ct);
        var college = await LoadCollegeAsync(req.CollegeId, ct);
        UnitScope.For(me, Feature, Cap.Add).Require(college);
        var (unit, position) = await LoadPositionAsync(req.PositionId, ct);
        await ValidateDepartmentAsync(unit, req.DepartmentId, college.Id, ct);

        if (position.Single)
        {
            var holder = await db.StructureMembers
                .Where(m => m.CollegeId == college.Id && m.PositionId == position.Id && m.DepartmentId == req.DepartmentId && (m.EndDate == null || m.EndDate >= req.StartDate))
                .FirstOrDefaultAsync(ct);
            if (holder is not null)
            {
                if (!req.ReplaceCurrent)
                    throw new DomainException("position_taken",
                        $"منصب «{position.TitleAr}» يشغله {holder.FullName}. اختر «استبدال الحالي» لإنهاء عضويته في اليوم السابق.",
                        $"The position is held by {holder.FullName}. Choose to replace the current holder.", 409);
                if (holder.StartDate >= req.StartDate)
                    throw new DomainException("replace_date", "تاريخ بداية العضو الجديد يجب أن يكون بعد بداية الحالي.", "The new start date must be after the current holder's start.", 400);
                holder.EndDate = req.StartDate.AddDays(-1);
                holder.EndReason = "استبدال بعضو جديد";
            }
        }

        var member = new StructureMember
        {
            CollegeId = college.Id,
            DepartmentId = unit.PerDepartment ? req.DepartmentId : null,
            PositionId = position.Id,
            CreatedBy = current.UserId ?? Guid.Empty,
            CreatedAt = clock.UtcNow,
        };
        Apply(member, req);
        db.StructureMembers.Add(member);
        audit.Record(AuditEvents.Audit("org", Feature, "org.member_added", "إضافة عضو إلى هيكل كلية", "Added a structure member") with
        {
            Target = new AuditTarget("structure_member", member.Id.ToString(), position.TitleAr, college.Code),
            Details = new { college = college.Code, position = position.TitleAr, member.StartDate, member.DecisionRef },
        });
        await db.SaveChangesAsync(ct);
        return ToDto(member, true);
    }

    public async Task<MemberDto> UpdateMemberAsync(Guid id, SaveMemberRequest req, CancellationToken ct)
    {
        var (member, college) = await LoadMemberForAsync(id, Cap.Edit, ct);
        Concurrency.Expect(db, member, req.Version);
        var changes = new ChangeSet()
            .Add("fullName", member.FullName, req.FullName?.Trim())
            .Add("title", member.Title, req.Title)
            .Add("startDate", member.StartDate, req.StartDate)
            .Add("decisionRef", member.DecisionRef, req.DecisionRef);
        Apply(member, req);
        audit.Record(AuditEvents.Audit("org", Feature, "org.member_updated", "تعديل بيانات عضو في هيكل كلية", "Updated a structure member") with
        {
            Target = new AuditTarget("structure_member", member.Id.ToString(), member.FullName, college.Code),
            Changes = changes.ToList(),
        });
        await db.SaveChangesAsync(ct);
        return ToDto(member, true);
    }

    /// <summary>إنهاء العضوية بتاريخ وسبب؛ لا حذف، فيبقى الهيكل السابق قابلًا للعرض.</summary>
    public async Task<MemberDto> EndMemberAsync(Guid id, EndRequest req, CancellationToken ct)
    {
        var (member, college) = await LoadMemberForAsync(id, Cap.Delete, ct);
        var reason = Require.Reason(req.Reason);
        if (member.EndDate is not null) throw new DomainException("already_ended", "العضوية منتهية بالفعل.", "The membership has already ended.");
        if (req.EndDate < member.StartDate) throw new DomainException("end_before_start", "تاريخ الانتهاء قبل تاريخ البداية.", "The end date is before the start date.", 400);
        member.EndDate = req.EndDate;
        member.EndReason = reason;
        audit.Record(AuditEvents.Audit("org", Feature, "org.member_ended", "إنهاء عضوية في هيكل كلية", "Ended a structure membership") with
        {
            Target = new AuditTarget("structure_member", member.Id.ToString(), member.FullName, college.Code),
            Reason = reason,
        });
        await db.SaveChangesAsync(ct);
        return ToDto(member, true);
    }

    public async Task<PartnerDto> AddPartnerAsync(SavePartnerRequest req, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.Add, ct);
        var department = await read.Query<Domain.Organization.OrgUnit>().FirstOrDefaultAsync(u => u.Id == req.DepartmentId && u.Kind == OrgUnitKind.Department && u.Active, ct) ?? throw DomainException.NotFound();
        UnitScope.For(me, Feature, Cap.Add).Require(department);
        var partner = new IndustryPartner { CollegeId = department.ParentId!.Value, DepartmentId = department.Id, CreatedAt = clock.UtcNow };
        Apply(partner, req);
        db.IndustryPartners.Add(partner);
        audit.Record(AuditEvents.Audit("org", Feature, "org.partner_added", "إضافة شريك صناعي لقسم", "Added an industry partner") with
        {
            Target = new AuditTarget("industry_partner", partner.Id.ToString(), partner.NameAr, department.Code),
        });
        await db.SaveChangesAsync(ct);
        return ToDto(partner, true);
    }

    public async Task<PartnerDto> UpdatePartnerAsync(Guid id, SavePartnerRequest req, CancellationToken ct)
    {
        var (partner, department) = await LoadPartnerForAsync(id, Cap.Edit, ct);
        Concurrency.Expect(db, partner, req.Version);
        var changes = new ChangeSet().Add("nameAr", partner.NameAr, req.NameAr?.Trim()).Add("coordinatorName", partner.CoordinatorName, req.CoordinatorName).Add("agreementRef", partner.AgreementRef, req.AgreementRef);
        Apply(partner, req);
        audit.Record(AuditEvents.Audit("org", Feature, "org.partner_updated", "تعديل بيانات شريك صناعي", "Updated an industry partner") with
        {
            Target = new AuditTarget("industry_partner", partner.Id.ToString(), partner.NameAr, department.Code),
            Changes = changes.ToList(),
        });
        await db.SaveChangesAsync(ct);
        return ToDto(partner, true);
    }

    public async Task<PartnerDto> EndPartnerAsync(Guid id, EndRequest req, CancellationToken ct)
    {
        var (partner, department) = await LoadPartnerForAsync(id, Cap.Delete, ct);
        var reason = Require.Reason(req.Reason);
        partner.EndedOn = req.EndDate;
        audit.Record(AuditEvents.Audit("org", Feature, "org.partner_ended", "إنهاء شراكة صناعية لقسم", "Ended an industry partnership") with
        {
            Target = new AuditTarget("industry_partner", partner.Id.ToString(), partner.NameAr, department.Code),
            Reason = reason,
        });
        await db.SaveChangesAsync(ct);
        return ToDto(partner, true);
    }

    private async Task<List<StructureUnit>> LoadTemplateAsync(CancellationToken ct)
    {
        var units = await db.StructureUnits.AsNoTracking().Include(u => u.Positions).OrderBy(u => u.SortOrder).ToListAsync(ct);
        foreach (var u in units) u.Positions = u.Positions.OrderBy(p => p.SortOrder).ToList();
        return units;
    }

    private async Task<OrgUnit> LoadCollegeAsync(Guid id, CancellationToken ct) =>
        await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && (u.Kind == OrgUnitKind.College || u.Kind == OrgUnitKind.Institute), ct)
        ?? throw DomainException.NotFound();

    private async Task<List<OrgUnit>> DepartmentsAsync(Guid collegeId, CancellationToken ct) =>
        await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => u.ParentId == collegeId && u.Kind == OrgUnitKind.Department && u.Active)
            .OrderBy(u => u.SortOrder).ThenBy(u => u.NameAr).ToListAsync(ct);

    private async Task<(StructureUnit Unit, StructurePosition Position)> LoadPositionAsync(Guid positionId, CancellationToken ct)
    {
        var position = await db.StructurePositions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == positionId, ct) ?? throw DomainException.NotFound();
        var unit = await db.StructureUnits.AsNoTracking().FirstAsync(u => u.Id == position.UnitId, ct);
        return (unit, position);
    }

    private async Task ValidateDepartmentAsync(StructureUnit unit, Guid? departmentId, Guid collegeId, CancellationToken ct)
    {
        if (!unit.PerDepartment) return;
        if (departmentId is not { } id || !await read.Query<Domain.Organization.OrgUnit>().AnyAsync(u => u.Id == id && u.ParentId == collegeId && u.Kind == OrgUnitKind.Department && u.Active, ct))
            throw new DomainException("department_required", "اختر القسم العلمي لهذا المنصب.", "Choose the department for this position.", 400);
    }

    private async Task<(StructureMember Member, OrgUnit College)> LoadMemberForAsync(Guid id, Cap cap, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, cap, ct);
        var member = await db.StructureMembers.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw DomainException.NotFound();
        var college = await LoadCollegeAsync(member.CollegeId, ct);
        UnitScope.For(me, Feature, cap).Require(college);
        return (member, college);
    }

    private async Task<(IndustryPartner Partner, OrgUnit Department)> LoadPartnerForAsync(Guid id, Cap cap, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, cap, ct);
        var partner = await db.IndustryPartners.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound();
        var department = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().FirstAsync(u => u.Id == partner.DepartmentId, ct);
        UnitScope.For(me, Feature, cap).Require(department);
        return (partner, department);
    }

    private static void Apply(StructureMember m, SaveMemberRequest req)
    {
        m.FullName = string.IsNullOrWhiteSpace(req.FullName) ? throw new DomainException("name_required", "اكتب اسم العضو.", "Enter the member's name.", 400) : req.FullName.Trim();
        m.Title = Require.Optional(req.Title, 200);
        m.Employer = Require.Optional(req.Employer, 200);
        m.Email = Contacts.Email(req.Email);
        m.Phone = Contacts.Phone(req.Phone);
        m.StartDate = req.StartDate;
        m.DecisionRef = Require.Optional(req.DecisionRef, 200);
        m.Notes = Require.Optional(req.Notes, 1000);
    }

    private static void Apply(IndustryPartner p, SavePartnerRequest req)
    {
        p.NameAr = string.IsNullOrWhiteSpace(req.NameAr) ? throw new DomainException("name_required", "اكتب اسم الجهة الشريكة.", "Enter the partner's name.", 400) : req.NameAr.Trim();
        p.Sector = Require.Optional(req.Sector, 150);
        p.CoordinatorName = Require.Optional(req.CoordinatorName, 200);
        p.CoordinatorEmail = Contacts.Email(req.CoordinatorEmail);
        p.CoordinatorPhone = Contacts.Phone(req.CoordinatorPhone);
        p.AgreementDate = req.AgreementDate;
        p.AgreementRef = Require.Optional(req.AgreementRef, 200);
    }

    private static TemplateUnitDto ToDto(StructureUnit u) =>
        new(u.Id, u.Code, u.NameAr, u.RoleAr, u.Duties, u.IsHead, u.PerDepartment,
            u.Positions.Select(p => new PositionDto(p.Id, p.TitleAr, p.Single, p.Required, p.SuggestedRoleCode)).ToList());

    private static MemberDto ToDto(StructureMember m, bool contacts) =>
        new(m.Id, m.PositionId, m.DepartmentId, m.FullName, m.Title, m.Employer, contacts ? m.Email : null, contacts ? m.Phone : null,
            m.StartDate, m.EndDate, m.DecisionRef, m.Notes, m.Version);

    private static PartnerDto ToDto(IndustryPartner p, bool contacts) =>
        new(p.Id, p.DepartmentId, p.NameAr, p.Sector, p.CoordinatorName, contacts ? p.CoordinatorEmail : null, contacts ? p.CoordinatorPhone : null,
            p.AgreementDate, p.AgreementRef, p.EndedOn, p.Version);
}

/// <summary>توحيد بيانات التواصل والتحقق منها.</summary>
public static class Contacts
{
    public static string? Email(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(v)) return null;
        var at = v.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at < v.Length - 3 && v.IndexOf('.', at) > at && v.Length <= 254 && !v.Contains(' ', StringComparison.Ordinal)
            ? v
            : throw new DomainException("invalid_email", "البريد الإلكتروني غير صحيح.", "The email address is not valid.", 400);
    }

    public static string? Phone(string? value)
    {
        var v = new string((value ?? "").Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (v.Length == 0) return null;
        return v.Length is >= 7 and <= 16 ? v : throw new DomainException("invalid_phone", "رقم الهاتف غير صحيح.", "The phone number is not valid.", 400);
    }
}
