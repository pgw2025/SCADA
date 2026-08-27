import { ref } from 'vue';
import { SystemUser } from '../types';

export const systemUsers = ref<SystemUser[]>([]);
export const isAuthenticated = ref<boolean>(false);
export const loginUser = ref<{ username: string; role: string } | null>(null);
// 认证初始化完成标志：initializeAuth（含回源 /me）结束后置 true，守卫据此等待，避免时序竞态
export const authInitialized = ref<boolean>(false);
