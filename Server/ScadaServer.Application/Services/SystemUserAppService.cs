using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
using ScadaServer.Domain.Constants;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Interfaces.Repositories;
using ScadaServer.Domain.Exceptions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;

namespace ScadaServer.Application.Services
{
    public class SystemUserAppService : ISystemUserAppService
    {
        private readonly ISystemUserRepository _repository;
        private readonly IConfiguration _configuration;

        public SystemUserAppService(ISystemUserRepository repository, IConfiguration configuration)
        {
            _repository = repository;
            _configuration = configuration;
        }

        public async Task<LoginResponseDto> LoginAsync(LoginDto loginDto)
        {
            var users = await _repository.GetListAsync(u => u.Username == loginDto.Username);
            var user = users.FirstOrDefault();

            var passwordHasher = new PasswordHasher<SystemUser>();

            if (user == null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, loginDto.Password) == PasswordVerificationResult.Failed)
            {
                return new LoginResponseDto { Success = false, Message = "Invalid username or password" };
            }

            // P1：账号被停用时禁止登录（置于密码校验之后，避免暴露账号是否存在）
            if (user.Status != "Active")
            {
                return new LoginResponseDto { Success = false, Message = "该账号已被停用，请联系管理员" };
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            // 与 WebApi 端一致：密钥必须来自配置（Jwt__Key），缺失即快速失败，禁止硬编码默认密钥。
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("未配置 Jwt:Key 签名密钥，无法签发 Token。"));
            // Token 有效期来自配置 Jwt:ExpireHours（小时），未配置/非法时默认 8 小时
            var expireRaw = _configuration["Jwt:ExpireHours"];
            var expireHours = double.TryParse(expireRaw, System.Globalization.CultureInfo.InvariantCulture, out var eh) ? eh : 8d;
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim("id", user.Id.ToString()),
                    // 短名 claim：前端 authApi 直接读 payload.username / payload.role，
                    // 避免解析 ClaimTypes 的长 URI claim（http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name）
                    new Claim("username", user.Username),
                    new Claim("role", user.Role)
                }),
                Expires = DateTime.UtcNow.AddHours(expireHours),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"]
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return new LoginResponseDto
            {
                Success = true,
                Token = tokenHandler.WriteToken(token),
                User = new SystemUserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Role = user.Role,
                    Status = user.Status
                }
            };
        }

        public async Task<SystemUserDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null) return null;
            return new SystemUserDto
            {
                Id = entity.Id,
                Username = entity.Username,
                Role = entity.Role,
                Status = entity.Status,
                CreatedAt = entity.CreatedAt
            };
        }

        public async Task<List<SystemUserDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(entity => new SystemUserDto
            {
                Id = entity.Id,
                Username = entity.Username,
                Role = entity.Role,
                Status = entity.Status,
                CreatedAt = entity.CreatedAt
            }).ToList();
        }

        public async Task CreateAsync(CreateUserDto dto)
        {
            // 用户名校验（去空格 + 长度约束，与 DB varchar(64)/唯一索引对应）
            var username = dto.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new BusinessException("用户名不能为空");
            }
            if (username.Length > 64)
            {
                throw new BusinessException("用户名长度不能超过64个字符");
            }

            // 密码策略校验（长度 + 组成）
            ValidatePassword(dto.Password);

            // 用户名唯一性
            var exists = await _repository.AnyAsync(u => u.Username == username);
            if (exists)
            {
                throw new BusinessException($"用户名 '{username}' 已存在");
            }

            var entity = new SystemUser
            {
                Username = username,
                Role = NormalizeRole(dto.Role, SystemRoles.Operator),
                Status = NormalizeStatus(dto.Status, "Active"),
                CreatedAt = DateTime.UtcNow
            };

            // 使用 PasswordHasher 哈希初始密码（与登录校验一致），保证 API 创建的用户可正常登录。
            var passwordHasher = new PasswordHasher<SystemUser>();
            entity.PasswordHash = passwordHasher.HashPassword(entity, dto.Password);

            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(SystemUserDto dto)
        {
            var entity = await _repository.GetByIdAsync(dto.Id);
            if (entity == null)
            {
                // 修复：原实现静默返回，前端提示成功但实际未更新。改为显式报错。
                throw new BusinessException($"ID 为 {dto.Id} 的用户不存在");
            }

            var username = dto.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new BusinessException("用户名不能为空");
            }
            if (username.Length > 64)
            {
                throw new BusinessException("用户名长度不能超过64个字符");
            }

            // 内置超级管理员保护：用户名、角色、状态三者均不可修改（即便绕过前端直接调 API 也被拒绝）
            if (entity.Username == "admin"
                && (username != "admin" || dto.Role != SystemRoles.Admin || dto.Status == "Inactive"))
            {
                throw new BusinessException("内置管理员 admin 的用户名、角色与状态不可修改");
            }

            // 用户名唯一性（排除自身）
            var dup = await _repository.AnyAsync(u => u.Username == username && u.Id != entity.Id);
            if (dup)
            {
                throw new BusinessException($"用户名 '{username}' 已存在");
            }

            entity.Username = username;
            var newRole = NormalizeRole(dto.Role, defaultValue: null);
            var newStatus = NormalizeStatus(dto.Status, defaultValue: null);

            // 最后管理员保护：将该用户从 Admin 降级前，须确保仍保留至少一名启用的管理员
            if (entity.Role == SystemRoles.Admin && newRole != SystemRoles.Admin)
            {
                var otherActiveAdmin = await _repository.AnyAsync(
                    u => u.Role == SystemRoles.Admin && u.Status == "Active" && u.Id != entity.Id);
                if (!otherActiveAdmin)
                {
                    throw new BusinessException("系统必须至少保留一名启用的管理员");
                }
            }

            entity.Role = newRole;
            entity.Status = newStatus;
            await _repository.UpdateAsync(entity);
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null)
            {
                // P1：删除不存在的用户不再静默成功，与 UpdateAsync 语义对齐
                throw new BusinessException($"ID 为 {id} 的用户不存在");
            }

            // 内置超级管理员保护：内置 admin 不可删除（即便前端兜底绕过直接调 API 也被拒绝）
            if (entity.Username == "admin")
            {
                throw new BusinessException("内置管理员 admin 不可删除");
            }

            // 最后管理员保护：删除 Admin 前，须确保仍保留至少一名启用的管理员
            if (entity.Role == SystemRoles.Admin)
            {
                var otherActiveAdmin = await _repository.AnyAsync(
                    u => u.Role == SystemRoles.Admin && u.Status == "Active" && u.Id != entity.Id);
                if (!otherActiveAdmin)
                {
                    throw new BusinessException("系统必须至少保留一名启用的管理员，禁止删除最后一名管理员");
                }
            }

            await _repository.DeleteAsync(entity);
        }

        public async Task ResetPasswordAsync(int id, string newPassword)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity == null)
            {
                throw new BusinessException($"ID 为 {id} 的用户不存在");
            }

            ValidatePassword(newPassword);
            entity.PasswordHash = new PasswordHasher<SystemUser>().HashPassword(entity, newPassword);
            await _repository.UpdateAsync(entity);
        }

        public async Task ChangePasswordAsync(int userId, string oldPassword, string newPassword)
        {
            var entity = await _repository.GetByIdAsync(userId);
            if (entity == null)
            {
                throw new BusinessException("用户不存在");
            }

            // 先验证旧密码
            var passwordHasher = new PasswordHasher<SystemUser>();
            var verify = passwordHasher.VerifyHashedPassword(entity, entity.PasswordHash, oldPassword);
            if (verify == PasswordVerificationResult.Failed)
            {
                throw new BusinessException("原密码不正确");
            }

            ValidatePassword(newPassword);
            entity.PasswordHash = passwordHasher.HashPassword(entity, newPassword);
            await _repository.UpdateAsync(entity);
        }

        /// <summary>
        /// 规范化角色值：必须位于 SystemRoles.All 白名单内，否则抛业务异常。
        /// <paramref name="defaultValue"/> 用于新建场景（空值回退到默认角色），更新场景传 null 强制必填。
        /// </summary>
        private static string NormalizeRole(string? role, string? defaultValue)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                if (defaultValue != null) return defaultValue;
                throw new BusinessException("角色不能为空");
            }

            var normalized = role.Trim();
            if (!SystemRoles.All.Contains(normalized))
            {
                throw new BusinessException($"无效的角色值: '{normalized}'，可选值: {string.Join("/", SystemRoles.All)}");
            }
            return normalized;
        }

        /// <summary>
        /// 规范化用户状态为 Active/Inactive。<paramref name="defaultValue"/> 语义同 <see cref="NormalizeRole"/>。
        /// </summary>
        private static string NormalizeStatus(string? status, string? defaultValue)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                if (defaultValue != null) return defaultValue;
                throw new BusinessException("用户状态不能为空");
            }

            var normalized = status.Trim();
            if (normalized is not ("Active" or "Inactive"))
            {
                throw new BusinessException($"无效的用户状态: '{normalized}'，可选值: Active/Inactive");
            }
            return normalized;
        }

        /// <summary>
        /// 密码策略校验：至少 8 位且同时包含字母与数字。新建、重置、自主修改共用同一规则。
        /// </summary>
        private static void ValidatePassword(string? password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                throw new BusinessException("密码长度至少为 8 位");
            }
            if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            {
                throw new BusinessException("密码必须同时包含字母和数字");
            }
        }
    }
}

