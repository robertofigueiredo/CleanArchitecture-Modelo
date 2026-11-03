using Application.Abstractions.Messaging;

namespace Application.Users.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AccessTokensResponse>;
