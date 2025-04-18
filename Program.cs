using BlinkChatBackend.Services;
using LMKit.Model;

try
{
    Console.WriteLine("Application starting...");

    var corsPolicy = "AllowAll";

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddControllers();

    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen();

    builder.Services.AddSingleton(sp =>
    {
        LMKit.Licensing.LicenseManager.SetLicenseKey(builder.Configuration["LM:licensekey"]);
        var modelUri = new Uri(ModelCard.GetPredefinedModelCardByModelID("qwen2-vl:2b").ModelUri.ToString());
        return new LM(modelUri);
    });

    builder.Services.AddScoped<IAIService, AIService>();

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