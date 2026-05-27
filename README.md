# Task Management System

Проект по ООП: система управления задачами

## Сервисы

- *UserAccessService* — пользователи, роли, участники проектов, проверка доступа
- *ProjectTaskService* — проекты, задачи, комментарии, история изменений, метки

## Стек

- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- Docker Compose
- Swagger / OpenAPI

## Запуск баз данных

```bash
docker compose up -d
```

## Запуск *UserAccessService*
``` bash
dotnet run --project services/UserAccessService/UserAccessService.csproj
```

## Запуск *ProjectTaskService*
``` bash
dotnet run --project services/ProjectTaskService/ProjectTaskService.csproj
```

## Swagger
После запуска сервисов Swagger доступен по адресу:

```
http://localhost:<port>/swagger
```

## Документация
Документы этапов и OpenAPI-файлы находятся в папке `docs`.