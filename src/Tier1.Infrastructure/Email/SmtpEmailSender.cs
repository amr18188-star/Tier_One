using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Tier1.Infrastructure.Email;

public sealed class EmailOptions
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public string From { get; set; } = "language-center@localhost";

    public string? User { get; set; }

    public string? Password { get; set; }

    /// <summary>StartTls في الإنتاج؛ None لخادم التطوير المحلي.</summary>
    public string Security { get; set; } = "StartTls";
}

public interface IEmailSender
{
    Task SendAsync(IEnumerable<string> to, string subject, string body, CancellationToken ct);
}

/// <summary>إرسال بريد نصي بسيط عبر SMTP. مزود البريد يختار عند التشغيل.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(IEnumerable<string> to, string subject, string body, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host)) throw new InvalidOperationException("Email:Host is not configured.");
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(o.From));
        foreach (var address in to) message.To.Add(MailboxAddress.Parse(address));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        // مهلة قصيرة لكل عملية وللإرسال كله: خادم بريد بطيء لا يحبس الدفعة حتى ينتهي قفلها فترسل نسخة أخرى الرسالة نفسها.
        using var client = new SmtpClient { Timeout = 30_000 };
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(45));
        var security = Enum.TryParse<SecureSocketOptions>(o.Security, true, out var s) ? s : SecureSocketOptions.StartTls;
        await client.ConnectAsync(o.Host, o.Port, security, limit.Token);
        if (!string.IsNullOrEmpty(o.User)) await client.AuthenticateAsync(o.User, o.Password ?? "", limit.Token);
        await client.SendAsync(message, limit.Token);
        await client.DisconnectAsync(true, limit.Token);
    }
}
