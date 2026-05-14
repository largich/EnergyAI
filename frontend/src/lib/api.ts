import type {
  AnomalyPoint,
  ClassCode,
  ComparisonResult,
  ConsumptionSeries,
  Equipment,
  Granularity,
  Measurement,
} from "./types";

const BASE = "/api/energy";

async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${res.status} ${res.statusText} — ${url}`);
  return res.json() as Promise<T>;
}

function toQuery(params: Record<string, string | number | undefined | null>): string {
  const parts: string[] = [];
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === "") continue;
    parts.push(`${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`);
  }
  return parts.length ? `?${parts.join("&")}` : "";
}

export interface ConsumptionFilters {
  from: string; // ISO
  to: string;   // ISO
  granularity: Granularity;
  equipment?: string;  // CSV
  classCode?: string;  // CSV
}

export const api = {
  classes: () => getJson<ClassCode[]>(`${BASE}/classes`),
  equipment: () => getJson<Equipment[]>(`${BASE}/equipment`),
  measurements: () => getJson<Measurement[]>(`${BASE}/measurements`),

  consumption: (f: ConsumptionFilters) =>
    getJson<ConsumptionSeries[]>(`${BASE}/consumption${toQuery({ ...f })}`),

  compare: (f: ConsumptionFilters & { previousFrom: string; previousTo: string }) =>
    getJson<ComparisonResult[]>(`${BASE}/consumption/compare${toQuery({ ...f })}`),

  anomalies: (f: ConsumptionFilters & { zThreshold?: number; deviationThreshold?: number }) =>
    getJson<AnomalyPoint[]>(`${BASE}/anomalies${toQuery({ ...f })}`),

  reportUrl: (f: ConsumptionFilters & { previousFrom?: string; previousTo?: string }) =>
    `${BASE}/reports/summary.xlsx${toQuery({ ...f })}`,
};
