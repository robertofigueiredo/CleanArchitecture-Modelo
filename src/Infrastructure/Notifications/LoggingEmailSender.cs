using Application.Abstractions.Notifications;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Notifications;

// A no-op email sender that only logs. Swap it for a real provider
// (SendGrid, Postmark, SMTP, ...) when you need to actually send mail.
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email to {Recipient}: {Subject}", recipient, subject);

        return Task.CompletedTask;
    }
}
