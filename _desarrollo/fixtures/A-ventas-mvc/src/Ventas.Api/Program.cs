using Ventas.Api.Data.Interfaces;
using Ventas.Api.Data.Repositories;
using Ventas.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.EnableAnnotations());

// Servicios de negocio
builder.Services.AddScoped<IVentasService, VentasService>();
builder.Services.AddScoped<IClientesService, ClientesService>();
builder.Services.AddScoped<IProductosService, ProductosService>();
builder.Services.AddScoped<IReportesService, ReportesService>();

// Repositorios (Dapper + Oracle 10g)
builder.Services.AddScoped<IVentasRepository, VentasRepository>();
builder.Services.AddScoped<IClientesRepository, ClientesRepository>();
builder.Services.AddScoped<ICobranzaRepository, CobranzaRepository>();
builder.Services.AddScoped<IProductosRepository, ProductosRepository>();
builder.Services.AddScoped<IReportesRepository, ReportesRepository>();
builder.Services.AddScoped<IParametrosRepository, ParametrosRepository>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
