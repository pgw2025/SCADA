namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 批量启用/停用设备的执行结果（部分成功模型：逐台隔离，单台失败不中断整批）。
    /// </summary>
    public class BatchSetEnabledResultDto
    {
        /// <summary>本次操作 ID（便于日志关联 / 前端聚合）。</summary>
        public string? OperationId { get; set; }

        /// <summary>目标设备总数。</summary>
        public int Total { get; set; }

        /// <summary>成功数量。</summary>
        public int Succeeded { get; set; }

        /// <summary>跳过数量（已是目标状态，或预检阻塞且 skipInvalid=true）。</summary>
        public int Skipped { get; set; }

        /// <summary>失败数量。</summary>
        public int Failed { get; set; }

        /// <summary>逐台明细。</summary>
        public List<BatchSetEnabledItemDto> Items { get; set; } = new();
    }

    /// <summary>批量启停的单台结果明细。</summary>
    public class BatchSetEnabledItemDto
    {
        public int DeviceId { get; set; }

        public string? Name { get; set; }

        /// <summary>Succeeded / Skipped / Failed。</summary>
        public string Result { get; set; } = string.Empty;

        /// <summary>跳过或失败原因（成功时为空）。</summary>
        public string? Reason { get; set; }
    }

    /// <summary>批量启用前的预检结果（只校验不执行）。</summary>
    public class BatchSetEnabledPrecheckDto
    {
        /// <summary>目标设备总数。</summary>
        public int Total { get; set; }

        /// <summary>可启动数量。</summary>
        public int Startable { get; set; }

        /// <summary>被阻塞（如变量地址未配置）的设备清单。</summary>
        public List<BatchSetEnabledBlockedDto> Blocked { get; set; } = new();
    }

    /// <summary>预检被阻塞的单台设备。</summary>
    public class BatchSetEnabledBlockedDto
    {
        public int DeviceId { get; set; }

        public string? Name { get; set; }

        public string? Reason { get; set; }
    }
}
