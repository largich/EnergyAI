import { useEffect, useState } from "react";
import type { ClassCode, Equipment, Granularity } from "../lib/types";
import { api } from "../lib/api";

export interface FilterState {
  from: string; // yyyy-MM-ddTHH:mm
  to: string;
  granularity: Granularity;
  classCode: string; // CSV, "" = all
  equipment: string; // CSV, "" = all
}

interface Props {
  value: FilterState;
  onChange: (next: FilterState) => void;
}

export function FilterBar({ value, onChange }: Props) {
  const [classes, setClasses] = useState<ClassCode[]>([]);
  const [equipment, setEquipment] = useState<Equipment[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [c, e] = await Promise.all([api.classes(), api.equipment()]);
        if (!cancelled) {
          setClasses(c);
          setEquipment(e);
        }
      } catch (err) {
        if (!cancelled) setLoadError(err instanceof Error ? err.message : "Failed to load filters");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const patch = (p: Partial<FilterState>) => onChange({ ...value, ...p });

  return (
    <section className="filter-bar card" aria-label="Filters">
      <div className="filter-grid">
        <label>
          <span>From (UTC)</span>
          <input
            type="datetime-local"
            value={value.from}
            onChange={(e) => patch({ from: e.target.value })}
          />
        </label>
        <label>
          <span>To (UTC)</span>
          <input
            type="datetime-local"
            value={value.to}
            onChange={(e) => patch({ to: e.target.value })}
          />
        </label>
        <label>
          <span>Granularity</span>
          <select
            value={value.granularity}
            onChange={(e) => patch({ granularity: e.target.value as Granularity })}
          >
            <option value="Hour">Hour</option>
            <option value="Day">Day</option>
            <option value="Week">Week</option>
            <option value="Month">Month</option>
          </select>
        </label>
        <label>
          <span>Medium (class)</span>
          <select
            value={value.classCode}
            onChange={(e) => patch({ classCode: e.target.value })}
          >
            <option value="">All</option>
            {classes.map((c) => (
              <option key={c.code} value={c.code}>
                {c.name ?? c.code}
              </option>
            ))}
          </select>
        </label>
        <label>
          <span>Equipment / location</span>
          <select
            value={value.equipment}
            onChange={(e) => patch({ equipment: e.target.value })}
          >
            <option value="">All</option>
            {equipment.map((e) => (
              <option key={e.code} value={e.code}>
                {e.code}
              </option>
            ))}
          </select>
        </label>
      </div>
      {loadError && (
        <div className="error-message" role="alert">
          Could not load filter options: {loadError}
        </div>
      )}
    </section>
  );
}
