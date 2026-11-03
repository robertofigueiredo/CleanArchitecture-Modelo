# Result Pattern

Defined in `src/SharedKernel/Result.cs`. This template never throws exceptions to signal an
expected/business failure — every handler returns `Result` (no value) or `Result<TValue>` (value
on success).

## Shape

```csharp
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

public class Result<TValue> : Result
{
    [NotNull]
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failure result can't be accessed.");

    public static implicit operator Result<TValue>(TValue? value) =>
        value is not null ? Success(value) : Failure<TValue>(Error.NullValue);

    public static Result<TValue> ValidationFailure(Error error) => new(default, false, error);
}
```

The `Result` constructor itself enforces internal consistency: a success `Result` must carry
`Error.None`, a failure `Result` must carry a non-`None` `Error` — constructing an inconsistent
combination throws `ArgumentException` immediately, so a `Result` can never be silently in a
malformed state.

## Rules

- **`Result<TValue>.Value` throws if accessed on a failed result.** Never call `.Value` without
  first confirming `IsSuccess` — in practice, that means never touch `.Value` outside
  `ResultExtensions.Match` (see [api/minimal-api.md](../api/minimal-api.md)).
- **The implicit `TValue → Result<TValue>` conversion** lets a handler `return someEntity.Id;` (as
  `Guid → Result<Guid>`) or `return todos;` (as `List<TodoResponse> → Result<List<TodoResponse>>`)
  without spelling out `Result.Success(...)` — this is why
  `CreateTodoCommandHandler.Handle` ends with a bare `return todoItem.Id;`. A `null` value through
  this operator becomes `Result.Failure<TValue>(Error.NullValue)`, not a success with a null
  value — this only matters for reference-typed `TValue`; value types (like `Guid`) never hit the
  `null` branch.
- **Build failures from a `Domain` `*Errors` factory**, never an ad-hoc `new Error(...)` inline in
  a handler — see [architecture/domain.md](../architecture/domain.md#errors--errors-static-classes).

## Composing multiple failures — `ValidationError`

```csharp
// src/SharedKernel/ValidationError.cs
public sealed record ValidationError : Error
{
    public ValidationError(Error[] errors)
        : base("Validation.General", "One or more validation errors occurred", ErrorType.Validation)
    { Errors = errors; }

    public static ValidationError FromResults(IEnumerable<Result> results) =>
        new([.. results.Where(r => r.IsFailure).Select(r => r.Error)]);
}
```

`ValidationError` is how more than one `Error` travels inside a single `Result` — used by
`ValidationDecorator` to collect every FluentValidation failure into one `Result.Failure`. See
[patterns/validation.md](../patterns/validation.md).

## Endpoint-side consumption

Endpoints never branch on `IsSuccess` by hand — they call `Match`:

```csharp
Result<Guid> result = await handler.Handle(command, cancellationToken);
return result.Match(Results.Ok, CustomResults.Problem);
```

Full HTTP mapping in [patterns/error-handling.md](../patterns/error-handling.md) and
[api/minimal-api.md](../api/minimal-api.md).

## REQUIRED

- Every command/query handler returns `Result`/`Result<T>` — no exceptions for expected failure
  paths.
- Build every failure from a `Domain.<Aggregate>Errors` factory (or, for validation, let
  `ValidationDecorator` build the `ValidationError`) — never construct `new Error(...)` inline in
  a handler or endpoint.

## FORBIDDEN

- Accessing `Result<T>.Value` without going through `Match` or an explicit `IsSuccess` check
  first.
- Throwing a custom exception type to represent something `ErrorType` already models
  (`NotFound`, `Conflict`, `Validation`, `Problem`) — use `Result.Failure`.
