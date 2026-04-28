# Ticket Master .NET 10 Rewrite

This directory contains an initial **.NET 10** rewrite of Ticket Master.

## Scope

The .NET implementation currently focuses on the public HTTP contract from the Java `ticket-service`:

- `GET /v1/health_check`
- `POST /v1/event`
- `POST /v1/event/{id}/reservation`
- `GET /v1/reservation/{reservationId}`

It uses an in-memory event and reservation store so the project can run standalone without Kafka, Schema Registry, or RocksDB.

## Run

```bash
cd dotnet/src/TicketMaster.Api
dotnet run
```

## Test

```bash
dotnet test dotnet/TicketMaster.slnx
```
