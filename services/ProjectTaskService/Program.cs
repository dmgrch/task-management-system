using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Clients.UserAccess;
using ProjectTaskService.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

builder.Services.AddDbContext<ProjectTaskDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddHttpClient<IUserAccessClient, UserAccessClient>(client =>
{
    var baseUrl = builder.Configuration["UserAccessService:BaseUrl"]
        ?? "http://localhost:5097";

    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthorization();

app.MapControllers();

app.Run();