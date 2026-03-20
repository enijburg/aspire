using Microsoft.DurableTask;

/// <summary>
/// Sample orchestrator that calls the <see cref="SayHelloActivity"/> for a given city name.
/// </summary>
internal class HelloOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        var result = await context.CallActivityAsync<string>(nameof(SayHelloActivity), "Tokyo") + " ";
        result += await context.CallActivityAsync<string>(nameof(SayHelloActivity), "London") + " ";
        result += await context.CallActivityAsync<string>(nameof(SayHelloActivity), input);
        return result;
    }
}
