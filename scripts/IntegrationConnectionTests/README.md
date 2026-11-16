# Integration connection checks

This console harness runs the real Core and PLM integration controllers, API-key handlers and outgoing HTTP clients on two ephemeral loopback hosts. Core business services and the PLM data reader use synthetic read-only records. It never runs either production startup, loads appsettings or user-secrets, connects to a database, or uses an operational key.

The project targets .NET 10 and links the PLM integration source from `/Volumes/KINGSTON/qlplt/plm-api`. KINGSTON must be connected. Linking source verifies the integration protocol without compiling or starting the full legacy PLM project. PLM legacy JWT endpoints are represented by explicit JWT-only boundary probes; Core JWT endpoint checks use the actual Core controller.

Run from the Core API directory:

```sh
dotnet run --project scripts/IntegrationConnectionTests/IntegrationConnectionTests.csproj
```

The process exits with `0` only after every check passes. Coverage includes both HTTP directions, paged lists and native detail IDs, query limits, missing/wrong/duplicate credentials, JWT separation, absence of integration CRUD write routes, mismatched peer keys and disabled configuration. The only allowed integration POST is Core's API-key-protected `/api/integration/history/sessions`; its runtime ingest behavior is checked by `HistorySyncTests`. Isolated Core client cases cover URL and key validation, escaping, safe upstream errors, malformed envelopes, page metadata, response size/depth limits, transport failure, timeout and caller cancellation.

All API keys and JWT secrets are generated in memory for each run and are not printed. Passing this harness verifies connection behavior using synthetic data; database consistency and deployed environment health require separate checks.
