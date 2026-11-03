using Application.Abstractions.Notifications;
using Domain.Users;
using SharedKernel;

namespace Application.Users.Register;

internal sealed class UserRegisteredDomainEventHandler(IEmailSender emailSender)
    : IDomainEventHandler<UserRegisteredDomainEvent>
{
    public async Task Handle(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        await emailSender.SendAsync(
            domainEvent.Email,
            "Welcome!",
            "Thanks for registering. We're glad to have you on board.",
            cancellationToken);
    }
}
