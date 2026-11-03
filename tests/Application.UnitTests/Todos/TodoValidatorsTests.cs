using Application.Todos.Create;
using Application.Todos.Update;
using Domain.Todos;
using FluentValidation.TestHelper;

namespace Application.UnitTests.Todos;

public sealed class TodoValidatorsTests
{
    private readonly CreateTodoCommandValidator _createValidator = new();
    private readonly UpdateTodoCommandValidator _updateValidator = new();

    [Fact]
    public void CreateValidator_Should_HaveError_WhenDescriptionIsEmpty()
    {
        var command = new CreateTodoCommand
        {
            UserId = Guid.NewGuid(),
            Description = string.Empty,
            Priority = Priority.Low
        };

        TestValidationResult<CreateTodoCommand> result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Description);
    }

    [Fact]
    public void CreateValidator_Should_NotHaveErrors_WhenCommandIsValid()
    {
        var command = new CreateTodoCommand
        {
            UserId = Guid.NewGuid(),
            Description = "Buy groceries",
            Priority = Priority.Medium,
            Labels = ["home"]
        };

        TestValidationResult<CreateTodoCommand> result = _createValidator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateValidator_Should_HaveError_WhenDescriptionExceedsMaxLength()
    {
        var command = new UpdateTodoCommand(Guid.NewGuid(), new string('a', 501));

        TestValidationResult<UpdateTodoCommand> result = _updateValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Description);
    }

    [Fact]
    public void UpdateValidator_Should_NotHaveErrors_WhenCommandIsValid()
    {
        var command = new UpdateTodoCommand(Guid.NewGuid(), "Updated description");

        TestValidationResult<UpdateTodoCommand> result = _updateValidator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
