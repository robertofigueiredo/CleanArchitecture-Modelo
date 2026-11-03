using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.Refresh;

internal sealed class RefreshTokenCommandHandler(
    IApplicationDbContext context,
    ITokenProvider tokenProvider,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<RefreshTokenCommand, AccessTokensResponse>
{
    public async Task<Result<AccessTokensResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        RefreshToken? refreshToken = await context.RefreshTokens
            .Include(rt => rt.User)
            .SingleOrDefaultAsync(rt => rt.Token == command.RefreshToken, cancellationToken);

        if (refreshToken is null || refreshToken.ExpiresOnUtc < dateTimeProvider.UtcNow)
        {
            return Result.Failure<AccessTokensResponse>(UserErrors.InvalidRefreshToken);
        }

        string accessToken = tokenProvider.Create(refreshToken.User);
        string newRefreshToken = tokenProvider.GenerateRefreshToken();

        // Rotate the refresh token so a stolen token can only be used once.
        refreshToken.Token = newRefreshToken;
        refreshToken.ExpiresOnUtc = dateTimeProvider.UtcNow.AddDays(RefreshTokenExpirationInDays);

        await context.SaveChangesAsync(cancellationToken);

        return new AccessTokensResponse(accessToken, newRefreshToken);
    }

    private const int RefreshTokenExpirationInDays = 7;
}
