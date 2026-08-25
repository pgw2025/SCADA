using System.Collections.Generic;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 页面及其全部组件（用于整树查询，避免多次往返）
    /// </summary>
    public class ScadaPageWithComponentsDto : ScadaPageDto
    {
        public List<HmiComponentDto> Components { get; set; } = new();
    }

    /// <summary>
    /// 工程整树：工程 + 全部页面（含各自组件）
    /// </summary>
    public class ScadaProjectFullDto
    {
        public ScadaProjectDto Project { get; set; }
        public List<ScadaPageWithComponentsDto> Pages { get; set; } = new();
    }
}
