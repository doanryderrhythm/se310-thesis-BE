using System.Net;
using System.Net.Mail;

namespace AuthApi.Features.Email;

/// <summary>
/// Sends queued emails over SMTP. Configure with Smtp__Host, Smtp__Port, Smtp__From and
/// optionally Smtp__User, Smtp__Password, Smtp__EnableSsl. Without Smtp__Host, emails are
/// logged and dropped so the rest of the API keeps working.
/// </summary>
internal sealed class EmailSenderWorker(
    EmailQueue queue, IConfiguration configuration, ILogger<EmailSenderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var host = configuration["Smtp:Host"];
        var port = int.TryParse(configuration["Smtp:Port"], out var configuredPort) ? configuredPort : 25;
        var from = configuration["Smtp:From"] ?? "no-reply@themis.local";
        var fromName = configuration["Smtp:FromName"] ?? "Themis Fashion Studio";
        var user = configuration["Smtp:User"];
        var password = configuration["Smtp:Password"];
        var enableSsl = bool.TryParse(configuration["Smtp:EnableSsl"], out var ssl) && ssl;

        if (string.IsNullOrWhiteSpace(host))
        {
            logger.LogWarning("Smtp:Host is not configured; outgoing email is disabled.");
        }

        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                logger.LogInformation("Email to {To} skipped (SMTP disabled): {Subject}", message.To, message.Subject);
                continue;
            }

            try
            {
                using var client = new SmtpClient(host, port) { EnableSsl = enableSsl, Timeout = 10_000 };
                if (!string.IsNullOrEmpty(user))
                {
                    client.Credentials = new NetworkCredential(user, password);
                }

                using var mail = new MailMessage
                {
                    From = new MailAddress(from, fromName),
                    Subject = message.Subject,
                    Body = message.HtmlBody,
                    IsBodyHtml = true
                };
                mail.To.Add(message.To);
                await client.SendMailAsync(mail, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Sending email to {To} failed.", message.To);
            }
        }
    }
}
