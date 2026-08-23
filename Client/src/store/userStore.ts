import { ref } from 'vue';
import { SystemUser } from '../types';

export const systemUsers = ref<SystemUser[]>([]);
export const isAuthenticated = ref<boolean>(false);
export const loginUser = ref<{ username: string; role: string } | null>(null);
