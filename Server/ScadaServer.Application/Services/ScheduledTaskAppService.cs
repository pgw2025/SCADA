using System.Text.Json;
using Cronos;
using ScadaServer.Application.DTOs;
using ScadaServer.Application.Interfaces;
using ScadaServer.Domain.Entities;
using ScadaServer.Domain.Enums;
using ScadaServer.Domain.Exceptions;
using ScadaServer.Domain.Interfaces.Repositories;

namespace ScadaServer.Application.Services
{
    /// <summary>
    /// 定时任务应用服务：CRUD + 保存前校验（Cron 表达式 / 任务类型白名单 / 按类型的参数完整性）。
    /// </summary>
    public class ScheduledTaskAppService : IScheduledTaskAppService
    {
        private readonly IScheduledTaskRepository _repository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly ISystemScriptRepository _scriptRepository;

        public ScheduledTaskAppService(
            IScheduledTaskRepository repository,
            IDeviceRepository deviceRepository,
            ISystemScriptRepository scriptRepository)
        {
            _repository = repository;
            _deviceRepository = deviceRepository;
            _scriptRepository = scriptRepository;
        }

        public async Task<ScheduledTaskDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return entity == null ? null : ToDto(entity);
        }

        public async Task<List<ScheduledTaskDto>> GetListAsync()
        {
            var list = await _repository.GetListAsync();
            return list.Select(ToDto).ToList();
        }

        public async Task CreateAsync(ScheduledTaskDto dto)
        {
            ValidateAndNormalize(dto);
            var entity = new ScheduledTask
            {
                Name = dto.Name,
                Type = dto.Type,
                CronExpression = dto.CronExpression,
                ParamsJson = dto.ParamsJson,
                Active = dto.Active,
                LastStatus = "Idle"
            };
            await _repository.InsertAsync(entity);
        }

        public async Task UpdateAsync(ScheduledTaskDto dto)
        {
            ValidateAndNormalize(dto);
            var entity = await _repository.GetByIdAsync(dto.Id)
                ?? throw new BusinessException($"ID 为 {dto.Id} 的定时任务不存在");

            entity.Name = dto.Name;
            entity.Type = dto.Type;
            entity.CronExpression = dto.CronExpression;
            entity.ParamsJson = dto.ParamsJson;
            entity.Active = dto.Active;
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

        // =============== 校验 ===============

        /// <summary>
        /// 保存前统一校验：名称、类型白名单、Cron 可解析、按类型的参数完整性（含外键存在性）。
        /// 校验失败抛 <see cref="BusinessException"/>，由全局异常中间件转为前端可读提示。
        /// </summary>
        private void ValidateAndNormalize(ScheduledTaskDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                throw new BusinessException("任务名称不能为空");
            }

            if (!ScheduledTaskTypes.All.Contains(dto.Type))
            {
                throw new BusinessException($"不支持的任务类型 '{dto.Type}'，合法值：{string.Join("、", ScheduledTaskTypes.All)}");
            }

            if (!TryParseCron(dto.CronExpression))
            {
                throw new BusinessException($"Cron 表达式 '{dto.CronExpression}' 无效（支持 5 段分钟级或 6 段秒级格式，如 '0 2 * * *' 或 '*/5 * * * * *'）");
            }

            if (string.IsNullOrWhiteSpace(dto.ParamsJson))
            {
                dto.ParamsJson = "{}";
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(dto.ParamsJson);
            }
            catch (JsonException ex)
            {
                throw new BusinessException($"任务参数不是合法 JSON: {ex.Message}");
            }

            using (doc)
            {
                var root = doc.RootElement;
                switch (dto.Type)
                {
                    case ScheduledTaskTypes.SetValue:
                        ValidateSetValue(root);
                        break;
                    case ScheduledTaskTypes.ExecuteScript:
                        ValidateExecuteScript(root);
                        break;
                    case ScheduledTaskTypes.ClearHistory:
                        ValidateClearHistory(root);
                        break;
                    // backup 无必填参数
                }
            }
        }

        private void ValidateSetValue(JsonElement root)
        {
            if (!root.TryGetProperty("deviceId", out var deviceIdEl) || deviceIdEl.ValueKind != JsonValueKind.Number)
            {
                throw new BusinessException("变量写入任务必须指定目标设备（deviceId，数字）");
            }
            if (!root.TryGetProperty("variableKey", out var varEl) || string.IsNullOrWhiteSpace(varEl.GetString()))
            {
                throw new BusinessException("变量写入任务必须指定目标变量（variableKey）");
            }
            if (!root.TryGetProperty("newValue", out var valueEl)
                || (valueEl.ValueKind != JsonValueKind.Number && valueEl.ValueKind != JsonValueKind.True && valueEl.ValueKind != JsonValueKind.False))
            {
                throw new BusinessException("变量写入任务必须指定写入值（newValue，数字或布尔）");
            }

            var deviceId = deviceIdEl.GetInt32();
            var device = _deviceRepository.GetByIdAsync(deviceId).GetAwaiter().GetResult();
            if (device == null)
            {
                throw new BusinessException($"变量写入任务的目标设备（ID={deviceId}）不存在");
            }
        }

        private void ValidateExecuteScript(JsonElement root)
        {
            if (!root.TryGetProperty("scriptId", out var scriptEl) || scriptEl.ValueKind != JsonValueKind.Number)
            {
                throw new BusinessException("脚本执行任务必须指定目标脚本（scriptId，数字）");
            }
            var scriptId = scriptEl.GetInt32();
            var script = _scriptRepository.GetByIdAsync(scriptId).GetAwaiter().GetResult();
            if (script == null)
            {
                throw new BusinessException($"脚本执行任务的目标脚本（ID={scriptId}）不存在");
            }
        }

        private static void ValidateClearHistory(JsonElement root)
        {
            if (!root.TryGetProperty("retentionDays", out var daysEl) || daysEl.ValueKind != JsonValueKind.Number)
            {
                throw new BusinessException("历史清理任务必须指定保留天数（retentionDays）");
            }
            var days = daysEl.GetInt32();
            if (days < 1)
            {
                throw new BusinessException("历史清理任务的保留天数必须 ≥ 1 天");
            }
        }

        /// <summary>
        /// 解析 Cron：兼容 6 段秒级与 5 段分钟级格式（与调度器一致）。
        /// </summary>
        internal static bool TryParseCron(string? expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }
            try
            {
                CronExpression.Parse(expression, CronFormat.IncludeSeconds);
                return true;
            }
            catch (CronFormatException)
            {
                try
                {
                    CronExpression.Parse(expression, CronFormat.Standard);
                    return true;
                }
                catch (CronFormatException)
                {
                    return false;
                }
            }
        }

        private static ScheduledTaskDto ToDto(ScheduledTask entity) => new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Type = entity.Type,
            CronExpression = entity.CronExpression,
            ParamsJson = entity.ParamsJson,
            Active = entity.Active,
            LastRunAt = entity.LastRunAt,
            LastStatus = entity.LastStatus,
            LastError = entity.LastError,
            LastDurationMs = entity.LastDurationMs,
            NextRunAt = entity.NextRunAt
        };
    }
}
