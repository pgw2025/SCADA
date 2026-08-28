import { systemScripts } from '../store/configStore';
import {
  fetchSystemScripts,
  createSystemScript,
  updateSystemScript,
  deleteSystemScript,
  validateSystemScript,
  runSystemScript,
  testSystemScript,
  resetSystemScriptTripped,
  fetchSystemScriptRecords
} from '../api/systemScriptApi';
import { SystemScript, ScriptValidationResult, ScriptExecutionRecord } from '../types';

/**
 * 系统脚本服务端门面。
 * 执行一律走服务端 Jint 沙箱（不再在浏览器用 new Function 本地运行），
 * 本文件只负责：刷新列表 + 包装 CRUD / 校验 / 运行 / 试运行 API。
 */

/** 从后端拉取全部脚本并写回全局 store（TaskManagementView 的脚本下拉框也读它）。 */
export const loadSystemScripts = async (): Promise<SystemScript[]> => {
  const { data } = await fetchSystemScripts();
  systemScripts.value = data ?? [];
  return systemScripts.value;
};

/** 新建脚本。 */
export const saveNewScript = async (dto: SystemScript): Promise<SystemScript> => {
  const { data } = await createSystemScript(dto);
  await loadSystemScripts();
  return data ?? dto;
};

/** 更新已有脚本（保存时后端版本 +1 并复位熔断）。 */
export const persistScript = async (dto: SystemScript): Promise<void> => {
  await updateSystemScript(dto);
  await loadSystemScripts();
};

/** 删除脚本。 */
export const removeScript = async (id: number): Promise<void> => {
  await deleteSystemScript(id);
  await loadSystemScripts();
};

/** 静态校验（不落库不执行）。 */
export const validateScript = (dto: SystemScript): Promise<ScriptValidationResult> =>
  validateSystemScript(dto).then(r => r.data);

/** 手动执行脚本（服务端沙箱）。返回后端 ScriptEngineResult。 */
export const runScript = async (id: number): Promise<any> => {
  const { data } = await runSystemScript(id);
  return data;
};

/** 试运行（dry-run）。返回后端 ScriptEngineResult。 */
export const testScript = async (
  dto: SystemScript,
  deviceKey?: string | null,
  variableKey?: string | null
): Promise<any> => {
  const { data } = await testSystemScript(dto, deviceKey, variableKey);
  return data;
};

/** 人工复位熔断状态。 */
export const resetScriptTripped = async (id: number): Promise<void> => {
  await resetSystemScriptTripped(id);
  await loadSystemScripts();
};

/** 按脚本分页查询执行记录。返回 { total, items }。 */
export const queryScriptRecords = (
  id: number,
  result?: string,
  pageIndex = 1,
  pageSize = 20
): Promise<{ total: number; items: ScriptExecutionRecord[] }> =>
  fetchSystemScriptRecords(id, result, pageIndex, pageSize).then(r => r.data);