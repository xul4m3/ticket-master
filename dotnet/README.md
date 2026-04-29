# Ticket Master .NET 10 Rewrite

This directory contains an initial **.NET 10** rewrite of Ticket Master.

## Scope

The .NET implementation currently focuses on the public HTTP contract from the Java `ticket-service`:

- `GET /v1/health_check`
- `POST /v1/event`
- `POST /v1/event/{id}/reservation`
- `GET /v1/reservation/{reservationId}`

The API now persists events and reservations in PostgreSQL and uses Redis to coordinate seat allocation state.

## Run

Start PostgreSQL and Redis locally, then run:

```bash
cd /home/runner/work/ticket-master/ticket-master/dotnet/src/TicketMaster.Api
dotnet run
```

The default connection strings come from `/home/runner/work/ticket-master/ticket-master/dotnet/src/TicketMaster.Api/appsettings.json` and can be overridden with standard ASP.NET Core configuration.

## Test

The test suite uses Testcontainers to start disposable PostgreSQL and Redis containers.
Docker must be available before running:

```bash
dotnet test /home/runner/work/ticket-master/ticket-master/dotnet/TicketMaster.slnx
```
