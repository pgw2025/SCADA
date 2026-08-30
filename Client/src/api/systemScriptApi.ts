import { http } from './http';
import { systemConfig } from '../store/configStore';
import { SystemScript, ScriptValidationResult, ScriptExecutionRecord } from '../types';

const base = () => `${systemConfig.value.backendApiUrl}/api/SystemScript`;

export const fetchSystemScripts = () => http.get(`${base()}`);

export const fetchSystemScript = (id: number) => http.get(`${base()}/${id}`);

export const createSystemScript = (dto: SystemScript) => http.post(`${base()}`, dto);

export const updateSystemScript = (dto: SystemScript) => http.put(`${base()}`, dto);

export const deleteSystemScript = (id: number) => http.delete(`${base()}/${id}`);

/** 静态校验（元数据 + 代码语法），不落库不执行。 */
export const validateSystemScript = (dto: SystemScript) =>
  http.post<ScriptValidationResult>(`${base()}/validate`, dto);

/** 手动执行脚本（服务端沙箱执行，返回执行结果含 log 输出）。 */
export const runSystemScript = (id: number) => http.post(`${base()}/${id}/run`);

/** 组态运行端触发脚本（HMI 按钮点击）：Operator/Admin 权限，与变量写入口径一致。 */
export const runScriptRuntime = (id: number) =>
  http.post(`${systemConfig.value.backendApiUrl}/api/ScriptRuntime/${id}/run`);

/** 试运行（dry-run）：不写真实变量、不落库、不更新熔断态。 */
export const testSystemScript = (dto: SystemScript, deviceKey?: string | null, variableKey?: string | null) =>
  http.post(`${base()}/test`, { script: dto, deviceKey, variableKey });

/** 人工复位熔断状态。 */
export const resetSystemScriptTripped = (id: number) =>
  http.post(`${base()}/${id}/reset-tripped`);

/** 按脚本分页查询执行记录（控制台追溯）。返回 { total, items }。 */
export const fetchSystemScriptRecords = (id: number, result?: string, pageIndex = 1, pageSize = 20) =>
  http.get<{ total: number; items: ScriptExecutionRecord[] }>(`${base()}/${id}/records`, {
    params: { result, pageIndex, pageSize }
  });