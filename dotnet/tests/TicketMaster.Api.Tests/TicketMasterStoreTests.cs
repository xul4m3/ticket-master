using TicketMaster.Api.Contracts;
using TicketMaster.Api.Services;

namespace TicketMaster.Api.Tests;

public class TicketMasterStoreTests
{
    private readonly TicketMasterStore _store = new();

    [Fact]
    public void RandomReservation_ReservesFirstAvailableSeats()
    {
        _store.UpsertEvent(new EventRequest
        {
            EventName = "concert-a",
            Artist = "artist",
            Areas =
            [
                new AreaRequest { AreaId = "A", Price = 100, RowCount = 2, ColCount = 3 }
            ]
        });

        var reservationId = _store.CreateReservation("concert-a", new CreateReservationRequest
        {
            UserId = "user-1",
            EventId = "concert-a",
            AreaId = "A",
            NumOfSeats = 2,
            Type = "RANDOM"
        });

        var found = _store.TryGetReservation(reservationId, out var reservation);

        Assert.True(found);
        Assert.NotNull(reservation);
        Assert.Equal("RESERVED", reservation!.State);
        Assert.Equal(2, reservation.NumOfSeat);
        Assert.Equal([(0, 0), (0, 1)], reservation.Seats.Select(seat => (seat.Row, seat.Col)).ToArray());
    }

    [Fact]
    public void SelfPickReservation_FailsWhenSeatAlreadyReserved()
    {
        _store.UpsertEvent(new EventRequest
        {
            EventName = "concert-b",
            Artist = "artist",
            Areas =
            [
                new AreaRequest { AreaId = "B", Price = 120, RowCount = 1, ColCount = 2 }
            ]
        });

        _store.CreateReservation("concert-b", new CreateReservationRequest
        {
            UserId = "user-1",
            EventId = "concert-b",
            AreaId = "B",
            NumOfSeats = 1,
            Type = "SELF_PICK",
            Seats = [new SeatRequest { Row = 0, Col = 0 }]
        });

        var failedReservationId = _store.CreateReservation("concert-b", new CreateReservationRequest
        {
            UserId = "user-2",
            EventId = "concert-b",
            AreaId = "B",
            NumOfSeats = 1,
            Type = "SELF_PICK",
            Seats = [new SeatRequest { Row = 0, Col = 0 }]
        });

        var found = _store.TryGetReservation(failedReservationId, out var reservation);

        Assert.True(found);
        Assert.NotNull(reservation);
        Assert.Equal("FAILED", reservation!.State);
        Assert.Contains("already reserved", reservation.FailedReason);
    }

    [Fact]
    public void CreateReservation_ThrowsWhenRouteAndBodyEventIdsDiffer()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            _store.CreateReservation("concert-c", new CreateReservationRequest
            {
                UserId = "user-1",
                EventId = "other-event",
                AreaId = "A",
                NumOfSeats = 1,
                Type = "RANDOM"
            }));

        Assert.Contains("Route event id must match", exception.Message);
    }

    [Fact]
    public void CreateReservation_FailsWhenEventDoesNotExist()
    {
        var reservationId = _store.CreateReservation("missing-event", new CreateReservationRequest
        {
            UserId = "user-1",
            AreaId = "A",
            NumOfSeats = 1,
            Type = "RANDOM"
        });

        var found = _store.TryGetReservation(reservationId, out var reservation);

        Assert.True(found);
        Assert.NotNull(reservation);
        Assert.Equal("FAILED", reservation!.State);
        Assert.Equal("Event does not exist.", reservation.FailedReason);
    }
}
