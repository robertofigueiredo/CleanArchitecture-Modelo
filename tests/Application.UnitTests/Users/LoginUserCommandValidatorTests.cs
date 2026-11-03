using Application.Users.Login;
using FluentValidation.TestHelper;

namespace Application.UnitTests.Users;

public sealed class LoginUserCommandValidatorTests
{
    private readonly LoginUserCommandValidator _validator = new();

    [Fact]
    public void Validator_Should_HaveError_WhenEmailIsEmpty()
    {
        var command = new LoginUserCommand(string.Empty, "Password123");

        TestValidationResult<LoginUserCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Validator_Should_HaveError_WhenEmailIsInvalid()
    {
        var command = new LoginUserCommand("not-an-email", "Password123");

        TestValidationResult<LoginUserCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Validator_Should_HaveError_WhenPasswordIsEmpty()
    {
        var command = new LoginUserCommand("test@example.com", string.Empty);

        TestValidationResult<LoginUserCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Password);
    }

    [Fact]
    public void Validator_Should_NotHaveErrors_WhenCommandIsValid()
    {
        var command = new LoginUserCommand("test@example.com", "Password123");

        TestValidationResult<LoginUserCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
