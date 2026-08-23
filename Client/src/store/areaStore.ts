import { ref } from 'vue';
import { Area } from '../types';

export const areas = ref<Area[]>([]);

export const setAreas = (data: Area[]) => {
  areas.value = data;
};
