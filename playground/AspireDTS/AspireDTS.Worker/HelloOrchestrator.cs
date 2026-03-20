using Microsoft.DurableTask;

namespace AspireDTS.Worker;

/// <summary>
/// Sample orchestrator that calls the <see cref="SayHelloActivity"/> for a given city name.
/// </summary>
internal class HelloOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        var results = await Task.WhenAll(
            context.CallActivityAsync<string>(nameof(SayHelloActivity), "Tokyo"),
            context.CallActivityAsync<string>(nameof(SayHelloActivity), "London"),
            context.CallActivityAsync<string>(nameof(SayHelloActivity), input));

        return string.Join(" ", results);
    }
}