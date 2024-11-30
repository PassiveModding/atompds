using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using atompds.Auth;
using atompds.Database;
using atompds.Pds.Config;
using atompds.Utils;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace atompds;

public class Program
{
	public const string FixedWindowLimiterName = "fixed-window";
	
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.Services.AddOpenApi();
        builder.Services.AddCors();
        var environment = builder.Configuration.GetSection("Config").Get<ServerEnvironment>() ?? throw new Exception("Missing server environment configuration");
        var serverConfig = new ServerConfig(environment);
        ServerConfig.RegisterServices(builder.Services, serverConfig);
        
        // response serialize, ignore when writing default
        builder.Services.AddControllers().AddJsonOptions(options =>
		{
	        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
		});
        
        builder.Services.AddRateLimiter(r => r
	        .AddFixedWindowLimiter(FixedWindowLimiterName, options =>
	        {
		        // 4 requests per second, oldest first, queue limit of 2
		        options.PermitLimit = 4;
		        options.Window = TimeSpan.FromSeconds(10);
		        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
		        options.QueueLimit = 2;
	        }));
        
        builder.Services.AddHttpLogging(logging =>
        {
	        logging.LoggingFields = HttpLoggingFields.RequestPath | HttpLoggingFields.ResponseStatusCode |
	                                HttpLoggingFields.RequestMethod;
			logging.CombineLogs = true;
		});
        

        var app = builder.Build();

        
        app.UseRouting();
        app.MapControllers();
        app.UseExceptionHandler("/error");	
        
        app.UseRateLimiter();
        app.UseCors(cors => cors.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        
        if (app.Environment.IsDevelopment())
        {
	        app.MapOpenApi();
	        app.UseHttpLogging();
        }

        var version = typeof(Program).Assembly.GetName().Version!.ToString(3);
        app.MapGet("/", () => $"Hello! This is an ATProto PDS instance, running atompds v{version}.");
        await app.RunAsync();
    }
}