using Npgsql;
using StackExchange.Redis;
using TicketMaster.Api.Contracts;
using TicketMaster.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(postgresConnectionString));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<TicketMasterStore>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<TicketMasterStore>().InitializeAsync();
}

var api = app.MapGroup("/v1");

api.MapGet("/health_check", () => Results.Ok());

api.MapPost("/event", async (EventRequest request, TicketMasterStore store, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await store.UpsertEventAsync(request, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

api.MapPost("/event/{id}/reservation", async (string id, CreateReservationRequest request, TicketMasterStore store, CancellationToken cancellationToken) =>
{
    try
    {
        var reservationId = await store.CreateReservationAsync(id, request, cancellationToken);
        return Results.Text(reservationId, "text/plain");
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

api.MapGet("/reservation/{reservationId}", async (string reservationId, TicketMasterStore store, CancellationToken cancellationToken) =>
{
    var reservation = await store.GetReservationAsync(reservationId, cancellationToken);
    return reservation is not null
        ? Results.Ok(reservation)
        : Results.NotFound();
});

app.Run();

public partial class Program;
