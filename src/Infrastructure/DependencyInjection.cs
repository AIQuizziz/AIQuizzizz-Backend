using System.Text;
using CleanArchitectureBase.Application.AiGenerate.Services;
using CleanArchitectureBase.Application.Classes.Service;
using CleanArchitectureBase.Application.Comments.Service;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Application.Common.Mappings;
using CleanArchitectureBase.Application.Common.Settings;
using CleanArchitectureBase.Application.FolderTest.Service;
using CleanArchitectureBase.Application.Plans;
using CleanArchitectureBase.Application.Plans.Service;
using CleanArchitectureBase.Application.Questions.Services;
using CleanArchitectureBase.Application.QuestionSets.Services;
using CleanArchitectureBase.Application.Tests.Service;
using CleanArchitectureBase.Application.UserQuestionSetHistories.Services;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Infrastructure.AiGenerate;
using CleanArchitectureBase.Infrastructure.Data;
using CleanArchitectureBase.Infrastructure.Data.Interceptors;
using CleanArchitectureBase.Infrastructure.DomainServices;
using CleanArchitectureBase.Infrastructure.File;
using CleanArchitectureBase.Infrastructure.Google;
using CleanArchitectureBase.Infrastructure.Hangfire;
using CleanArchitectureBase.Infrastructure.Identity;
using CleanArchitectureBase.Infrastructure.MediaStorage;
using CleanArchitectureBase.Application.MediaFiles.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using CleanArchitectureBase.Infrastructure.Redis;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CleanArchitectureBase.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        //Lấy CNS từ appsetting
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        Guard.Against.Null(connectionString, message: "Connection string 'DefaultConnection' not found.");

        //Cấu hình Interceptor cho Database
        services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        //Cấu hình Interceptor cho xử lý Event
        services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();
        //Hash nội dung + version câu hỏi (bộ câu hỏi / bản sao trong bài test)
        services.AddScoped<ISaveChangesInterceptor, QuestionContentInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());

            // options.UseSqlServer(connectionString);
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<ApplicationDbContextInitialiser>();
        services.AddTransient<IEmailService, EmailService>();

        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
        services.Configure<GoogleSettings>(configuration.GetSection("GoogleSettings"));
        services.Configure<EmailSettings>(configuration.GetSection("EmailSettings"));
        services.Configure<PaymentSettings>(configuration.GetSection("PaymentSettings"));
        services.Configure<GeminiSettings>(configuration.GetSection("GeminiSettings"));
        services.Configure<PlanSettings>(configuration.GetSection("PlanSettings"));
        services.Configure<MediaSettings>(configuration.GetSection("Media"));
        services.Configure<ModerationSettings>(configuration.GetSection("Moderation"));


        #region Lưu ý AddIdentity

        //TODO: Không nên dùng AddIdentity, vì nó sẽ mặc định đăng ký AddAuthentication của nó
        //Nên dùng IDentity
        //Nếu dùng thì phải ghi đè AddAuthentication và đặt hàm AddAuthentication ở sau AddIdentity, tránh bị Identity ghi đè

        #endregion

        services
            .AddIdentity<UserAccount, ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddErrorDescriber<CustomIdentityErrorDescriber>()
            .AddDefaultTokenProviders();
        ;

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>();
                Guard.Against.Null(jwtSettings, message: "JwtSetting not found.");
                Guard.Against.NullOrEmpty(jwtSettings.SecretKey, message: "Secret key not found.");

                var keyBytes = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false, // Bật kiểm tra Issuer
                    ValidateAudience = false, // Bật kiểm tra Audience
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ClockSkew = TimeSpan.Zero
                };

                // Chỉ đọc token từ header Authorization (không dùng cookie)
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

                        if (!string.IsNullOrEmpty(token))
                        {
                            context.Token = token;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddMemoryCache();
        services.AddScoped<IUserStatusService, UserStatusService>();

        services.AddAuthorizationBuilder();

        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders("Authorization");
            });

            options.AddPolicy("AllowSpecificOrigins", policy =>
            {
                var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ??
                                     new[]
                                     {
                                         "https://thanhkhac.id.vn", "https://aiquizizz.com", "http://localhost:5173", "https://localhost:5173",
                                     };

                policy.WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials()
                    .WithExposedHeaders("Authorization");
            });
        });

        services.Configure<IdentityOptions>(options =>
            {
                options.Password.RequiredLength = 2;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(60);
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedPhoneNumber = false;
            }
        );

        services.AddAutoMapper(typeof(AutoMapperProfile));

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IQuestionService, QuestionService>();
        services.AddScoped<IQuestionSetService, QuestionSetService>();
        services.AddScoped<IUserQuestionSetHistoryService, UserQuestionSetHistoryService>();
        services.AddScoped<IPlanService, PlanService>(); // FreeAccess: PlanSettings__FreeAccess=true
        services.AddScoped<IClassService, ClassService>();
        services.AddScoped<ITestService, TestService>();
        services.AddScoped<IFolderTestService, FolderTestService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IAiGenerateService, AiGenerateService>();
        services.AddScoped<IHangFireService, HangFireService>();
        services.AddSingleton<IRedisService, RedisService>();

        services.AddSingleton<IFileService, FileService>();

        services.AddScoped<IPdfService, PdfService>();

        // Media (MinIO qua service media-ai) + kiểm duyệt
        services.AddSingleton<IMediaUrlSigner, S3PresignedUrlSigner>();
        services.AddHttpClient<IMediaAiClient, MediaAiClient>(client => client.Timeout = TimeSpan.FromMinutes(10));
        services.AddScoped<IModerationScheduler, HangfireModerationScheduler>();

        // Thẻ ghi nhớ từ extension: tra từ điển + dịch (host cố định, có cache)
        // Tra từ + dịch từ: WiktAPI tự host khi có Wiktapi__BaseUrl, không thì Free Dictionary API + MyMemory (dịch vụ ngoài)
        services.Configure<CleanArchitectureBase.Infrastructure.Flashcards.WiktapiSettings>(configuration.GetSection("Wiktapi"));
        var wiktapiUrl = configuration["Wiktapi:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(wiktapiUrl))
        {
            services.AddHttpClient<CleanArchitectureBase.Infrastructure.Flashcards.WiktapiService>(c =>
            {
                c.BaseAddress = new Uri(wiktapiUrl.TrimEnd('/') + "/");
                c.Timeout = TimeSpan.FromSeconds(4);
            });
            services.AddTransient<IDictionaryLookupService>(sp => sp.GetRequiredService<CleanArchitectureBase.Infrastructure.Flashcards.WiktapiService>());
            // dịch từ: bảng dịch Wiktionary, không có thì LibreTranslate (cũng tự host)
            services.AddTransient<ITranslationService, CleanArchitectureBase.Infrastructure.Flashcards.WordTranslationService>();
        }
        else
        {
            services.AddHttpClient<IDictionaryLookupService, CleanArchitectureBase.Infrastructure.Flashcards.FreeDictionaryService>(c => c.Timeout = TimeSpan.FromSeconds(4));
            services.AddHttpClient<ITranslationService, CleanArchitectureBase.Infrastructure.Flashcards.MyMemoryTranslationService>(c => c.Timeout = TimeSpan.FromSeconds(5));
        }
        services.AddHttpClient<ISentenceTranslationService, CleanArchitectureBase.Infrastructure.Flashcards.LibreTranslateService>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<IModerationService, ModerationService>();

        services.AddSingleton<IGoogleAccessTokenProvider>(provider =>
        {
            var json = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS_JSON");
            return new GoogleAccessTokenProvider(json);
        });
        // Register Google Auth Service
        services.AddHttpClient<IGoogleAuthService, GoogleAuthService>();
        services.AddHttpClient<IAiGenerateService, AiGenerateService>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5); 
            }
        );

        services.AddAuthorization(options =>
            options.AddPolicy(Policies.CanPurge, policy => policy.RequireRole(Roles.Administrator)));

        return services;
    }
}
