using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using TicketMaster.Api.Contracts;
using TicketMaster.Api.Services;

namespace TicketMaster.Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TicketMasterPersistenceCollection : ICollectionFixture<TicketMasterPersistenceFixture>
{
    public const string Name = "ticket-master-persistence";
}

public sealed class TicketMasterPersistenceFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("ticket_master_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7.2-alpine")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;
    public IConnectionMultiplexer ConnectionMultiplexer { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();

        DataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        ConnectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());

        await CreateStore().InitializeAsync();
        await ResetAsync();
    }

    public async Task ResetAsync()
    {
        await using var truncateCommand = DataSource.CreateCommand("TRUNCATE TABLE reservations, events;");
        await truncateCommand.ExecuteNonQueryAsync();
        await ConnectionMultiplexer.GetDatabase().ExecuteAsync("FLUSHALL");
    }

    public TicketMasterStore CreateStore() => new(DataSource, ConnectionMultiplexer);

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await ConnectionMultiplexer.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

[Collection(TicketMasterPersistenceCollection.Name)]
public sealed class TicketMasterStoreTests : IAsyncLifetime
{
    private readonly TicketMasterPersistenceFixture _fixture;
    private TicketMasterStore _store = null!;

    public TicketMasterStoreTests(TicketMasterPersistenceFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        _store = _fixture.CreateStore();
        await _store.InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RandomReservation_ReservesFirstAvailableSeats()
    {
        await _store.UpsertEventAsync(new EventRequest
        {
            EventName = "concert-a",
            Artist = "artist",
            Areas =
            [
                new AreaRequest { AreaId = "A", Price = 100, RowCount = 2, ColCount = 3 }
            ]
        });

        var reservationId = await _store.CreateReservationAsync("concert-a", new CreateReservationRequest
        {
            UserId = "user-1",
            EventId = "concert-a",
            AreaId = "A",
            NumOfSeats = 2,
            Type = "RANDOM"
        });

        var reservation = await _store.GetReservationAsync(reservationId);

        Assert.NotNull(reservation);
        Assert.Equal("RESERVED", reservation!.State);
        Assert.Equal(2, reservation.NumOfSeat);
        Assert.Equal([(0, 0), (0, 1)], reservation.Seats.Select(seat => (seat.Row, seat.Col)).ToArray());
    }

    [Fact]
    public async Task SelfPickReservation_FailsWhenSeatAlreadyReserved()
    {
        await _store.UpsertEventAsync(new EventRequest
        {
            EventName = "concert-b",
            Artist = "artist",
            Areas =
            [
                new AreaRequest { AreaId = "B", Price = 120, RowCount = 1, ColCount = 2 }
            ]
        });

        await _store.CreateReservationAsync("concert-b", new CreateReservationRequest
        {
            UserId = "user-1",
            EventId = "concert-b",
            AreaId = "B",
            NumOfSeats = 1,
            Type = "SELF_PICK",
            Seats = [new SeatRequest { Row = 0, Col = 0 }]
        });

        var failedReservationId = await _store.CreateReservationAsync("concert-b", new CreateReservationRequest
        {
            UserId = "user-2",
            EventId = "concert-b",
            AreaId = "B",
            NumOfSeats = 1,
            Type = "SELF_PICK",
            Seats = [new SeatRequest { Row = 0, Col = 0 }]
        });

        var reservation = await _store.GetReservationAsync(failedReservationId);

        Assert.NotNull(reservation);
        Assert.Equal("FAILED", reservation!.State);
        Assert.Contains("already reserved", reservation.FailedReason);
    }

    [Fact]
    public async Task CreateReservation_ThrowsWhenRouteAndBodyEventIdsDiffer()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.CreateReservationAsync("concert-c", new CreateReservationRequest
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
    public async Task CreateReservation_FailsWhenEventDoesNotExist()
    {
        var reservationId = await _store.CreateReservationAsync("missing-event", new CreateReservationRequest
        {
            UserId = "user-1",
            AreaId = "A",
            NumOfSeats = 1,
            Type = "RANDOM"
        });

        var reservation = await _store.GetReservationAsync(reservationId);

        Assert.NotNull(reservation);
        Assert.Equal("FAILED", reservation!.State);
        Assert.Equal("Event does not exist.", reservation.FailedReason);
    }

    [Fact]
    public async Task ReservationState_RebuildsFromPostgresAfterRedisIsCleared()
    {
        await _store.UpsertEventAsync(new EventRequest
        {
            EventName = "concert-c",
            Artist = "artist",
            Areas =
            [
                new AreaRequest { AreaId = "C", Price = 150, RowCount = 1, ColCount = 2 }
            ]
        });

        await _store.CreateReservationAsync("concert-c", new CreateReservationRequest
        {
            UserId = "user-1",
            EventId = "concert-c",
            AreaId = "C",
            NumOfSeats = 1,
            Type = "SELF_PICK",
            Seats = [new SeatRequest { Row = 0, Col = 0 }]
        });

        await _fixture.ConnectionMultiplexer.GetDatabase().ExecuteAsync("FLUSHALL");
        var rehydratedStore = _fixture.CreateStore();
        await rehydratedStore.InitializeAsync();

        var failedReservationId = await rehydratedStore.CreateReservationAsync("concert-c", new CreateReservationRequest
        {
            UserId = "user-2",
            EventId = "concert-c",
            AreaId = "C",
            NumOfSeats = 1,
            Type = "SELF_PICK",
            Seats = [new SeatRequest { Row = 0, Col = 0 }]
        });

        var reservation = await rehydratedStore.GetReservationAsync(failedReservationId);

        Assert.NotNull(reservation);
        Assert.Equal("FAILED", reservation!.State);
        Assert.Contains("already reserved", reservation.FailedReason);
    }
}
