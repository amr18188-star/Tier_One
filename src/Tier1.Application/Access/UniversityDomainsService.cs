using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Auditing;
using Tier1.Application.Common;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;

namespace Tier1.Application.Access;

/// <param name="Users">Accounts whose email falls under this root (the most specific root, if roots nest).</param>
public sealed record EmailDomainDto(Guid Id, string Domain, string UniversityAr, string? UniversityEn, bool Active, int Users,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, uint Version);

public sealed record SaveEmailDomainRequest(string Domain, string UniversityAr, string? UniversityEn, bool Active, uint? Version);

public sealed record DeleteEmailDomainRequest(string Reason);

/// <summary>
/// ADR-0016: the approved university email domains (adm.security). Each root covers itself and all its subdomains; a change
/// applies at once to account creation and email changes, with no deployment. Disabling or deleting a domain never touches an
/// account: existing accounts keep signing in, and the list shows how many accounts each domain has before the change.
/// </summary>
public sealed class UniversityDomainsService(IIdentityStore db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit)
{
    private const string Feature = Features.Security;

    public async Task<IReadOnlyList<EmailDomainDto>> ListAsync(CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        var domains = await db.EmailDomains.AsNoTracking().OrderBy(d => d.Domain).ToListAsync(ct);
        var counts = await CountUsersAsync(domains.Select(d => d.Domain).ToList(), ct);
        return domains.Select(d => ToDto(d, counts.GetValueOrDefault(d.Domain))).ToList();
    }

    /// <summary>The active roots, for checking an email in a form (any account administrator may read them).</summary>
    public async Task<IReadOnlyList<string>> ActiveRootsAsync(CancellationToken ct) =>
        await db.EmailDomains.AsNoTracking().Where(d => d.Active).OrderBy(d => d.Domain).Select(d => d.Domain).ToListAsync(ct);

    /// <summary>
    /// The email is at an active approved domain or one of its subdomains: for new accounts and email changes. Returns the
    /// normalized email.
    /// </summary>
    public async Task<string> EnsureAllowedEmailAsync(string? email, CancellationToken ct)
    {
        var normalized = IamPolicy.NormalizeEmail(email);
        var roots = await ActiveRootsAsync(ct);
        if (EmailDomainRules.MatchingRoot(normalized, roots) is null)
            throw new DomainException("email_domain_not_allowed",
                $"البريد يجب أن يكون بريدًا جامعيًا من نطاق معتمد أو أحد نطاقاته الفرعية: {string.Join("، ", roots)}.",
                $"The email must be a university email at an approved domain or one of its subdomains: {string.Join(", ", roots)}.", 400) { Field = "email" };
        return normalized;
    }

    public async Task<EmailDomainDto> CreateAsync(SaveEmailDomainRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Add, ct);
        var root = EmailDomainRules.NormalizeRoot(req.Domain);
        var (ar, en) = Names(req);
        await EnsureNoOverlapAsync(root, null, ct);
        var domain = new EmailDomain { Domain = root, UniversityAr = ar, UniversityEn = en, Active = req.Active, CreatedAt = clock.UtcNow, CreatedBy = current.UserId };
        db.EmailDomains.Add(domain);
        audit.Record(Event("security.domain_add", "إضافة نطاق بريد جامعي", "Added a university email domain", domain, "A") with
        {
            Changes = [new FieldChange("domain", null, root), new FieldChange("university", null, ar), new FieldChange("active", null, req.Active ? "true" : "false")],
        });
        await db.SaveChangesAsync(ct);
        return ToDto(domain, (await CountUsersAsync([root], ct)).GetValueOrDefault(root));
    }

    public async Task<EmailDomainDto> UpdateAsync(Guid id, SaveEmailDomainRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var domain = await db.EmailDomains.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound();
        Concurrency.Expect(db, domain, Concurrency.Require(req.Version));
        var root = EmailDomainRules.NormalizeRoot(req.Domain);
        var (ar, en) = Names(req);
        if (root != domain.Domain) await EnsureNoOverlapAsync(root, id, ct);
        var users = (await CountUsersAsync([domain.Domain], ct)).GetValueOrDefault(domain.Domain);
        var changes = new List<FieldChange>();
        if (root != domain.Domain) changes.Add(new FieldChange("domain", domain.Domain, root));
        if (ar != domain.UniversityAr) changes.Add(new FieldChange("universityAr", domain.UniversityAr, ar));
        if (en != domain.UniversityEn) changes.Add(new FieldChange("universityEn", domain.UniversityEn, en));
        if (req.Active != domain.Active) changes.Add(new FieldChange("active", domain.Active ? "true" : "false", req.Active ? "true" : "false"));
        if (changes.Count == 0) return ToDto(domain, users);
        var (action, msgAr, msgEn) = req.Active == domain.Active ? ("security.domain_update", "تعديل نطاق بريد جامعي", "Updated a university email domain")
            : req.Active ? ("security.domain_enable", "تفعيل نطاق بريد جامعي", "Enabled a university email domain")
            : ("security.domain_disable", "إيقاف نطاق بريد جامعي", "Disabled a university email domain");
        domain.Domain = root;
        domain.UniversityAr = ar;
        domain.UniversityEn = en;
        domain.Active = req.Active;
        domain.UpdatedAt = clock.UtcNow;
        audit.Record(Event(action, msgAr, msgEn, domain, "E") with { Changes = changes, Details = new { existingAccounts = users } });
        await db.SaveChangesAsync(ct);
        return ToDto(domain, (await CountUsersAsync([root], ct)).GetValueOrDefault(root));
    }

    /// <summary>Removes the domain from the approved list. Accounts under it stay and keep signing in; no new one can be created there.</summary>
    public async Task DeleteAsync(Guid id, string reason, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Delete, ct);
        var why = Require.Reason(reason);
        var domain = await db.EmailDomains.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound();
        var users = (await CountUsersAsync([domain.Domain], ct)).GetValueOrDefault(domain.Domain);
        db.EmailDomains.Remove(domain);
        audit.Record(Event("security.domain_delete", "حذف نطاق بريد جامعي", "Deleted a university email domain", domain, "D") with
        {
            Changes = [new FieldChange("domain", domain.Domain, null)],
            Reason = why,
            Details = new { existingAccounts = users, university = domain.UniversityAr },
        });
        await db.SaveChangesAsync(ct);
    }

    private static (string Ar, string? En) Names(SaveEmailDomainRequest req)
    {
        var ar = Require.Optional(req.UniversityAr, 200)
            ?? throw new DomainException("university_required", "اكتب اسم الجامعة.", "Enter the university name.", 400) { Field = "universityAr" };
        return (ar, Require.Optional(req.UniversityEn, 200));
    }

    /// <summary>One root per name, and no root inside another (eng.capu.edu.eg is already covered by capu.edu.eg).</summary>
    private async Task EnsureNoOverlapAsync(string root, Guid? self, CancellationToken ct)
    {
        var others = await db.EmailDomains.AsNoTracking().Where(d => d.Id != self).Select(d => d.Domain).ToListAsync(ct);
        if (others.Contains(root, StringComparer.Ordinal))
            throw new DomainException("duplicate_domain", "هذا النطاق مسجل من قبل.", "This domain is already registered.", 409) { Field = "domain" };
        if (others.FirstOrDefault(o => EmailDomainRules.Covers(o, root)) is { } parent)
            throw new DomainException("domain_covered", $"النطاق {root} يدخل تحت {parent} المسجل، فهو مقبول به.",
                $"{root} is already covered by the registered {parent}.", 400) { Field = "domain" };
        if (others.FirstOrDefault(o => EmailDomainRules.Covers(root, o)) is { } child)
            throw new DomainException("domain_covers_existing", $"النطاق {root} يشمل {child} المسجل؛ عدّل {child} أو احذفه أولًا.",
                $"{root} would cover the registered {child}; edit or delete {child} first.", 400) { Field = "domain" };
    }

    /// <summary>Accounts per root, each counted once under the most specific root that covers its email.</summary>
    private async Task<Dictionary<string, int>> CountUsersAsync(IReadOnlyList<string> roots, CancellationToken ct)
    {
        if (roots.Count == 0) return [];
        var all = await db.EmailDomains.AsNoTracking().Select(d => d.Domain).ToListAsync(ct);
        var emails = await db.Users.AsNoTracking().Select(u => u.Email).ToListAsync(ct);
        return emails.Select(e => EmailDomainRules.MatchingRoot(e, all.Union(roots)))
            .Where(r => r is not null && roots.Contains(r))
            .GroupBy(r => r!)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    private static EmailDomainDto ToDto(EmailDomain d, int users) =>
        new(d.Id, d.Domain, d.UniversityAr, d.UniversityEn, d.Active, users, d.CreatedAt, d.UpdatedAt, d.Version);

    private static AuditEntry Event(string action, string ar, string en, EmailDomain d, string cap) =>
        AuditEvents.Security(action, Feature, ar, en) with
        {
            Permission = $"{Feature}:{cap}",
            Target = new AuditTarget("EmailDomain", d.Id.ToString(), d.Domain),
        };
}
