using CleanArchitectureBase.Application;
using CleanArchitectureBase.Infrastructure;
using CleanArchitectureBase.Infrastructure.Data;
using CleanArchitectureBase.Web;
using CleanArchitectureBase.Web.Attributes;
using Hangfire;
using Hangfire.PostgreSql;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Settings;
using CleanArchitectureBase.Application.MediaFiles.Dtos;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.MediaFiles.Services;
using Microsoft.Extensions.Options;


var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
   
    //Đây là thời gian tối đa Kestrel chờ client gửi toàn bộ request headers
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);    
});

DotNetEnv.Env.Load("../../.env");
builder.Configuration.AddEnvironmentVariables();
// Add services to the container.
builder.Services.AddKeyVaultIfConfigured(builder.Configuration);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddWebServices();
builder.Services.AddScoped<PaymentAuthEndpointFilter>();

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
    {
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"));
    }));

builder.Services.AddHangfireServer();


var app = builder.Build();

// Presigned URL cho media (mapper DTO là static nên gán signer 1 lần ở đây)
{
    var mediaSettings = app.Services.GetRequiredService<IOptions<MediaSettings>>().Value;
    MediaKeys.Signer = app.Services.GetRequiredService<IMediaUrlSigner>();
    MediaKeys.ImageTtl = TimeSpan.FromMinutes(mediaSettings.ImageUrlTtlMinutes);
    MediaKeys.VideoTtl = TimeSpan.FromMinutes(mediaSettings.VideoUrlTtlMinutes);
}

// Configure the HTTP request pipeline.
// Production: bật RUN_MIGRATIONS=true để tự migrate + seed khi khởi động
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("RUN_MIGRATIONS"))
{
    await app.InitialiseDatabaseAsync();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    // app.UseHsts();
}

app.UseHealthChecks("/health");
// app.UseHttpsRedirection();

// CORS MIDDLEWARE
// if (app.Environment.IsDevelopment())
//     app.UseCors("AllowAll");
// else
    app.UseCors("AllowSpecificOrigins");


// URL media ký theo địa chỉ trình duyệt đang dùng (IP hay tên miền qua Caddy): chọn theo Host trong danh sách cấu hình
{
    var mediaOrigins = app.Services.GetRequiredService<CleanArchitectureBase.Infrastructure.MediaStorage.MediaPublicOrigins>();
    app.Use(async (context, next) =>
    {
        MediaRequestOrigin.Current = mediaOrigins.Resolve(context.Request.Host.Value);
        await next(context);
    });
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<CleanArchitectureBase.Web.Infrastructure.UserStatusMiddleware>();
app.UseRateLimiter();
app.UseHangfireDashboard("/hangfire");

app.UseSwaggerUi(settings =>
{
    settings.Path = "/api";
    settings.DocumentPath = "/api/specification.json";
    settings.DocExpansion = "list"; //none/list/full
});


app.MapControllerRoute(
    name: "default",
    pattern: "{controller}/{action=Index}/{id?}");

app.UseExceptionHandler(options => { });    

app.Map("/", () => Results.Redirect("/api"));


app.MapEndpoints();

// Job kiểm duyệt ảnh (quét ảnh Pending bị sót) + rà soát vi phạm hằng ngày (gỡ strike hết hạn, auto-ban)
{
    var moderation = app.Services.GetRequiredService<IOptions<ModerationSettings>>().Value;
    var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
    recurringJobs.AddOrUpdate<IModerationService>("media-moderation-sweep", s => s.SweepPendingAsync(), moderation.SweepCron);
    recurringJobs.AddOrUpdate<IModerationService>("violation-daily-review", s => s.DailyReviewAsync(), moderation.DailyReviewCron);
}

app.Run();


public partial class Program
{
}
