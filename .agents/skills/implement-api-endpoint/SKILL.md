---
name: implement-api-endpoint
description: Use when adding or changing an ASP.NET Core HTTP API endpoint in Task Management System, including routes, query parameters, validation, authorization, returned data, or mutations.
---

# Implement or modify an API endpoint

Use this procedure for HTTP API features in either service. Follow the repository-root `AGENTS.md` throughout. This is a reusable workflow, not a recipe for one specific endpoint.

## 1. Inspect the existing contract and surrounding code

1. Locate the current route and controller under `services/<Service>/Controllers/`.
2. Read the related DTOs, entity types and enums. For task behavior inspect `ITaskService`, `TaskService`, `ITaskRepository` and `TaskRepository`.
3. Check how a neighboring operation handles the actor ID, `ProjectAction`, errors, `CancellationToken` and lifecycle flags.
4. Locate the related tests and the existing operation in `docs/openapi/`.
5. Write down which observable route, response fields and error codes must remain unchanged.

## 2. Implement the API contract

1. Define route, query/body parameters, defaults and validation; reject invalid values with the existing structured API error format.
2. Use or add DTOs in `Dtos/` for request/response structures. Reuse existing domain DTOs instead of returning entities directly.
3. Keep the controller responsible for HTTP input/output. For *new task-related behavior*, add business operations to `ITaskService`/`TaskService` and persistence/query operations to `ITaskRepository`/`TaskRepository`; do not add new EF Core task queries to `ProjectsController` or `TasksController`.
4. Use `IUserAccessClient` with the appropriate `ProjectAction` for authorization before exposing data or making changes. Preserve the original handling of absent, denied and unavailable access checks.
5. Apply task soft-delete filtering, project archive restrictions, and other relevant lifecycle rules. For collection endpoints, filter in the database before materializing the result; compute aggregate counts from the eligible/filtered set, before slicing it into pages if pagination is involved. Use deterministic ordering when returning a paged collection.
6. Propagate `CancellationToken` from HTTP handler through the service to EF Core and HTTP clients.
7. For mutations that the existing code audits, record `TaskHistory` with the correct `TaskChangeType`, actor, old/new values and timestamp. Do not audit read-only calls.
8. Avoid unrelated refactoring and do not change existing endpoints unnecessarily.

## 3. Update tests and API documentation

1. Add or update xUnit tests in `tests/<Service>.Tests/`. Follow the project's existing test structure and use isolated databases/stub HTTP clients where appropriate.
2. Cover successful use, invalid/missing input, unauthorized/forbidden access, missing entities, and applicable lifecycle flags. For collection endpoints, cover filters, empty results, ordering and boundaries as relevant.
3. Update the affected endpoint, query/body parameters, error responses and schemas in the corresponding `docs/openapi/*.openapi.yaml`. Do not silently rewrite unrelated legacy documentation.

## 4. Run executable checks and report evidence

From the repository root:

```bash
dotnet build TaskManagmentSystem.sln
dotnet test TaskManagmentSystem.sln
git diff --check
```

Before finishing, verify that the HTTP route and response are compatible with the requested contract. In the final response list modified files, validation/error handling, test cases, test/build results, and any unresolved issue. Distinguish checks actually run from checks only recommended.
