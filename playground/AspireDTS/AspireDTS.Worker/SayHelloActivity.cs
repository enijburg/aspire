using Microsoft.DurableTask;

namespace AspireDTS.Worker;

/// <summary>
/// Sample activity that returns a greeting for the given city name.
/// </summary>
internal class SayHelloActivity(ILogger<SayHelloActivity> logger) : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        logger.LogInformation("Saying hello to {City}", input);
        return Task.FromResult($"Hello, {input}!");
    }
}