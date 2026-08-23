import { HMIComponent } from '../types';
import { scadaProjects, selectedProjectId, selectedPageId } from '../store/scadaStore';

export const updateCurrentPageComponents = (newComponents: HMIComponent[]) => {
    const projIdx = scadaProjects.value.findIndex(p => p.id === selectedProjectId.value);
    if (projIdx === -1) return;
    const pageIdx = scadaProjects.value[projIdx].pages.findIndex(pg => pg.id === selectedPageId.value);
    if (pageIdx === -1) return;

    scadaProjects.value[projIdx].pages[pageIdx].components = [...newComponents];
};
