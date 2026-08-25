using ScadaServer.Application.Interfaces;
using ScadaServer.Application.DTOs;
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

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"] ?? "SUPER_SECRET_KEY_FOR_SCADA_SERVER_12345");
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim("id", user.Id.ToString())
                }),
                Expires = DateTime.UtcNow.AddDays(7),
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
                Status = entity.Status
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
                Status = entity.Status
            }).ToList();
        }

        public async Task CreateAsync(CreateUserDto dto)
        {
            // 用户名与初始密码校验
            if (string.IsNullOrWhiteSpace(dto.Username))
            {
                throw new BusinessException("用户名不能为空");
            }
            if (string.IsNullOrWhiteSpace(dto.Password))
            {
                throw new BusinessException("新建用户必须设置初始密码");
            }

            // 用户名唯一性
            var exists = await _repository.AnyAsync(u => u.Username == dto.Username);
            if (exists)
            {
                throw new BusinessException($"用户名 '{dto.Username}' 已存在");
            }

            var entity = new SystemUser
            {
                Username = dto.Username,
                Role = string.IsNullOrWhiteSpace(dto.Role) ? "Operator" : dto.Role,
                Status = string.IsNullOrWhiteSpace(dto.Status) ? "Active" : dto.Status
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

            entity.Username = dto.Username;
            entity.Role = dto.Role;
            entity.Status = dto.Status;
            await _repository.UpdateAsync(entity);
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
            }
        }
    }
}

