/**
 * PWA 缓存名 / API 白名单 / 排除清单（页面与 Service Worker 共用）。
 *
 * 安全模型（doc/pwa 阶段三 D4/D5）：
 * - 运行时仅对「只读业务 GET」做 NetworkFirst 缓存，管理面/鉴权/写操作一律不缓存；
 * - 白名单外的 /api、/open、非 GET 请求 SW 直接透传网络（不落缓存）；
 * - 跨账号清理由 CLEAR_USER_DATA 消息 + IndexedDB 命名空间双保险兜底（见 pwaSecurity / snapshotDB）。
 */

/** 缓存名常量（页面与 SW 必须一致） */
export const CACHE = {
  /** 预缓存（App Shell，由 Workbox 注入清单管理，CLEAR_USER_DATA 不动） */
  precache: 'precache',
  /** 运行时 API 响应缓存（NetworkFirst，换号/登出时清空） */
  apiRuntime: 'api-runtime',
  /** 运行时导航（HTML）缓存（NetworkFirst，换号/登出时清空） */
  htmlRuntime: 'html-runtime',
  /** 运行时同源静态资源兜底（CacheFirst，可选） */
  staticRuntime: 'static-runtime'
} as const;

/**
 * 可缓存的只读业务 API 前缀白名单（D4）。
 * 仅覆盖设备/变量/报警记录/组态工程与页面/区域/实时数据等业务只读 GET；
 * 管理面（用户/系统配置/系统日志/脚本/任务等）一律不进入白名单 → 永不缓存。
 */
export const CACHEABLE_API_PREFIXES: readonly string[] = [
  '/api/Devices', // 设备列表
  '/api/Device/', // 设备详情（带 id，避免误匹配 /api/DeviceConnection）
  '/api/DataPointMappings',
  '/api/AlarmRecords',
  '/api/ScadaPage',
  '/api/ScadaProject',
  '/api/Areas',
  '/api/RealtimeData',
  '/api/TelemetryData',
  '/api/HistoricalRecord',
  '/api/ModelVariable',
  '/api/DataModel',
  '/api/Sensor',
  '/api/HmiComponent',
  '/api/ExposedInterface',
  '/api/MqttServer'
];

/**
 * 显式排除清单（纵深防御）：即便未来有人误加白名单前缀，
 * 命中以下前缀的响应也绝不缓存（含鉴权、用户/权限、敏感管理面、写操作所在控制器）。
 */
export const EXCLUDED_API_PREFIXES: readonly string[] = [
  '/api/Auth', // 含 /me：缓存会让已吊销会话离线"复活"
  '/api/SystemUser', // 用户管理（含权限/角色）
  '/api/NotificationConfig', // 通知配置（含密钥）
  '/api/SystemLog', // 系统日志（敏感）
  '/api/ConfigLog', // 配置变更日志
  '/api/DatabaseConfig', // 数据库配置（敏感）
  '/api/SystemScript', // 脚本代码
  '/api/ScheduledTask', // 任务调度
  '/api/System/', // 系统状态等
  '/api/VariableTrigger', // 触发器配置
  '/api/DeviceConnection', // 连接参数（敏感）
  '/api/DeviceDataModel' // 绑定关系
];

/**
 * 判断某请求是否应进入 api-runtime 运行时缓存：
 * - 必须是 GET；
 * - 命中白名单前缀；
 * - 不命中排除前缀。
 */
export function isCacheableApi(url: string, method: string): boolean {
  if (method && method.toUpperCase() !== 'GET') return false;
  // 仅对同源 /api 路径生效
  if (!url.includes('/api/')) return false;
  if (EXCLUDED_API_PREFIXES.some((p) => url.startsWith(p))) return false;
  return CACHEABLE_API_PREFIXES.some((p) => url.startsWith(p));
}

/** 判断是否为导航请求（SPA 路由刷新/直链） */
export function isNavigationRequest(request: Request): boolean {
  return request.mode === 'navigate' || (request.headers.get('Accept')?.includes('text/html') ?? false);
}
