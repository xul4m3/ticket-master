using TicketMaster.Api.Contracts;
using TicketMaster.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TicketMasterStore>();

var app = builder.Build();

var api = app.MapGroup("/v1");

api.MapGet("/health_check", () => Results.Ok());

api.MapPost("/event", (EventRequest request, TicketMasterStore store) =>
{
    try
    {
        return Results.Ok(store.UpsertEvent(request));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

api.MapPost("/event/{id}/reservation", (string id, CreateReservationRequest request, TicketMasterStore store) =>
{
    try
    {
        var reservationId = store.CreateReservation(id, request);
        return Results.Text(reservationId, "text/plain");
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

api.MapGet("/reservation/{reservationId}", (string reservationId, TicketMasterStore store) =>
    store.TryGetReservation(reservationId, out var reservation)
        ? Results.Ok(reservation)
        : Results.NotFound());

app.Run();

public partial class Program;
