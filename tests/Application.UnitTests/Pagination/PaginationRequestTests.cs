using Application.Abstractions.Pagination;
using SharedKernel;

namespace Application.UnitTests.Pagination;

public class PaginationRequestTests
{
    [Fact]
    public void Create_NoParametersSupplied_UsesDefaults()
    {
        Result<PaginationRequest> result = PaginationRequest.Create(page: null, pageSize: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Page.ShouldBe(PaginationRequest.DefaultPage);
        result.Value.PageSize.ShouldBe(PaginationRequest.DefaultPageSize);
    }

    [Theory]
    [InlineData(1, 25)]
    [InlineData(3, 10)]
    [InlineData(1, 1000)]
    public void Create_ValidPageAndPageSize_ReturnsSuccess(int page, int pageSize)
    {
        Result<PaginationRequest> result = PaginationRequest.Create(page, pageSize);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Page.ShouldBe(page);
        result.Value.PageSize.ShouldBe(pageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_PageLessThanOne_ReturnsUnprocessableFailure(int page)
    {
        Result<PaginationRequest> result = PaginationRequest.Create(page, pageSize: 25);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Unprocessable);
        result.Error.Code.ShouldBe("Pagination.InvalidPage");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_PageSizeLessThanOne_ReturnsUnprocessableFailure(int pageSize)
    {
        Result<PaginationRequest> result = PaginationRequest.Create(page: 1, pageSize);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Unprocessable);
        result.Error.Code.ShouldBe("Pagination.InvalidPageSize");
    }

    [Fact]
    public void Create_PageSizeExceedsMax_ReturnsUnprocessableFailure()
    {
        Result<PaginationRequest> result = PaginationRequest.Create(page: 1, pageSize: PaginationRequest.MaxPageSize + 1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Unprocessable);
        result.Error.Code.ShouldBe("Pagination.PageSizeTooLarge");
    }

    [Fact]
    public void Create_PageSizeAtMax_ReturnsSuccess()
    {
        Result<PaginationRequest> result = PaginationRequest.Create(page: 1, pageSize: PaginationRequest.MaxPageSize);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PageSize.ShouldBe(PaginationRequest.MaxPageSize);
    }

    [Theory]
    [InlineData(1, 25, 0)]
    [InlineData(2, 25, 25)]
    [InlineData(3, 10, 20)]
    [InlineData(4, 25, 75)]
    public void Skip_ComputesZeroBasedOffsetFromOneBasedPage(int page, int pageSize, int expectedSkip)
    {
        PaginationRequest pagination = PaginationRequest.Create(page, pageSize).Value;

        pagination.Skip.ShouldBe(expectedSkip);
    }
}
