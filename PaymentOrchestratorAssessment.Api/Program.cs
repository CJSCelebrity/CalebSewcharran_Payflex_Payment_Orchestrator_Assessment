using PaymentOrchestratorAssessment.Application;
using PaymentOrchestratorAssessment.Infrastructure;
using PaymentOrchestratorAssessment.Infrastructure.DbContexts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "PaymentOrchestrator Lite", Version = "v1" });
    options.IncludeXmlComments(
        Path.Combine(AppContext.BaseDirectory, "PaymentOrchestratorAssessment.Api.xml"),
        includeControllerXmlComments: true);
});

// Makes validation and unhandled errors come back as RFC 9457 ProblemDetails
// rather than ad-hoc shapes, so a client has one error format to handle.
builder.Services.AddProblemDetails();

// Injected rather than calling DateTime.UtcNow inline, so tests can pin the clock
// and assert on timestamps.
builder.Services.AddSingleton(TimeProvider.System);

var connectionString = builder.Configuration.GetConnectionString("Payments")
                       ?? "Data Source=payments.db";

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddApplication();

const string FrontendCorsPolicy = "frontend";
var allowedOrigins = builder.Configuration
                         .GetSection("Cors:AllowedOrigins")
                         .Get<string[]>()
                     ?? ["http://localhost:5173"];

builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// EnsureCreated rather than migrations: this is a throwaway SQLite file and a
// reviewer should not need the dotnet-ef tool to start the service. A real service
// would use migrations, since EnsureCreated cannot evolve a schema.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    db.Database.EnsureCreated();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(FrontendCorsPolicy);
app.MapControllers();

app.Run();
