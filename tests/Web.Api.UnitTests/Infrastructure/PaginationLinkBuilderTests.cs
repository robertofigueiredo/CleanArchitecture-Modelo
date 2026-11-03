using Application.Abstractions.Pagination;
using Microsoft.AspNetCore.Http;
using Web.Api.Contracts;
using Web.Api.Infrastructure;

namespace Web.Api.UnitTests.Infrastructure;

public class PaginationLinkBuilderTests
{
    [Fact]
    public void Build_MiddlePage_ProducesAllFiveLinksPreservingQueryParameters()
    {
        HttpRequest request = CreateRequest("/api/todos", "?userId=11111111-1111-1111-1111-111111111111&page=2&page-size=25");
        PagedResult<string> pagedResult = CreatePagedResult(page: 2, pageSize: 25, totalRecords: 100);

        Links links = PaginationLinkBuilder.Build(request, pagedResult);

        links.Self.ShouldBe("http://localhost/api/todos?userId=11111111-1111-1111-1111-111111111111&page=2&page-size=25");
        links.First.ShouldBe("http://localhost/api/todos?userId=11111111-1111-1111-1111-111111111111&page=1&page-size=25");
        links.Prev.ShouldBe("http://localhost/api/todos?userId=11111111-1111-1111-1111-111111111111&page=1&page-size=25");
        links.Next.ShouldBe("http://localhost/api/todos?userId=11111111-1111-1111-1111-111111111111&page=3&page-size=25");
        links.Last.ShouldBe("http://localhost/api/todos?userId=11111111-1111-1111-1111-111111111111&page=4&page-size=25");
    }

    [Fact]
    public void Build_FirstPage_OmitsFirstAndPrev()
    {
        HttpRequest request = CreateRequest("/api/todos", "?page=1&page-size=25");
        PagedResult<string> pagedResult = CreatePagedResult(page: 1, pageSize: 25, totalRecords: 100);

        Links links = PaginationLinkBuilder.Build(request, pagedResult);

        links.First.ShouldBeNull();
        links.Prev.ShouldBeNull();
        links.Next.ShouldNotBeNull();
        links.Last.ShouldNotBeNull();
    }

    [Fact]
    public void Build_LastPage_OmitsNextAndLast()
    {
        HttpRequest request = CreateRequest("/api/todos", "?page=4&page-size=25");
        PagedResult<string> pagedResult = CreatePagedResult(page: 4, pageSize: 25, totalRecords: 100);

        Links links = PaginationLinkBuilder.Build(request, pagedResult);

        links.Next.ShouldBeNull();
        links.Last.ShouldBeNull();
        links.First.ShouldNotBeNull();
        links.Prev.ShouldNotBeNull();
    }

    [Fact]
    public void Build_NoRecords_OnlySelfIsPresent()
    {
        HttpRequest request = CreateRequest("/api/todos", "?page=1&page-size=25");
        PagedResult<string> pagedResult = CreatePagedResult(page: 1, pageSize: 25, totalRecords: 0);

        Links links = PaginationLinkBuilder.Build(request, pagedResult);

        links.Self.ShouldNotBeNull();
        links.First.ShouldBeNull();
        links.Prev.ShouldBeNull();
        links.Next.ShouldBeNull();
        links.Last.ShouldBeNull();
    }

    [Fact]
    public void Build_SinglePageResult_OmitsEveryNavigationLink()
    {
        HttpRequest request = CreateRequest("/api/todos", "?page=1&page-size=25");
        PagedResult<string> pagedResult = CreatePagedResult(page: 1, pageSize: 25, totalRecords: 10);

        Links links = PaginationLinkBuilder.Build(request, pagedResult);

        links.First.ShouldBeNull();
        links.Prev.ShouldBeNull();
        links.Next.ShouldBeNull();
        links.Last.ShouldBeNull();
    }

    [Fact]
    public void BuildSelfUrl_ReflectsSchemeHostPathAndQueryString()
    {
        HttpRequest request = CreateRequest("/api/todos/123", "?include=labels", scheme: "https", host: "api.example.com");

        string self = PaginationLinkBuilder.BuildSelfUrl(request);

        self.ShouldBe("https://api.example.com/api/todos/123?include=labels");
    }

    private static PagedResult<string> CreatePagedResult(int page, int pageSize, int totalRecords) =>
        PagedResult<string>.Create([], PaginationRequest.Create(page, pageSize).Value, totalRecords);

    private static HttpRequest CreateRequest(string path, string queryString, string scheme = "http", string host = "localhost")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = scheme;
        httpContext.Request.Host = new HostString(host);
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString(queryString);

        return httpContext.Request;
    }
}
