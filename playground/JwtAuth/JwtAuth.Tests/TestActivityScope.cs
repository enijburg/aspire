using System.Net;

namespace JwtAuth.Tests;

/// <summary>
/// Provides an ambient scope for test methods to report their observed HTTP status code
/// back to the <see cref="TracedTestMethodAttribute"/> wrapper.
/// </summary>
public static class TestActivityScope
{
    private static readonly AsyncLocal<ActivityState?> SCurrentState = new();

    /// <summary>
    /// Reports the actual HTTP status code observed during the test.
    /// Call this from within a test method decorated with <see cref="TracedTestMethodAttribute"/>.
    /// </summary>
    public static void ReportStatusCode(HttpStatusCode statusCode)
    {
        if (SCurrentState.Value is { } state)
        {
            state.ActualStatusCode = statusCode;
        }
    }

    internal static ActivityState Begin()
    {
        var state = new ActivityState();
        SCurrentState.Value = state;
        return state;
    }

    internal static void End()
    {
        SCurrentState.Value = null;
    }

    internal sealed class ActivityState
    {
        public HttpStatusCode? ActualStatusCode { get; set; }
    }
}
