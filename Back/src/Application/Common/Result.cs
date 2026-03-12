namespace Service.Application.Common;

/// <summary>
/// Result pattern for command/query responses.
/// </summary>
/// <typeparam name="T">Value type.</typeparam>
public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Indicates if the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Value for successful results.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Error code for failed results.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Error message for failed results.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">Result value.</param>
    /// <returns>Successful result.</returns>
    public static Result<T> Ok(T value) => new(true, value, null, null);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="errorCode">Error code.</param>
    /// <param name="errorMessage">Error message.</param>
    /// <returns>Failed result.</returns>
    public static Result<T> Fail(string errorCode, string errorMessage) => new(false, default, errorCode, errorMessage);
}
