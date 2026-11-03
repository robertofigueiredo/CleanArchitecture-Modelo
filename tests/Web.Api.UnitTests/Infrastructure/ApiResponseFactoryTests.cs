using Application.Abstractions.Pagination;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Web.Api.Contracts;
using Web.Api.Infrastructure;

namespace Web.Api.UnitTests.Infrastructure;

public class ApiResponseFactoryTests
{
    [Fact]
    public void Ok_NonPaginatedData_ReturnsEnvelopeWithDataAndSelfLink_AndNoMeta()
    {
        HttpRequest request = CreateRequest("/api/todos/123", "");

        IResult result = ApiResponseFactory.Ok(new { Id = 123 }, request);

        SuccessResponse<object> body = GetBody<SuccessResponse<object>>(result);
        body.Data.ShouldNotBeNull();
        body.Links.Self.ShouldBe("http://localhost/api/todos/123");
        body.Meta.ShouldBeNull(); // meta is optional and only used for paginated collections
    }

    [Fact]
    public void Ok_PagedResult_ReturnsEnvelopeWithDataMetaAndLinks()
    {
        HttpRequest request = CreateRequest("/api/todos", "?page=1&page-size=25");
        PaginationRequest pagination = PaginationRequest.Create(1, 25).Value;
        PagedResult<string> pagedResult = PagedResult<string>.Create(["a", "b"], pagination, totalRecords: 2);

        IResult result = ApiResponseFactory.Ok(pagedResult, request);

        SuccessResponse<IReadOnlyList<string>> body = GetBody<SuccessResponse<IReadOnlyList<string>>>(result);
        body.Data.ShouldBe(["a", "b"]);
        body.Meta.ShouldNotBeNull();
        body.Meta!.TotalRecords.ShouldBe(2);
        body.Meta.TotalPages.ShouldBe(1);
        body.Links.Self.ShouldNotBeNullOrWhiteSpace();
    }

    private static TBody GetBody<TBody>(IResult result)
    {
        var valueResult = result.ShouldBeAssignableTo<IValueHttpResult>();
        return valueResult!.Value.ShouldBeOfType<TBody>();
    }

    private static HttpRequest CreateRequest(string path, string queryString)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost");
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString(queryString);

        return httpContext.Request;
    }
}
