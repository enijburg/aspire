using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;

namespace JwtAuth.Tests;

/// <summary>
/// A custom MSTest attribute that automatically wraps each test method with
/// OpenTelemetry activity tracing. Replaces the need to manually call
/// <c>StartTestActivity</c> / <c>CompleteTestActivity</c> in every test.
/// </summary>
/// <remarks>
/// Tests report their observed HTTP status code via <see cref="TestActivityScope.ReportStatusCode"/>.
/// If no status code is reported, the activity outcome is derived from the MSTest result.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TracedTestMethodAttribute(
    HttpStatusCode expectedStatusCode = HttpStatusCode.OK,
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = -1)
    : TestMethodAttribute(callerFilePath, callerLineNumber)
{
    /// <summary>
    /// Shared <see cref="ActivitySource"/> used for all test activities.
    /// Also referenced by helper methods that create sub-activities (e.g. report helpers).
    /// </summary>
    internal static readonly ActivitySource TestActivitySource = new("JwtAuth.Tests");

    /// <summary>
    /// The HTTP status code the test expects to observe.
    /// </summary>
    public HttpStatusCode ExpectedStatusCode { get; } = expectedStatusCode;

    /// <inheritdoc />
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        using var activity = TestActivitySource.StartActivity(testMethod.TestMethodName, ActivityKind.Internal);
        activity?.SetTag("test.name", testMethod.TestMethodName);
        activity?.SetTag("test.expected_status_code", (int)ExpectedStatusCode);
        activity?.SetTag("test.expects_success", (int)ExpectedStatusCode < 400);

        var scope = TestActivityScope.Begin();
        try
        {
            var results = await base.ExecuteAsync(testMethod);

            if (scope.ActualStatusCode.HasValue)
            {
                var actualStatusCode = scope.ActualStatusCode.Value;
                var passed = (int)actualStatusCode == (int)ExpectedStatusCode;

                activity?.SetTag("test.actual_status_code", (int)actualStatusCode);
                activity?.SetTag("test.passed", passed);
                activity?.SetStatus(
                    passed ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                    passed ? "Test passed" : $"Expected {(int)ExpectedStatusCode} but got {(int)actualStatusCode}");
            }
            else
            {
                // No status code reported; derive outcome from the test result
                var passed = results.All(r => r.Outcome == UnitTestOutcome.Passed);
                activity?.SetTag("test.passed", passed);
                activity?.SetStatus(
                    passed ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                    passed ? "Test passed" : "Test failed");
            }

            return results;
        }
        finally
        {
            TestActivityScope.End();
        }
    }
}
