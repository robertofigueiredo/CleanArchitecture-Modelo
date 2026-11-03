using Microsoft.AspNetCore.Http.HttpResults;
using SharedKernel;
using Web.Api.Contracts;
using Web.Api.Infrastructure;

namespace Web.Api.UnitTests.Infrastructure;

public class CustomResultsTests
{
    [Fact]
    public void Problem_SuccessResult_Throws()
    {
        Result success = Result.Success();

        Should.Throw<InvalidOperationException>(() => CustomResults.Problem(success));
    }

    [Fact]
    public void Problem_NotFoundError_MapsTo404()
    {
        Result result = Result.Failure(Error.NotFound("TodoItems.NotFound", "not found"));

        (int statusCode, ErrorResponse body) = Invoke(result);

        statusCode.ShouldBe(StatusCodes.Status404NotFound);
        body.Errors.Single().Code.ShouldBe("TodoItems.NotFound");
    }

    [Fact]
    public void Problem_ConflictError_MapsTo409()
    {
        Result result = Result.Failure(Error.Conflict("Users.EmailNotUnique", "conflict"));

        (int statusCode, _) = Invoke(result);

        statusCode.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void Problem_ProblemError_MapsTo400()
    {
        Result result = Result.Failure(Error.Problem("TodoItems.AlreadyCompleted", "already completed"));

        (int statusCode, _) = Invoke(result);

        statusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Problem_ForbiddenError_MapsTo403()
    {
        Result result = Result.Failure(Error.Forbidden("Users.Unauthorized", "not yours"));

        (int statusCode, _) = Invoke(result);

        statusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void Problem_UnprocessableError_MapsTo422()
    {
        Result result = Result.Failure(Error.Unprocessable("Pagination.PageSizeTooLarge", "too large"));

        (int statusCode, _) = Invoke(result);

        statusCode.ShouldBe(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void Problem_GenericFailureError_MapsTo500()
    {
        Result result = Result.Failure(Error.Failure("General.Unexpected", "unexpected"));

        (int statusCode, _) = Invoke(result);

        statusCode.ShouldBe(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void Problem_ValidationErrorWithMultipleFailures_ReturnsOneApiErrorPerFailure()
    {
        var validationError = new ValidationError(
        [
            Error.Problem("Description.Required", "Description is required."),
            Error.Problem("Priority.Invalid", "Priority is invalid.")
        ]);

        Result result = Result.Failure(validationError);

        (int statusCode, ErrorResponse body) = Invoke(result);

        statusCode.ShouldBe(StatusCodes.Status400BadRequest);
        body.Errors.Count.ShouldBe(2);
        body.Errors.ShouldContain(e => e.Code == "Description.Required");
        body.Errors.ShouldContain(e => e.Code == "Priority.Invalid");
    }

    [Fact]
    public void Problem_EveryError_HasCodeTitleAndDetail()
    {
        Result result = Result.Failure(Error.NotFound("TodoItems.NotFound", "The item was not found"));

        (_, ErrorResponse body) = Invoke(result);

        ApiError error = body.Errors.Single();
        error.Code.ShouldBe("TodoItems.NotFound");
        error.Title.ShouldNotBeNullOrWhiteSpace();
        error.Detail.ShouldBe("The item was not found");
    }

    private static (int StatusCode, ErrorResponse Body) Invoke(Result result)
    {
        IResult apiResult = CustomResults.Problem(result);

        var statusCodeResult = apiResult.ShouldBeAssignableTo<IStatusCodeHttpResult>();
        var valueResult = apiResult.ShouldBeAssignableTo<IValueHttpResult>();

        int statusCode = statusCodeResult!.StatusCode!.Value;
        var body = valueResult!.Value.ShouldBeOfType<ErrorResponse>();

        return (statusCode, body);
    }
}
