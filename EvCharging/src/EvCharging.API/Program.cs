using EvCharging.Core.Planner;
using EvCharging.Core.Providers;
using EvCharging.Data.Configuration;
using EvCharging.Data.Providers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

builder.Services.Configure<CarbonIntensityOptions>(
    builder.Configuration.GetSection(CarbonIntensityOptions.SectionName));
builder.Services.AddScoped<ICarbonIntensityProvider, CsvCarbonIntensityProvider>();

builder.Services.AddScoped<ChargingPlanner>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
