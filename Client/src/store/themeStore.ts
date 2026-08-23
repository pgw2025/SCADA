import { ref, watch } from 'vue';

export type ThemeMode = 'light' | 'dark';

const THEME_STORAGE_KEY = 'iota_scada_theme';

// Initialize theme from localStorage or default to 'light' (or system preference if set)
const getInitialTheme = (): ThemeMode => {
  if (typeof window !== 'undefined') {
    const saved = localStorage.getItem(THEME_STORAGE_KEY);
    if (saved === 'dark' || saved === 'light') {
      return saved;
    }
  }
  return 'light';
};

export const currentTheme = ref<ThemeMode>(getInitialTheme());

export const applyTheme = (theme: ThemeMode) => {
  currentTheme.value = theme;
  if (typeof window !== 'undefined') {
    localStorage.setItem(THEME_STORAGE_KEY, theme);
    const root = document.documentElement;
    if (theme === 'dark') {
      root.classList.add('dark');
    } else {
      root.classList.remove('dark');
    }
  }
};

export const setTheme = applyTheme;

export const toggleTheme = () => {
  const nextTheme = currentTheme.value === 'dark' ? 'light' : 'dark';
  applyTheme(nextTheme);
};

export const initTheme = () => {
  applyTheme(currentTheme.value);
};
