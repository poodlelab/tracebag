using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Tracebag.Api.Security;

public static class TokenGenerator
{
    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return WebEncoders.Base64UrlEncode(bytes);
    }
}
