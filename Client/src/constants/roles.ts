// 系统角色常量——前端唯一事实来源，必须与后端
// ScadaServer.Domain.Constants.SystemRoles 保持一致（大小写敏感）。
// 后端有 SystemRoles.All 白名单校验，任一侧拼写/大小写变动都会导致刷新后静默降级，
// 后续可加 CI 比对两处字面量。
export const ROLE_ADMIN = 'Admin';
export const ROLE_OPERATOR = 'Operator';
export const ROLE_VIEWER = 'Viewer';

// 仅管理员可访问的路由 meta.roles
export const ADMIN_ROLES = [ROLE_ADMIN];
