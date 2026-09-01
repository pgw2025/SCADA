using System.Collections.Generic;

namespace ScadaServer.Application.DTOs
{
    /// <summary>
    /// 页面及其全部组件（用于整树查询，避免多次往返）
    /// </summary>
    public class ScadaPageWithComponentsDto : ScadaPageDto
    {
        /// <summary>该页面下的全部组件列表</summary>
        public List<HmiComponentDto> Components { get; set; } = new();
    }

    /// <summary>
    /// 工程整树：工程 + 全部页面（含各自组件）
    /// </summary>
    public class ScadaProjectFullDto
    {
        /// <summary>工程基本信息</summary>
        public ScadaProjectDto Project { get; set; } = null!;

        /// <summary>工程下的全部页面（含各自组件）</summary>
        public List<ScadaPageWithComponentsDto> Pages { get; set; } = new();
    }
}
