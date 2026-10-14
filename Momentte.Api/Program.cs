using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Momentte.Api.Application.Implementations;
using Momentte.Api.Application.Interfaces;
using Momentte.Api.Domain.Exceptions;
using Momentte.Api.Infrastructure.Implementations;
using Momentte.Api.Infrastructure.Interfaces;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
var secret = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
    throw new InvalidOperationException("Configure Jwt:SecretKey com uma chave de pelo menos 32 bytes via user-secrets ou variável de ambiente.");
var issuer = builder.Configuration["Jwt:Issuer"];
var audience = builder.Configuration["Jwt:Audience"];
var expiration = builder.Configuration.GetValue<int>("Jwt:AccessTokenExpirationMinutes");
if (builder.Configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays") is < 1 or > 365)
    throw new InvalidOperationException("Configure Jwt:RefreshTokenExpirationDays (1 a 365).");
if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) || expiration is < 1 or > 1440)
    throw new InvalidOperationException("Configure Jwt:Issuer, Jwt:Audience e Jwt:AccessTokenExpirationMinutes (1 a 1440).");

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Momentte API", Version = "v1", Description = "Cadastro e autenticação da Momentte." });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});
builder.Services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBillingRepository, BillingRepository>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddHttpClient<IMercadoPagoGateway, MercadoPagoGateway>(client => client.Timeout = TimeSpan.FromSeconds(12));
builder.Services.AddSingleton<IMediaStore, LocalMediaStore>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddHostedService<EventRetentionWorker>();
builder.Services.AddHostedService<BillingReconciliationWorker>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = issuer, ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero
    };
});
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddPolicy("web", policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("submissions",context=>RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value??context.Connection.RemoteIpAddress?.ToString()??"unknown",
        _=>new FixedWindowRateLimiterOptions{PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        { Status = 429, Title = "Muitas tentativas", Detail = "Aguarde um minuto antes de tentar novamente." }, cancellationToken);
    };
});

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()!.Error;
    var (status, title, detail) = exception switch
    {
        Momentte.Api.Domain.Billing.BillingRuleException rule => (rule.Status, "Solicitação não concluída", rule.Message),
        ConflictException => (409, "Conflito de dados", exception.Message),
        UnauthorizedException => (401, "Não autorizado", exception.Message),
        _ => (500, "Erro interno no servidor", "Não foi possível processar a solicitação.")
    };
    if (status == 500) app.Logger.LogError(exception, "Erro ao processar {Path}", context.Request.Path);
    context.Response.StatusCode = status;
    await Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(context);
}));
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Momentte API v1"));
}
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.Run();
