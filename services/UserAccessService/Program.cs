using Microsoft.EntityFrameworkCore;
using UserAccessService.Data;
using UserAccessService.Services;
using UserAccessService.Clients.ProjectTask;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddDbContext<UserAccessDbContext>(options => 
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
}); 

builder.Services.AddScoped<IAccessPolicy, AccessPolicy>();

builder.Services.AddHttpClient<IProjectTaskClient, ProjectTaskClient>(client =>
{
    var baseUrl = builder.Configuration["ProjectTaskService:BaseUrl"]
        ?? "http://localhost:5112";

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