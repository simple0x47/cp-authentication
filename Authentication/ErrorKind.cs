namespace Cuplan;

public static class ErrorKind
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string OkResponseNull = "ok_response_null";
    public const string ErrorResponseNull = "error_response_null";
    public const string TimedOut = "timed_out";
    public const string ServiceError = "service_error";
    public const string UnknownError = "unknown_error";
}