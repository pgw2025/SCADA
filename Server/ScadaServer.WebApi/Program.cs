using ScadaServer.Application.Options;
using ScadaServer.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 配置系统数据库选项
builder.Services.Configure<SystemDbOptions>(builder.Configuration.GetSection(SystemDbOptions.SectionName));

// 添加认证服务（JWT + CORS + Swagger + Controllers）
builder.Services.AddAuthenticationServices(builder.Configuration);

// 添加数据库服务（SqlSugar + UnitOfWork + Repositories）
builder.Services.AddDatabaseServices();

// 添加应用层服务
builder.Services.AddApplicationServices();

// 添加基础设施服务（设备注册、协议工厂、Runtime等）
builder.Services.AddInfrastructureServices();

var app = builder.Build();

// 配置中间件管道
app.ConfigureMiddlewarePipeline();

// 执行启动初始化（数据库初始化、MQTT启动等）
await app.InitializeAsync();

app.Run();
