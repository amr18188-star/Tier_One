using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Tier1.Infrastructure.Email;

public sealed class EmailOptions
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public string From { get; set; } = "tier1@localhost";

    /// <summary>للتطوير والاختبار: كل رسالة تكتب ملف .eml في هذا المجلد بدل إرسالها.</summary>
    public string? PickupDirectory { get; set; }

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
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(o.From));
        foreach (var address in to) message.To.Add(MailboxAddress.Parse(address));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        if (!string.IsNullOrWhiteSpace(o.PickupDirectory))
        {
            Directory.CreateDirectory(o.PickupDirectory);
            await message.WriteToAsync(Path.Combine(o.PickupDirectory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml"), ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(o.Host)) throw new InvalidOperationException("Email:Host is not configured.");
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
