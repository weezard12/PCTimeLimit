using PCTimeLimitServer.Configuration;
using PCTimeLimitShared.Contracts;

namespace PCTimeLimitServer.Api;

public static class OpsAuthorization
{
    public static bool HasValidOpsKey(HttpRequest request, SecurityOptions securityOptions)
    {
        if (!request.Headers.TryGetValue(ApiHeaders.OpsKey, out var provided))
        {
            return false;
        }

        var supplied = provided.ToString();
        return !string.IsNullOrWhiteSpace(supplied)
            && string.Equals(supplied, securityOptions.OpsKey, StringComparison.Ordinal);
    }
}
