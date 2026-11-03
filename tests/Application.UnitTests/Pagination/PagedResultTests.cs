using Application.Abstractions.Pagination;

namespace Application.UnitTests.Pagination;

public class PagedResultTests
{
    [Fact]
    public void Create_NoRecords_TotalPagesIsZero()
    {
        PaginationRequest pagination = PaginationRequest.Create(page: 1, pageSize: 25).Value;

        PagedResult<string> result = PagedResult<string>.Create([], pagination, totalRecords: 0);

        result.TotalRecords.ShouldBe(0);
        result.TotalPages.ShouldBe(0);
        result.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(100, 25, 4)]  // exact multiple
    [InlineData(101, 25, 5)]  // ceiling division: one extra record needs one extra page
    [InlineData(24, 25, 1)]   // fewer records than a single page
    [InlineData(1, 1, 1)]
    [InlineData(1000, 1000, 1)]
    [InlineData(1001, 1000, 2)]
    public void Create_ComputesTotalPagesUsingCeilingDivision(int totalRecords, int pageSize, int expectedTotalPages)
    {
        PaginationRequest pagination = PaginationRequest.Create(page: 1, pageSize).Value;

        PagedResult<string> result = PagedResult<string>.Create([], pagination, totalRecords);

        result.TotalPages.ShouldBe(expectedTotalPages);
    }

    [Fact]
    public void HasPreviousPage_FirstPage_IsFalse()
    {
        PaginationRequest pagination = PaginationRequest.Create(page: 1, pageSize: 25).Value;

        PagedResult<string> result = PagedResult<string>.Create([], pagination, totalRecords: 100);

        result.HasPreviousPage.ShouldBeFalse();
    }

    [Fact]
    public void HasPreviousPage_NotFirstPage_IsTrue()
    {
        PaginationRequest pagination = PaginationRequest.Create(page: 2, pageSize: 25).Value;

        PagedResult<string> result = PagedResult<string>.Create([], pagination, totalRecords: 100);

        result.HasPreviousPage.ShouldBeTrue();
    }

    [Fact]
    public void HasNextPage_LastPage_IsFalse()
    {
        PaginationRequest pagination = PaginationRequest.Create(page: 4, pageSize: 25).Value;

        PagedResult<string> result = PagedResult<string>.Create([], pagination, totalRecords: 100);

        result.HasNextPage.ShouldBeFalse();
    }

    [Fact]
    public void HasNextPage_NotLastPage_IsTrue()
    {
        PaginationRequest pagination = PaginationRequest.Create(page: 3, pageSize: 25).Value;

        PagedResult<string> result = PagedResult<string>.Create([], pagination, totalRecords: 100);

        result.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public void HasNextPage_ZeroRecords_IsFalse()
    {
        PaginationRequest pagination = PaginationRequest.Create(page: 1, pageSize: 25).Value;

        PagedResult<string> result = PagedResult<string>.Create([], pagination, totalRecords: 0);

        result.HasNextPage.ShouldBeFalse();
        result.HasPreviousPage.ShouldBeFalse();
    }
}
