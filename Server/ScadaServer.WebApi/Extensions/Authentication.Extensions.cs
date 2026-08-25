using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Converters;
using ScadaServer.Application.Options;
using System.Text;

namespace ScadaServer.WebApi.Extensions
{
    /// <summary>
    /// 认证与安全相关服务注册扩展
    /// </summary>
    public static partial class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加认证服务（JWT + CORS + Swagger）
        /// </summary>
        public static IServiceCollection AddAuthenticationServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Configure API behavior options
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    // 前后端设备类型枚举命名不一致（OpcUa ↔ "OPCUA"），统一转换
                    options.JsonSerializerOptions.Converters.Add(new DeviceTypeJsonConverter());
                    // 前后端数据类型枚举命名不一致（Boolean ↔ BOOL / Float ↔ FLOAT 等），统一转换
                    options.JsonSerializerOptions.Converters.Add(new DataTypeEnumJsonConverter());
                    // object 属性反序列化为 CLR 原始类型而非 JsonElement，避免驱动层类型转换失败
                    options.JsonSerializerOptions.Converters.Add(new ObjectClrTypeJsonConverter());
                })
                .ConfigureApiBehaviorOptions(options =>
                {
                    options.InvalidModelStateResponseFactory = context =>
                    {
                        var errors = context.ModelState
                            .Where(e => e.Value?.Errors.Count > 0)
                            .ToDictionary(
                                kvp => kvp.Key.Replace("$.", "").Replace("dto.", ""),
                                kvp => kvp.Value?.Errors.Select(e =>
                                {
                                    if (e.ErrorMessage.Contains("could not be converted"))
                                        return "数据格式或类型不正确";
                                    return e.ErrorMessage;
                                }).ToList()
                            );

                        var result = ApiResponse.Fail("数据校验失败", errors);
                        return new BadRequestObjectResult(result);
                    };
                });

            // CORS 策略：只放行显式配置的白名单来源（AllowedCorsOrigins）。
            // 移除 SetIsOriginAllowed 动态放行逻辑，避免任何来源都能携带凭证跨域访问。
            services.AddCors(options =>
            {
                var allowedOrigins = configuration.GetSection("AllowedCorsOrigins").Get<string[]>()
                                    ?? Array.Empty<string>();
                options.AddPolicy("AllowSpecificOrigins", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials();
                });
            });

            // JWT 签名密钥：仅来自配置（appsettings.json 开发占位 / 生产环境变量 Jwt__Key 覆盖）。
            // 严禁代码内硬编码默认密钥——缺失时快速失败，防止弱密钥或默认密钥上线。
            var jwtKey = configuration["Jwt:Key"]
                ?? throw new InvalidOperationException(
                    "未配置 Jwt:Key 签名密钥。开发环境使用 appsettings.json 的 Jwt:Key，生产环境必须通过环境变量 Jwt__Key 注入。");

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                };
            });

            // 全局授权策略：除显式标记 [AllowAnonymous] 的端点（登录、SignalR Hub）外，
            // 所有 API 默认必须携带有效 JWT 才能访问，避免“忘了加 [Authorize] 就裸奔”。
            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

            services.AddSignalR();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "ScadaServer API", Version = "v1" });

                // 添加 JWT 认证支持
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new string[] {}
                    }
                });
            });

            return services;
        }
    }
}