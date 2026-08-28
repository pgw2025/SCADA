import { http } from './http';
import { systemConfig } from '../store/index';
import {
  DatabaseConfig,
  MainDatabaseConfig,
  TestConnectionRequest,
  TestConnectionResult,
  HistoryMigrationResult
} from '../types';

const base = () => `${systemConfig.value.backendApiUrl}/api/DatabaseConfig`;

// ===== 历史库 / 实时库配置（DatabaseConfigs 表）=====
export const fetchDatabaseConfigs = () => http.get(`${base()}`);

export const createDatabaseConfig = (dto: DatabaseConfig) => http.post(`${base()}`, dto);

export const updateDatabaseConfig = (dto: DatabaseConfig) => http.put(`${base()}`, dto);

export const deleteDatabaseConfig = (id: number) => http.delete(`${base()}/${id}`);

// ===== 主库（MySQL，自举依赖，override 文件）=====
export const fetchMainDatabaseConfig = () => http.get(`${base()}/main`);

export const saveMainDatabaseConfig = (dto: MainDatabaseConfig) => http.put(`${base()}/main`, dto);

// ===== 连接测试（通用）=====
export const testDatabaseConnection = (req: TestConnectionRequest) =>
  http.post<TestConnectionResult>(`${base()}/test-connection`, req);

// ===== 历史数据迁移（MySQL 存量 → 生效 InfluxDB 历史库）=====
export const migrateHistoryData = () =>
  http.post<HistoryMigrationResult>(`${systemConfig.value.backendApiUrl}/api/scada/history/migrate`);