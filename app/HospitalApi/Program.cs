using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready");

app.MapGet("/api/patients", () => Results.Ok(new[]
{
    new Patient( Id : 1, Name : "Demo Patient A", Ward : "Cardiology" ),
    new Patient(Id : 2, Name : "Demo Patient B", Ward : "Neurology")
}))
.WithName("GetPatients");

app.Run();

public record Patient (int Id, string Name, string Ward);