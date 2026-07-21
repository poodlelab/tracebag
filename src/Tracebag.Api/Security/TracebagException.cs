namespace Tracebag.Api.Security;

public sealed class TracebagException : Exception
{
    public TracebagException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
