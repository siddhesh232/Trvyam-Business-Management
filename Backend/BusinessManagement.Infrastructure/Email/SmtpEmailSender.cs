using BusinessManagement.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BusinessManagement.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    /// <summary>StartTls (587, default) | SslOnConnect (465) | None (local dev servers only).</summary>
    public string Security { get; set; } = "StartTls";
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Trvyam Management";
}

/// <summary>Plain SMTP (works with any free provider, e.g. Gmail app password, Brevo, or your own server).</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> log) : IEmailSender
{
    public async Task SendAsync(string toAddress, string subject, string textBody, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.FromAddress))
            throw new InvalidOperationException("SMTP is not configured. Set Email__Host and Email__FromAddress (and credentials) via environment variables.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = textBody };

        var socket = o.Security.ToLowerInvariant() switch
        {
            "ssl" or "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(o.Host, o.Port, socket, ct);
        if (!string.IsNullOrEmpty(o.User)) await client.AuthenticateAsync(o.User, o.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        log.LogInformation("Email sent (subject: {Subject})", subject);   // never log body or recipient secrets
    }
}
