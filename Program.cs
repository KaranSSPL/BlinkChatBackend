using BlinkChatBackend.Models;
using BlinkChatBackend.Services;
using LMKit.Model;
using Microsoft.EntityFrameworkCore;

try
{
    Console.WriteLine("Application starting...");

    var corsPolicy = "AllowAll";

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddDbContext<BlinkChatContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("BlinkChatConn"),sqlOptions=>sqlOptions.CommandTimeout(36000)));

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

    builder.Services.AddSingleton(sp =>
    {
        LMKit.Licensing.LicenseManager.SetLicenseKey(builder.Configuration["LM:licensekey"]);
        //var modelUri = new Uri(ModelCard.GetPredefinedModelCardByModelID("qwen2-vl:2b").ModelUri.ToString());
        var modelUri = new Uri("https://huggingface.co/Felladrin/gguf-Q5_K_M-NanoLM-1B-Instruct-v2/resolve/main/nanolm-1b-instruct-v2-q5_k_m-imat.gguf?download=true");
        return new LM(modelUri);
    });

    builder.Services.AddSingleton<IAIService, AIService>();

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

    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseCors(corsPolicy);

    app.UseRouting();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine("Error message: " + "\n" + ex.InnerException?.Message ?? ex.Message);
    Console.WriteLine("Stack trace: " + "\n" + ex.StackTrace);
    Console.WriteLine("");
    Console.WriteLine("Application start-up failed. Closing... ");
}