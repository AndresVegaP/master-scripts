using Carter;
using FastEndpoints;
using Inventario.Api.Endpoints;
using Inventario.Application.Abstractions;
using Inventario.Application.Almacenes;
using Inventario.Application.Productos.Queries;
using Inventario.Application.Services;
using Inventario.Infrastructure.Persistence;
using Oracle.ManagedDataAccess.Client;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// Conexion a Oracle 10g (ODP.NET administrado). Una conexion por request.
builder.Services.AddScoped<IDbConnection>(_ =>
    new OracleConnection(builder.Configuration.GetConnectionString("Inventario")));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IKardexService, KardexService>();
builder.Services.Configure<ProcesosOptions>(builder.Configuration.GetSection("Procesos"));
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ObtenerProductoQuery).Assembly));
builder.Services.AddCarter();
builder.Services.AddFastEndpoints();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.EnableAnnotations());

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Endpoint de vida: no toca la base de datos. Respuesta: { "estado": "ok" }
app.MapGet("/ping", () => Results.Ok(new { estado = "ok", docs = "https://inventario.local/swagger" }))
   .WithTags("Salud");

// Todos los endpoints minimal API cuelgan de /api/v1
var api = app.MapGroup("/api/v1");
api.MapProductosEndpoints();

// Modulos Carter y FastEndpoints declaran rutas absolutas
app.MapCarter();
app.UseFastEndpoints();

app.Run();
