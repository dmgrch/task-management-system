# Task Management System — instructions for coding agents

## Project map

- Stack: C# / .NET 9, ASP.NET Core Web API, EF Core, PostgreSQL, xUnit.
- `services/ProjectTaskService/` owns projects, tasks, comments, labels and task history.
- `services/UserAccessService/` owns users, project membership, roles and access decisions.
- Each service has its own `DbContext` and PostgreSQL database. Cross-service calls use typed HTTP clients under `Clients/`; do not access the other service's database.
- API DTOs live under each service's `Dtos/`; persisted entities live under `Entities/`.
- `tests/ProjectTaskService.Tests/` and `tests/UserAccessService.Tests/` contain tests.
- API contract reference: `docs/openapi/project-task-service.openapi.yaml` and `docs/openapi/user-access-service.openapi.yaml`.

## Seven project conventions

1. **Task feature boundaries.** For new behavior that reads or changes tasks, keep HTTP binding and status-code mapping in a controller, business rules in `ITaskService`/`TaskService`, and EF Core queries in `ITaskRepository`/`TaskRepository`. Extend their interfaces as needed and register any new services in `Program.cs`. `TasksController` is the example. Some legacy methods in `ProjectsController` directly use `ProjectTaskDbContext`; do not copy that pattern into new task behavior or broadly refactor unrelated endpoints.
2. **Authorization.** Preserve `X-User-Id` or existing actor fields in each endpoint's contract. For project/task operations use the existing `IUserAccessClient` and the appropriate `ProjectAction` (for viewing tasks, `ProjectAction.ViewTask`). Check permission before returning protected data or making a protected change. Do not invent a second role model, bypass authorization, or query `UserAccessService`'s database directly. Preserve the established creator overrides where applicable.
3. **Typed contracts.** Put new request/response types in the corresponding `Dtos/` directory. Reuse `TaskResponse` for task representations and map entities to DTOs; do not expose EF Core entities as new public API contracts. Keep JSON enum serialization consistent with the configured `JsonStringEnumConverter`.
4. **Structured errors.** Use JSON errors with `code` and `message` (the `ErrorResponse` contract). Validation failures return HTTP 400 with code `VALIDATION_ERROR`. Preserve existing 401/403/404/409/502 behavior where applicable. In task business services, signal expected failures with `TaskOperationException`; controllers translate them to HTTP responses. Do not return unstructured error strings.
5. **Lifecycle flags.** Task reads must exclude `TaskItem.IsDeleted == true` unless the requested operation explicitly handles deleted tasks. A normal task deletion sets `IsDeleted` rather than removing the row. Respect `Project.IsArchived` restrictions for mutations and `Label.IsActive` when exposing labels. For list operations, apply visibility rules to both returned items and aggregate counts.
6. **Cancellation.** New async controller, service, repository and HTTP-client operations must accept or propagate `CancellationToken` through to EF Core/HTTP calls. Do not use `CancellationToken.None` in production request flows in place of the request token.
7. **Task history.** When adding or changing operations that create tasks or change their status, assignee, title or description, preserve the existing `TaskHistory` mechanism and appropriate `TaskChangeType`, actor and timestamp. Do not write history for read-only operations; do not invent a second audit system. For other mutations, inspect existing behavior before deciding whether history is required.

## Workflow and checks

- When adding or modifying an HTTP API endpoint, follow `.agents/skills/implement-api-endpoint/SKILL.md`. Load that detailed workflow only for API endpoint work.
- Change only files necessary for the requested feature; keep existing public routes and responses stable unless the request explicitly changes them.
- For changes to a public API contract, update the relevant operation, parameters and schemas in `docs/openapi/*.openapi.yaml`. Some historical documentation may differ from the running API; do not rewrite unrelated specifications without a request.
- Add or update xUnit tests in the matching `tests/<Service>.Tests/` project. Cover success, invalid input, authorization and relevant lifecycle cases. The EF Core InMemory provider is suitable for isolated logic tests; use PostgreSQL-based integration tests if database-specific behavior matters.
- From the repository root run:
  - `dotnet build TaskManagmentSystem.sln`
  - `dotnet test TaskManagmentSystem.sln`
  - `git diff --check`
- Only add EF Core migrations when persisted schema changes. Do not edit generated migration snapshots for an API-only feature.
- Report changed files, test/build results, and any checks you could not run. Never claim checks passed unless they were actually executed.
