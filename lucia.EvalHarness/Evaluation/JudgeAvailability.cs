using System.Net.Http;
using System.Text.Json;
using Azure;
using System.ClientModel;

namespace lucia.EvalHarness.Evaluation;

public static class JudgeAvailability
{
    public const string NotConfigured = "not_configured";
    public const string ProviderError = "provider_error";
    public const string Timeout = "timeout";
    public const string InvalidResponse = "invalid_response";
    public const string Partial = "partial";
    public const string Unavailable = "unavailable";

    public static string Reason(string status) => status switch
    {
        NotConfigured => "Judge is not configured.",
        ProviderError => "Judge provider request failed.",
        Timeout => "Judge request timed out.",
        InvalidResponse => "Judge response was invalid.",
        Partial => "Some judge scores are unavailable.",
        Unavailable => "Score is unavailable.",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown availability status.")
    };

    public static bool TryClassify(Exception exception, CancellationToken callerToken, out string status)
    {
        status = string.Empty;

        if (exception is OperationCanceledException)
        {
            if (callerToken.IsCancellationRequested)
            {
                return false;
            }

            status = Timeout;
            return true;
        }

        if (exception is TimeoutException)
        {
            status = Timeout;
            return true;
        }

        if (exception is JsonException or FormatException)
        {
            status = InvalidResponse;
            return true;
        }

        if (exception is HttpRequestException or ClientResultException or RequestFailedException)
        {
            status = ProviderError;
            return true;
        }

        return false;
    }

}
