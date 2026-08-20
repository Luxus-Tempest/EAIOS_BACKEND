using System.Net;
using System.Net.Mail;
using System.Text;

namespace EAIOS.Api.Infrastructure.Email;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Envoi d'emails transactionnels. Deux implémentations :
/// <see cref="LoggingEmailService"/> (dev — journalise) et <see cref="SmtpEmailService"/> (prod — SMTP).
/// </summary>
public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);

    Task SendInvitationAsync(string toEmail, string organizationName, string inviterName,
        string token, string? personalMessage = null, CancellationToken ct = default);

    Task SendEmailVerificationAsync(string toEmail, string firstName, string token, CancellationToken ct = default);

    Task SendPasswordResetAsync(string toEmail, string firstName, string token, CancellationToken ct = default);

    Task SendWelcomeAsync(string toEmail, string firstName, string organizationName, CancellationToken ct = default);

    Task SendNotificationAsync(string toEmail, string subject, string body,
        string? actionUrl = null, string? actionLabel = null, CancellationToken ct = default);
}

public sealed record EmailMessage(
    string   To,
    string   Subject,
    string   HtmlBody,
    string?  TextBody = null,
    string?  ReplyTo  = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);

// ── Options ───────────────────────────────────────────────────────────────────

public sealed class EmailOptions
{
    public string  Provider     { get; init; } = "Logging";   // Logging | Smtp
    public string  FromAddress  { get; init; } = "no-reply@eaios.io";
    public string  FromName     { get; init; } = "EAIOS";
    public string  AppBaseUrl   { get; init; } = "http://localhost:5173";
    public string  SmtpHost     { get; init; } = "localhost";
    public int     SmtpPort     { get; init; } = 587;
    public bool    SmtpUseSsl   { get; init; } = true;
    public string? SmtpUser     { get; init; }
    public string? SmtpPassword { get; init; }

    public static EmailOptions FromConfiguration(IConfiguration config)
    {
        var s = config.GetSection("Email");
        return new EmailOptions
        {
            Provider     = s["Provider"]    ?? "Logging",
            FromAddress  = s["FromAddress"] ?? "no-reply@eaios.io",
            FromName     = s["FromName"]    ?? "EAIOS",
            AppBaseUrl   = s["AppBaseUrl"]  ?? config["Cors:AllowedOrigins:0"] ?? "http://localhost:5173",
            SmtpHost     = s["Smtp:Host"]   ?? "localhost",
            SmtpPort     = s.GetValue("Smtp:Port", 587),
            SmtpUseSsl   = s.GetValue("Smtp:UseSsl", true),
            SmtpUser     = s["Smtp:User"],
            SmtpPassword = s["Smtp:Password"]
        };
    }
}

// ── Templates partagés ────────────────────────────────────────────────────────

/// <summary>
/// Rendu des emails transactionnels. Centralisé pour que les deux providers
/// produisent exactement le même contenu.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage Invitation(EmailOptions o, string toEmail, string organizationName,
        string inviterName, string token, string? personalMessage)
    {
        var link = $"{o.AppBaseUrl.TrimEnd('/')}/register?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(toEmail)}";
        var extra = string.IsNullOrWhiteSpace(personalMessage)
            ? string.Empty
            : $"<blockquote style=\"border-left:3px solid #d0d7de;margin:16px 0;padding:4px 16px;color:#57606a\">{Encode(personalMessage)}</blockquote>";

        var html = Layout(
            title: $"Invitation à rejoindre {Encode(organizationName)}",
            body: $"<p>Bonjour,</p>" +
                  $"<p><strong>{Encode(inviterName)}</strong> vous invite à rejoindre l'espace " +
                  $"<strong>{Encode(organizationName)}</strong> sur EAIOS.</p>" +
                  extra +
                  "<p>Cette invitation expire dans 7 jours.</p>",
            actionUrl: link,
            actionLabel: "Accepter l'invitation");

        var text = $"{inviterName} vous invite à rejoindre {organizationName} sur EAIOS.\n\nAcceptez ici : {link}\n\nCette invitation expire dans 7 jours.";
        return new EmailMessage(toEmail, $"Invitation à rejoindre {organizationName} sur EAIOS", html, text);
    }

    public static EmailMessage EmailVerification(EmailOptions o, string toEmail, string firstName, string token)
    {
        var link = $"{o.AppBaseUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(toEmail)}";
        var html = Layout(
            title: "Vérifiez votre adresse email",
            body: $"<p>Bonjour {Encode(firstName)},</p>" +
                  "<p>Confirmez votre adresse email pour activer votre compte EAIOS.</p>" +
                  "<p style=\"color:#57606a;font-size:13px\">Ce lien expire dans 24 heures.</p>",
            actionUrl: link,
            actionLabel: "Vérifier mon email");

        var text = $"Bonjour {firstName},\n\nConfirmez votre adresse email : {link}\n\nCe lien expire dans 24 heures.";
        return new EmailMessage(toEmail, "Vérifiez votre adresse email EAIOS", html, text);
    }

    public static EmailMessage PasswordReset(EmailOptions o, string toEmail, string firstName, string token)
    {
        var link = $"{o.AppBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(toEmail)}";
        var html = Layout(
            title: "Réinitialisation de votre mot de passe",
            body: $"<p>Bonjour {Encode(firstName)},</p>" +
                  "<p>Une réinitialisation de mot de passe a été demandée pour votre compte EAIOS.</p>" +
                  "<p style=\"color:#57606a;font-size:13px\">Ce lien expire dans 1 heure. " +
                  "Si vous n'êtes pas à l'origine de cette demande, ignorez cet email — votre mot de passe reste inchangé.</p>",
            actionUrl: link,
            actionLabel: "Choisir un nouveau mot de passe");

        var text = $"Bonjour {firstName},\n\nRéinitialisez votre mot de passe : {link}\n\nCe lien expire dans 1 heure. Si vous n'êtes pas à l'origine de cette demande, ignorez cet email.";
        return new EmailMessage(toEmail, "Réinitialisation de votre mot de passe EAIOS", html, text);
    }

    public static EmailMessage Welcome(EmailOptions o, string toEmail, string firstName, string organizationName)
    {
        var link = o.AppBaseUrl.TrimEnd('/');
        var html = Layout(
            title: $"Bienvenue sur EAIOS, {Encode(firstName)}",
            body: $"<p>Votre compte est actif sur l'espace <strong>{Encode(organizationName)}</strong>.</p>" +
                  "<p>Vous pouvez dès à présent déposer des documents, interroger la base de connaissance " +
                  "et lancer vos premiers agents.</p>",
            actionUrl: link,
            actionLabel: "Accéder à EAIOS");

        var text = $"Bienvenue sur EAIOS, {firstName}.\n\nVotre compte est actif sur {organizationName}.\n\n{link}";
        return new EmailMessage(toEmail, "Bienvenue sur EAIOS", html, text);
    }

    public static EmailMessage Notification(EmailOptions o, string toEmail, string subject, string body,
        string? actionUrl, string? actionLabel)
    {
        var absolute = actionUrl is null
            ? null
            : actionUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? actionUrl
                : $"{o.AppBaseUrl.TrimEnd('/')}/{actionUrl.TrimStart('/')}";

        var html = Layout(subject, $"<p>{Encode(body)}</p>", absolute, actionLabel ?? "Ouvrir dans EAIOS");
        return new EmailMessage(toEmail, subject, html, body);
    }

    // ── Mise en page commune ──────────────────────────────────────────────────
    private static string Layout(string title, string body, string? actionUrl, string? actionLabel)
    {
        var button = actionUrl is null
            ? string.Empty
            : $"<p style=\"margin:28px 0\">" +
              $"<a href=\"{actionUrl}\" style=\"background:#1f6feb;color:#ffffff;text-decoration:none;" +
              $"padding:11px 20px;border-radius:6px;font-weight:600;display:inline-block\">" +
              $"{Encode(actionLabel ?? "Continuer")}</a></p>" +
              $"<p style=\"color:#8c959f;font-size:12px;word-break:break-all\">" +
              $"Si le bouton ne fonctionne pas, copiez ce lien : {actionUrl}</p>";

        return "<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"></head>" +
               "<body style=\"margin:0;padding:24px;background:#f6f8fa;" +
               "font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1f2328\">" +
               "<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #d0d7de;" +
               "border-radius:10px;padding:32px\">" +
               $"<h1 style=\"margin:0 0 20px;font-size:20px;font-weight:600\">{Encode(title)}</h1>" +
               body +
               button +
               "<hr style=\"border:none;border-top:1px solid #d0d7de;margin:28px 0 16px\">" +
               "<p style=\"color:#8c959f;font-size:12px;margin:0\">" +
               "EAIOS — Enterprise AI Operating System. Cet email vous a été envoyé automatiquement." +
               "</p></div></body></html>";
    }

    private static string Encode(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}

// ── Implémentation dev : journalise l'email au lieu de l'envoyer ──────────────

public sealed class LoggingEmailService(IConfiguration config, ILogger<LoggingEmailService> logger) : IEmailService
{
    private readonly EmailOptions _options = EmailOptions.FromConfiguration(config);

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[EMAIL DEV] To: {To} | Subject: {Subject}\n{Body}",
            message.To, message.Subject, message.TextBody ?? "(html uniquement)");
        return Task.CompletedTask;
    }

    public Task SendInvitationAsync(string toEmail, string organizationName, string inviterName,
        string token, string? personalMessage = null, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.Invitation(_options, toEmail, organizationName, inviterName, token, personalMessage), ct);

    public Task SendEmailVerificationAsync(string toEmail, string firstName, string token, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.EmailVerification(_options, toEmail, firstName, token), ct);

    public Task SendPasswordResetAsync(string toEmail, string firstName, string token, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.PasswordReset(_options, toEmail, firstName, token), ct);

    public Task SendWelcomeAsync(string toEmail, string firstName, string organizationName, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.Welcome(_options, toEmail, firstName, organizationName), ct);

    public Task SendNotificationAsync(string toEmail, string subject, string body,
        string? actionUrl = null, string? actionLabel = null, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.Notification(_options, toEmail, subject, body, actionUrl, actionLabel), ct);
}

// ── Implémentation prod : SMTP ────────────────────────────────────────────────

public sealed class SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger) : IEmailService
{
    private readonly EmailOptions _options = EmailOptions.FromConfiguration(config);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl             = _options.SmtpUseSsl,
            DeliveryMethod        = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(_options.SmtpUser))
            client.Credentials = new NetworkCredential(_options.SmtpUser, _options.SmtpPassword);

        using var mail = new MailMessage
        {
            From            = new MailAddress(_options.FromAddress, _options.FromName),
            Subject         = message.Subject,
            Body            = message.HtmlBody,
            IsBodyHtml      = true,
            BodyEncoding    = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        mail.To.Add(message.To);

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            mail.ReplyToList.Add(message.ReplyTo);

        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.TextBody, Encoding.UTF8, "text/plain"));
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.HtmlBody, Encoding.UTF8, "text/html"));
        }

        foreach (var a in message.Attachments ?? [])
            mail.Attachments.Add(new Attachment(new MemoryStream(a.Content), a.FileName, a.ContentType));

        try
        {
            await client.SendMailAsync(mail, ct);
            logger.LogInformation("Email envoyé à {To} — {Subject}", message.To, message.Subject);
        }
        catch (Exception ex)
        {
            // Un email non délivré ne doit jamais faire échouer la requête métier appelante.
            logger.LogError(ex, "Échec d'envoi email à {To} — {Subject}", message.To, message.Subject);
        }
    }

    public Task SendInvitationAsync(string toEmail, string organizationName, string inviterName,
        string token, string? personalMessage = null, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.Invitation(_options, toEmail, organizationName, inviterName, token, personalMessage), ct);

    public Task SendEmailVerificationAsync(string toEmail, string firstName, string token, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.EmailVerification(_options, toEmail, firstName, token), ct);

    public Task SendPasswordResetAsync(string toEmail, string firstName, string token, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.PasswordReset(_options, toEmail, firstName, token), ct);

    public Task SendWelcomeAsync(string toEmail, string firstName, string organizationName, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.Welcome(_options, toEmail, firstName, organizationName), ct);

    public Task SendNotificationAsync(string toEmail, string subject, string body,
        string? actionUrl = null, string? actionLabel = null, CancellationToken ct = default) =>
        SendAsync(EmailTemplates.Notification(_options, toEmail, subject, body, actionUrl, actionLabel), ct);
}
