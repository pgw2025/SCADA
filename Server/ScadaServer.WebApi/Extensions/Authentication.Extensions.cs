using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ScadaServer.Application.DTOs;
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

            // Add CORS policy
            services.AddCors(options =>
            {
                var allowedOrigins = configuration.GetSection("AllowedCorsOrigins").Get<string[]>() ?? Array.Empty<string>();
                options.AddPolicy("AllowSpecificOrigins", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials()
                           .SetIsOriginAllowedToAllowWildcardSubdomains();

                    policy.SetIsOriginAllowed(origin =>
                    {
                        if (string.IsNullOrWhiteSpace(origin)) return false;
                        return origin.Contains("localhost") || origin.Contains("127.0.0.1") || origin.Contains("100.88.88.");
                    });
                });
            });

            // JWT Authentication Configuration
            var jwtKey = configuration["Jwt:Key"] ?? "SUPER_SECRET_KEY_FOR_SCADA_SERVER_12345";
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