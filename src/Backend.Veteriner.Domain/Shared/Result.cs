namespace Backend.Veteriner.Domain.Shared;

public readonly struct Result
{
    public bool IsSuccess { get; }
    public Error Error { get; }
    private Result(bool ok, Error err) { IsSuccess = ok; Error = err; }
    public static Result Success() => new(true, Error.None);
    public static Result Failure(string code, string message) => new(false, new Error(code, message));
    public static Result Failure(Error error) => new(false, error);
}

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error Error { get; }
    private Result(bool ok, T? val, Error err) { IsSuccess = ok; Value = val; Error = err; }
    public static Result<T> Success(T value) => new(true, value, Error.None);
    public static Result<T> Failure(string code, string message) => new(false, default, new Error(code, message));
    public static Result<T> Failure(Error error) => new(false, default, error);
}
