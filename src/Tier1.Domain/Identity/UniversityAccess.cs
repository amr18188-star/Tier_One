namespace Tier1.Domain.Identity;

/// <summary>
/// An approved university email root domain (iam.email_domains): an account's email must be at the root itself or at any of its
/// subdomains (capu.edu.eg covers eng.capu.edu.eg). Managed by the system administrator; no domain is written in the code.
/// </summary>
public sealed class EmailDomain
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The root domain, lower case, without "@" (capu.edu.eg).</summary>
    public string Domain { get; set; } = "";

    public string UniversityAr { get; set; } = "";

    public string? UniversityEn { get; set; }

    /// <summary>A disabled domain accepts no new account or email change; existing accounts keep working.</summary>
    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency (xmin in PostgreSQL).</summary>
    public uint Version { get; set; }
}

/// <summary>
/// One allowed address or range (iam.ip_allow_rules), for every user (UserId null: the global list) or for one user. Stored as a
/// normalized CIDR: 192.168.1.10 is 192.168.1.10/32, and 2001:db8::/32 stays as written.
/// </summary>
public sealed class IpAllowRule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? UserId { get; set; }

    public string Cidr { get; set; } = "";

    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public uint Version { get; set; }
}
