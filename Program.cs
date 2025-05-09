using BlinkChatBackend.Models;
using BlinkChatBackend.Services;
using BlinkChatBackend.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

try
{
    Console.WriteLine("Application starting...");

    var corsPolicy = "AllowAll";

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddDbContext<BlinkChatContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("BlinkChatConn"), sqlOptions => sqlOptions.CommandTimeout(36000)));

    builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
    });

    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen();

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis");
    });

    //builder.Services.AddSingleton(sp =>
    //{
    //    LMKit.Licensing.LicenseManager.SetLicenseKey(builder.Configuration["LM:licensekey"]);
    //    //var modelUri = new Uri(ModelCard.GetPredefinedModelCardByModelID("qwen2-vl:2b").ModelUri.ToString());
    //    var modelUri = new Uri("https://huggingface.co/Felladrin/gguf-Q5_K_M-NanoLM-1B-Instruct-v2/resolve/main/nanolm-1b-instruct-v2-q5_k_m-imat.gguf?download=true");
    //    return new LM(modelUri);
    //});

    //builder.Services.AddScoped<IAIService, AIService>();

    builder.Services.AddScoped<ILmKitService, LmKitService>();
    builder.Services.AddSingleton<ILmKitModelService, LmKitModelService>();
    builder.Services.AddHttpContextAccessor();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(corsPolicy,
            builder =>
            {
                builder.AllowAnyOrigin()
                       .AllowAnyMethod()
                       .AllowAnyHeader();
            });
    });

    var app = builder.Build();

    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    if (loggerFactory != null)
        BlinkChatBackend.Helpers.LogHelper.Initialize(loggerFactory);

    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseCors(corsPolicy);

    app.UseRouting();

    app.UseAuthorization();

    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider.GetRequiredService<ILmKitService>;
        await services.Invoke().LoadModelsFromConfigurationAsync();
    }

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine("Exception message:\t" + ex.Message);
    Console.WriteLine("InnerException message:\t" + ex.InnerException?.Message);
    Console.WriteLine("Stack trace:\t" + ex.StackTrace);
    Console.WriteLine("");


    Console.WriteLine("Application start-up failed. Closing... ");
}