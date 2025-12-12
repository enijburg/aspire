using Aspire.Hosting.Groups;

var builder = DistributedApplication.CreateBuilder(args);

// make sure the group status reflects the status of its children
builder.Services.AddAggregateParentStatusFromChildren();

var step1 = builder.AddExecutable("step1", "powershell.exe", builder.AppHostDirectory)
    .WithArgs(
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy", "Bypass",
        "-Command",
        "Write-Host 'Setup...'; Start-Sleep -Seconds 3; Write-Host 'Done.'");

var step2 = builder.AddExecutable("step2", "powershell.exe", builder.AppHostDirectory)
    .WithArgs(
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy", "Bypass",
        "-Command",
        "Write-Host 'Setup...'; Start-Sleep -Seconds 6; Write-Error 'Step2 failed'; exit 1");

builder.AddGroup("my-group")
    .WithChildRelationship(step1)
    .WithChildRelationship(step2);

await builder.Build().RunAsync();
