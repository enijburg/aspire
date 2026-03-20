using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

/// <summary>
/// Sample activity that returns a greeting for the given city name.
/// </summary>
internal class SayHelloActivity : TaskActivity<string, string>
{
    private readonly ILogger<SayHelloActivity> _logger;

    public SayHelloActivity(ILogger<SayHelloActivity> logger)
    {
        _logger = logger;
    }

    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        _logger.LogInformation("Saying hello to {City}", input);
        return Task.FromResult($"Hello, {input}!");
    }
}
