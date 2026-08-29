using ScadaServer.Application.DTOs;

namespace ScadaServer.Application.ImportExport;

/// <summary>
/// 模型变量导入解析器接口。
/// <para>实现必须无状态、仅从文件流解析出行，不查询数据库（冲突检测在应用层统一处理），
/// 因此可注册为 Singleton。解析失败不抛异常：非法行以 <see cref="VariableImportRow.HasError"/> 标记，
/// 保证"部分成功、整批不中断"。</para>
/// </summary>
public interface IVariableImportParser
{
    /// <summary>
    /// 从文件内容解析出变量导入行。
    /// </summary>
    /// <param name="fileStream">文件流（调用方负责读取与释放）</param>
    /// <param name="fileName">原始文件名（用于扩展名判定的兜底）</param>
    Task<List<VariableImportRow>> ParseAsync(Stream fileStream, string fileName);
}