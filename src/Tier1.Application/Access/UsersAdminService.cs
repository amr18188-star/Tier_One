using System.Net.Mail;
using Tier1.Application.Abstractions;
using Tier1.Application.Accounts;
using Tier1.Application.Auditing;
using Tier1.Application.Security;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Tier1.Application.Access;

/// <param name="Incomplete">ADR-0021: the account lacks some of its required data (English name, national ID or mobile number).</param>
public sealed record UserListItemDto(Guid Id, string Email, string Name, bool Active, bool MfaEnabled, bool Locked, DateTimeOffset? LastLoginAt, IReadOnlyList<string> Roles,
    bool Incomplete = false);

public sealed record AssignmentDto(Guid Id, Guid RoleId, string RoleCode, string RoleName, IReadOnlyList<string> ScopeValues, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo,
    PersonDto? GrantedBy, DateTimeOffset GrantedAt, string? Reason, DateTimeOffset? RevokedAt, string? RevokeReason, bool Active, bool PendingApproval, PersonDto? ApprovedBy, bool CanApprove);

public sealed record OverrideDto(Guid Id, string Feature, string Caps, IReadOnlyList<string> Specials, OverrideEffect Effect, string Scope, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo,
    PersonDto? GrantedBy, string Reason, DateTimeOffset? RevokedAt, bool Active);

public sealed record UserDetailDto(Guid Id, string Email, string FullNameAr, string? FullNameEn, bool Active, bool MfaEnabled, bool MustChangePassword, DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, bool HoldsReservedRole, IReadOnlyList<AssignmentDto> Assignments, IReadOnlyList<OverrideDto> Overrides,
    string? Title = null, string? DisplayName = null, string? Phone = null, string? NationalIdMasked = null, IReadOnlyList<string>? MissingData = null);

/// <param name="Title">The title before the name, one of <see cref="Domain.Identity.PersonTitles"/> (ADR-0019), or null for none.</param>
/// <param name="FullNameEn">Required, in Latin letters (ADR-0021).</param>
/// <param name="NationalId">Required: the Egyptian national ID, 14 digits, on one account only (ADR-0021).</param>
/// <param name="Phone">Required: an Egyptian mobile number (ADR-0021).</param>
public sealed record CreateUserRequest(string Email, string FullNameAr, string? FullNameEn, string? Title = null, string? NationalId = null, string? Phone = null);

/// <summary>
/// لقب في قائمة الاختيار. <c>Title</c> نصه العربي، وهو القيمة التي ترسل وتحفظ بأي لغة للواجهة؛ <c>Label</c> و<c>Meaning</c> ما يعرض
/// بلغة الواجهة؛ ومجموعته (هيئة التدريس، الموظفون، الرتب العسكرية).
/// </summary>
public sealed record TitleDto(string Title, string Label, string Meaning, Domain.Identity.TitleGroup Group);

/// <param name="Email">A new university email (the user's login name), or null to keep it. Checked against the approved domains.</param>
/// <param name="Title">The title before the name, or null for none; saved with the names (not "keep what is there").</param>
/// <param name="FullNameEn">Required, in Latin letters (ADR-0021).</param>
/// <param name="NationalId">A new national ID, or null to keep the one on file (the server never sends it back in full). Required when the account has none.</param>
/// <param name="Phone">Required: an Egyptian mobile number (ADR-0021).</param>
public sealed record UpdateUserRequest(string FullNameAr, string? FullNameEn, string? Email = null, string? Title = null, string? NationalId = null, string? Phone = null);

/// <summary>كلمة المرور المؤقتة تعرض مرة واحدة فقط ولا تحفظ إلا بصمتها.</summary>
public sealed record TemporaryPasswordDto(Guid UserId, string TemporaryPassword);

public sealed record AssignRoleRequest(Guid RoleId, IReadOnlyList<string>? ScopeValues, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, string Reason);

public sealed record UpdateAssignmentRequest(IReadOnlyList<string>? ScopeValues, DateTimeOffset? ValidTo, string Reason);

public sealed record AddOverrideRequest(string Feature, string Caps, IReadOnlyList<string>? Specials, OverrideEffect Effect, string? Scope, DateTimeOffset? ValidTo, string Reason);
/// <summary>
/// Staff accounts as the administration screens read them: the list, one account with its assignments and overrides, and its effective
/// permissions. ARCH-2: changes are in <see cref="UserAccountsService"/>, <see cref="RoleAssignmentService"/> and <see cref="UserOverridesService"/>,
/// under the rules of <see cref="IamPolicy"/>.
/// </summary>
public sealed class UsersAdminService(IIdentityStore db, IClock clock, ICurrentUser current, IAccessService access)
{
    public async Task<IReadOnlyList<UserListItemDto>> ListAsync(string? search, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var q = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(u => u.Email.Contains(s) || u.FullNameAr.Contains(search.Trim()) || (u.FullNameEn != null && u.FullNameEn.ToLower().Contains(s)));
        }
        var users = await q.OrderBy(u => u.FullNameAr).Take(500).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var roles = await db.UserRoles.AsNoTracking()
            .Where(a => ids.Contains(a.UserId) && a.RevokedAt == null && a.ApprovedAt != null && a.ValidFrom <= now && (a.ValidTo == null || a.ValidTo > now))
            .Join(db.Roles, a => a.RoleId, r => r.Id, (a, r) => new { a.UserId, r.Code })
            .ToListAsync(ct);
        return users.Select(u => new UserListItemDto(u.Id, u.Email, u.DisplayName, u.Active, u.MfaEnabled, u.LockedUntil > now, u.LastLoginAt,
            roles.Where(r => r.UserId == u.Id).Select(r => r.Code).Distinct().Order(StringComparer.Ordinal).ToList(), u.MissingData.Count > 0)).ToList();
    }

    public async Task<UserDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        var assignments = await db.UserRoles.AsNoTracking().Where(a => a.UserId == id)
            .Join(db.Roles, a => a.RoleId, r => r.Id, (a, r) => new { A = a, r.Code, r.NameAr, r.NameEn })
            .OrderByDescending(x => x.A.GrantedAt).ToListAsync(ct);
        var overrides = await db.UserOverrides.AsNoTracking().Where(o => o.UserId == id).OrderByDescending(o => o.GrantedAt).ToListAsync(ct);
        var people = await PeopleAsync(assignments.SelectMany(a => new[] { a.A.GrantedBy, a.A.ApprovedBy }).Concat(overrides.Select(o => o.GrantedBy)), ct);
        var me = current.UserId;
        var iAmSa = (await access.CurrentAsync(ct)).IsSuperAdmin;
        return new UserDetailDto(u.Id, u.Email, u.FullNameAr, u.FullNameEn, u.Active, u.MfaEnabled, u.MustChangePassword, u.LockedUntil > now ? u.LockedUntil : null, u.CreatedAt, u.LastLoginAt,
            assignments.Any(x => x.A.RevokedAt == null && PermissionCatalog.ReservedRoles.Contains(x.Code)),
            assignments.Select(x => new AssignmentDto(x.A.Id, x.A.RoleId, x.Code, Lang.Name(x.NameAr, x.NameEn), x.A.ScopeValues, x.A.ValidFrom, x.A.ValidTo,
                x.A.GrantedBy is { } g ? people.GetValueOrDefault(g) : null, x.A.GrantedAt, x.A.Reason, x.A.RevokedAt, x.A.RevokeReason, x.A.IsActiveAt(now),
                x.A.IsPendingApproval, x.A.ApprovedBy is { } ab ? people.GetValueOrDefault(ab) : null,
                x.A.IsPendingApproval && iAmSa && me != x.A.GrantedBy && me != id)).ToList(),
            overrides.Select(o => new OverrideDto(o.Id, o.Feature, PermissionCatalog.Letters(o.Caps), o.Specials, o.Effect, GrantView.ScopeCode(o.Scope), o.ValidFrom, o.ValidTo,
                o.GrantedBy is { } g ? people.GetValueOrDefault(g) : null, o.Reason, o.RevokedAt, o.IsActiveAt(now))).ToList(),
            u.Title, u.DisplayName,
            // ADR-0021: the national ID leaves the server masked only (its last four digits).
            u.Phone, u.NationalId is { } nid ? NationalIds.Mask(nid) : null, u.MissingData);
    }

    public async Task<EffectiveDto> EffectiveAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) throw DomainException.NotFound();
        return EffectiveDto.From(await access.GetAsync(userId, ct));
    }

    private Task<Dictionary<Guid, PersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct) =>
        People.LookupAsync(db.Users, ids.Where(i => i is not null).Select(i => i!.Value), ct);
}
